using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using MissionPlanner.BSA.Core;
using MissionPlanner.Warnings;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Config
{
    public class BsaWarningOwnershipState
    {
        public string PackageId { get; set; }
        public string ProfileId { get; set; }
        public Dictionary<string, string> RuleFingerprints { get; set; } = new Dictionary<string, string>();
    }

    public class BsaWarningMergeResult
    {
        public List<CustomWarning> Warnings { get; set; } = new List<CustomWarning>();
        public BsaWarningOwnershipState Ownership { get; set; }
        public List<string> Conflicts { get; set; } = new List<string>();
        public int PreservedUnrelatedCount { get; set; }
    }

    /// <summary>Merges package-owned rules without removing or adopting unrelated operator warnings.</summary>
    public static class BsaWarningProfileAdapter
    {
        static readonly Dictionary<string, string> LegacyFieldByText = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Aircraft display data lost"] = "customfield13",
            ["ESC telemetry incomplete"] = "customfield14",
            ["GPS redundancy lost"] = "customfield15"
        };

        public static BsaWarningMergeResult Merge(IEnumerable<CustomWarning> existing,
            BsaWarningProfile profile, BsaWarningOwnershipState previous)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Rules == null) throw new InvalidDataException("Warning profile has no rules.");
            var live = (existing ?? Enumerable.Empty<CustomWarning>()).ToList();
            var result = new BsaWarningMergeResult
            {
                Ownership = new BsaWarningOwnershipState
                {
                    PackageId = profile.OwnerPackageId,
                    ProfileId = profile.ProfileId
                }
            };

            var removable = new HashSet<CustomWarning>();
            if (previous != null &&
                string.Equals(previous.PackageId, profile.OwnerPackageId, StringComparison.Ordinal) &&
                string.Equals(previous.ProfileId, profile.ProfileId, StringComparison.Ordinal))
            {
                foreach (var owned in previous.RuleFingerprints)
                {
                    var exact = live.FirstOrDefault(w => Fingerprint(w) == owned.Value);
                    if (exact != null) removable.Add(exact);
                    else if (live.Any(w => string.Equals(w.Text, RuleText(profile, owned.Key), StringComparison.Ordinal)))
                        result.Conflicts.Add("Owned warning '" + owned.Key + "' was edited locally.");
                }
            }

            // One-time adoption of only the exact reviewed v0.2.0 rules.
            foreach (var warning in live)
                if (IsExactLegacyWarning(warning)) removable.Add(warning);

            if (result.Conflicts.Count > 0) return result;
            result.Warnings.AddRange(live.Where(w => !removable.Contains(w)));
            result.PreservedUnrelatedCount = result.Warnings.Count;
            foreach (var rule in profile.Rules)
            {
                ValidateRule(rule);
                var warning = Render(rule);
                result.Warnings.Add(warning);
                result.Ownership.RuleFingerprints[rule.RuleId] = Fingerprint(warning);
            }
            return result;
        }

        public static byte[] SerializeWarnings(IReadOnlyCollection<CustomWarning> warnings)
        {
            var serializer = new XmlSerializer(typeof(List<CustomWarning>), new[] { typeof(CustomWarning) });
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, warnings.ToList());
                return stream.ToArray();
            }
        }

        public static byte[] SerializeOwnership(BsaWarningOwnershipState ownership) =>
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ownership, Formatting.Indented));

        public static BsaWarningOwnershipState ReadOwnership(string path) =>
            File.Exists(path) ? JsonConvert.DeserializeObject<BsaWarningOwnershipState>(File.ReadAllText(path)) : null;

        public static string Fingerprint(CustomWarning warning) => BsaHash.HashObject(new
        {
            warning.Name,
            warning.Warning,
            Condition = warning.ConditionType.ToString(),
            warning.Text,
            warning.RepeatTime,
            Type = warning.type.ToString(),
            Child = warning.Child == null ? null : new
            {
                warning.Child.Name,
                warning.Child.Warning,
                Condition = warning.Child.ConditionType.ToString(),
                warning.Child.Text,
                warning.Child.RepeatTime,
                Type = warning.Child.type.ToString()
            }
        });

        static CustomWarning Render(BsaWarningRule rule) => new CustomWarning
        {
            Name = rule.Condition.FieldId,
            Warning = rule.Condition.Value,
            ConditionType = ParseCondition(rule.Condition.Operator),
            Text = rule.Text,
            RepeatTime = rule.RepeatSeconds,
            type = CustomWarning.WarningType.SpeakAndText,
            Child = rule.ArmedOnly ? new CustomWarning
            {
                Name = "armed",
                Warning = 1,
                ConditionType = CustomWarning.Conditional.EQ,
                Text = rule.Text,
                RepeatTime = 0,
                type = CustomWarning.WarningType.SpeakAndText
            } : null
        };

        static void ValidateRule(BsaWarningRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.Text) ||
                rule.Condition == null || string.IsNullOrWhiteSpace(rule.Condition.FieldId) ||
                rule.Condition.FieldId.StartsWith("customfield", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Warning rules require an ID, text, and condition.");
            if (rule.RepeatSeconds < 0) throw new InvalidDataException("Warning repeat interval cannot be negative.");
            ParseCondition(rule.Condition.Operator);
        }

        static CustomWarning.Conditional ParseCondition(string value)
        {
            if (!Enum.TryParse(value, false, out CustomWarning.Conditional condition) || condition == CustomWarning.Conditional.NONE)
                throw new InvalidDataException("Unsupported warning condition '" + value + "'.");
            return condition;
        }

        static string RuleText(BsaWarningProfile profile, string ruleId) =>
            profile.Rules.FirstOrDefault(r => r.RuleId == ruleId)?.Text;

        static bool IsExactLegacyWarning(CustomWarning warning)
        {
            if (warning == null || !LegacyFieldByText.TryGetValue(warning.Text ?? string.Empty, out var field)) return false;
            return warning.Name == field && warning.Warning == 0.5 && warning.ConditionType == CustomWarning.Conditional.LT &&
                   warning.RepeatTime == 10 && warning.type == CustomWarning.WarningType.SpeakAndText &&
                   warning.Child != null && warning.Child.Name == "armed" && warning.Child.Warning == 1 &&
                   warning.Child.ConditionType == CustomWarning.Conditional.EQ;
        }
    }
}
