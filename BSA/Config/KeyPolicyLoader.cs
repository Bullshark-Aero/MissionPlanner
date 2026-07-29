using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Thrown for both malformed JSON and structurally-invalid (but well-formed) key policies. Always
    /// carries a human-readable message, mirroring PreflightConfigException's contract.
    /// </summary>
    public class KeyPolicyConfigException : Exception
    {
        public KeyPolicyConfigException(string message) : base(message) { }
        public KeyPolicyConfigException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Loads and structurally validates bsa_key_policy.json - same fail-closed, aggregate-every-error
    /// shape as PreflightChecklistLoader (BSA/Core/PreflightChecklistLoader.cs), applied to the key
    /// policy schema instead. A malformed or hand-edited-wrong policy must refuse to load, never fall
    /// back to a partial/best-effort classification (that would risk exporting an unclassified key).
    /// </summary>
    public static class KeyPolicyLoader
    {
        public const int SupportedSchemaVersion = 1;

        public static KeyPolicyConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new KeyPolicyConfigException($"Key policy not found: {path}");

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (!(ex is KeyPolicyConfigException))
            {
                throw new KeyPolicyConfigException($"Could not read key policy '{path}': {ex.Message}", ex);
            }

            return Parse(text, path);
        }

        public static KeyPolicyConfig Parse(string json, string sourceDescription = "<string>")
        {
            KeyPolicyConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<KeyPolicyConfig>(json);
            }
            catch (JsonException ex)
            {
                throw new KeyPolicyConfigException($"Key policy '{sourceDescription}' is not valid JSON: {ex.Message}", ex);
            }

            if (config == null)
                throw new KeyPolicyConfigException($"Key policy '{sourceDescription}' is empty.");

            var errors = Validate(config).ToList();
            if (errors.Count > 0)
                throw new KeyPolicyConfigException(
                    $"Key policy '{sourceDescription}' is invalid:" + Environment.NewLine +
                    "- " + string.Join(Environment.NewLine + "- ", errors));

            return config;
        }

        /// <summary>Yields every problem found, not just the first.</summary>
        public static IEnumerable<string> Validate(KeyPolicyConfig config)
        {
            if (config.SchemaVersion != SupportedSchemaVersion)
            {
                yield return $"Unsupported schema_version {config.SchemaVersion}; this build supports {SupportedSchemaVersion}.";
                yield break;
            }

            if (config.Default == null)
                yield return "Default is required (Portable/MachineSpecific/Secret/Volatile).";

            var rules = config.Rules ?? new List<KeyPolicyRule>();
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var label = $"Rules[{i}]";

                if (string.IsNullOrWhiteSpace(rule.Match))
                    yield return $"{label} is missing Match.";

                if (rule.Class == null)
                    yield return $"{label} is missing Class (Portable/MachineSpecific/Secret/Volatile).";
            }
        }
    }
}
