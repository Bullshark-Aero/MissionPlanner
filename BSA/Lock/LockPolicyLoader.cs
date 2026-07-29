using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>Thrown for both malformed JSON and structurally-invalid (but well-formed) lock
    /// policies. Mirrors PreflightConfigException/KeyPolicyConfigException's contract.</summary>
    public class LockPolicyConfigException : Exception
    {
        public LockPolicyConfigException(string message) : base(message) { }
        public LockPolicyConfigException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Loads and structurally validates lock_policy.json - same fail-closed, aggregate-every-error
    /// shape as PreflightChecklistLoader/KeyPolicyLoader. All six single-shaped actions
    /// (ParamResetDefaults, FirmwareUpload, MissionEdit, PreflightConfigEdit, LockPolicyEdit - plus
    /// every ParamWrite/MpSettingChange row) must be explicitly present with a Class: an operational
    /// lock left partially configured (silently falling back to Default for something like firmware
    /// upload) is exactly the failure mode this loader exists to prevent.
    /// </summary>
    public static class LockPolicyLoader
    {
        public const int SupportedSchemaVersion = 1;

        public static LockPolicyConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new LockPolicyConfigException($"Lock policy not found: {path}");

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (!(ex is LockPolicyConfigException))
            {
                throw new LockPolicyConfigException($"Could not read lock policy '{path}': {ex.Message}", ex);
            }

            return Parse(text, path);
        }

        public static LockPolicyConfig Parse(string json, string sourceDescription = "<string>")
        {
            LockPolicyConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<LockPolicyConfig>(json);
            }
            catch (JsonException ex)
            {
                throw new LockPolicyConfigException($"Lock policy '{sourceDescription}' is not valid JSON: {ex.Message}", ex);
            }

            if (config == null)
                throw new LockPolicyConfigException($"Lock policy '{sourceDescription}' is empty.");

            var errors = Validate(config).ToList();
            if (errors.Count > 0)
                throw new LockPolicyConfigException(
                    $"Lock policy '{sourceDescription}' is invalid:" + Environment.NewLine +
                    "- " + string.Join(Environment.NewLine + "- ", errors));

            return config;
        }

        public static IEnumerable<string> Validate(LockPolicyConfig config)
        {
            if (config.SchemaVersion != SupportedSchemaVersion)
            {
                yield return $"Unsupported schema_version {config.SchemaVersion}; this build supports {SupportedSchemaVersion}.";
                yield break;
            }

            if (string.IsNullOrWhiteSpace(config.PolicyVersion))
                yield return "PolicyVersion is required.";

            if (config.Default == null)
                yield return "Default is required (Allow/Warn/Block/Authorise).";

            if (config.Actions == null)
            {
                yield return "Actions is required.";
                yield break;
            }

            foreach (var error in ValidateListAction("ParamWrite", config.Actions.ParamWrite))
                yield return error;
            foreach (var error in ValidateListAction("MpSettingChange", config.Actions.MpSettingChange))
                yield return error;

            foreach (var error in ValidateSingleAction("ParamResetDefaults", config.Actions.ParamResetDefaults))
                yield return error;
            foreach (var error in ValidateSingleAction("FirmwareUpload", config.Actions.FirmwareUpload))
                yield return error;
            foreach (var error in ValidateSingleAction("MissionEdit", config.Actions.MissionEdit))
                yield return error;
            foreach (var error in ValidateSingleAction("PreflightConfigEdit", config.Actions.PreflightConfigEdit))
                yield return error;
            foreach (var error in ValidateSingleAction("LockPolicyEdit", config.Actions.LockPolicyEdit))
                yield return error;
        }

        static IEnumerable<string> ValidateListAction(string actionId, List<LockActionRule> rules)
        {
            if (rules == null)
                yield break; // an empty/absent list is valid - matches nothing, falls to Default

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var label = $"Actions.{actionId}[{i}]";

                if (string.IsNullOrWhiteSpace(rule.Match))
                    yield return $"{label} is missing Match.";
                if (rule.Class == null)
                    yield return $"{label} is missing Class.";
            }
        }

        static IEnumerable<string> ValidateSingleAction(string actionId, LockActionRule rule)
        {
            if (rule == null)
            {
                yield return $"Actions.{actionId} is required.";
                yield break;
            }

            if (rule.Class == null)
                yield return $"Actions.{actionId} is missing Class.";
        }
    }
}
