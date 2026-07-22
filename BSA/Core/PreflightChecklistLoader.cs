using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Thrown for both malformed JSON and structurally-invalid (but well-formed) checklists. Always
    /// carries a human-readable message a checklist author can act on without reading a stack trace.
    /// </summary>
    public class PreflightConfigException : Exception
    {
        public PreflightConfigException(string message) : base(message) { }
        public PreflightConfigException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Loads and structurally validates preflight_checks.default.json (or a user override). Validation
    /// is deliberately strict and fails closed - a malformed or hand-edited-wrong config must refuse to
    /// start a run, never fall back to a partial/best-effort interpretation.
    /// </summary>
    public static class PreflightChecklistLoader
    {
        public const int SupportedSchemaVersion = 1;

        /// <param name="knownRegisteredCheckKeys">
        /// Keys registered in RegisteredCheckRegistry. Passed in (rather than referenced directly) so
        /// this loader has no dependency on the Checks layer. Pass null to skip that one validation
        /// (still validates everything else) - useful for tests that only care about schema shape.
        /// </param>
        public static PreflightChecklistConfig Load(string path, IEnumerable<string> knownRegisteredCheckKeys = null)
        {
            if (!File.Exists(path))
                throw new PreflightConfigException($"Preflight checklist not found: {path}");

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (!(ex is PreflightConfigException))
            {
                throw new PreflightConfigException($"Could not read preflight checklist '{path}': {ex.Message}", ex);
            }

            return Parse(text, knownRegisteredCheckKeys, path);
        }

        public static PreflightChecklistConfig Parse(string json, IEnumerable<string> knownRegisteredCheckKeys = null,
            string sourceDescription = "<string>")
        {
            PreflightChecklistConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<PreflightChecklistConfig>(json);
            }
            catch (JsonException ex)
            {
                throw new PreflightConfigException($"Preflight checklist '{sourceDescription}' is not valid JSON: {ex.Message}", ex);
            }

            if (config == null)
                throw new PreflightConfigException($"Preflight checklist '{sourceDescription}' is empty.");

            var errors = Validate(config, knownRegisteredCheckKeys).ToList();
            if (errors.Count > 0)
                throw new PreflightConfigException(
                    $"Preflight checklist '{sourceDescription}' is invalid:" + Environment.NewLine +
                    "- " + string.Join(Environment.NewLine + "- ", errors));

            return config;
        }

        /// <summary>
        /// Yields every problem found (not just the first) so a checklist author can fix a config in
        /// one pass instead of one error at a time.
        /// </summary>
        public static IEnumerable<string> Validate(PreflightChecklistConfig config, IEnumerable<string> knownRegisteredCheckKeys = null)
        {
            if (config.SchemaVersion != SupportedSchemaVersion)
            {
                yield return $"Unsupported schema_version {config.SchemaVersion}; this build supports {SupportedSchemaVersion}.";
                yield break;
            }

            if (config.Metadata == null || string.IsNullOrWhiteSpace(config.Metadata.Name))
                yield return "Metadata.Name is required.";

            if (config.Checks == null || config.Checks.Count == 0)
            {
                yield return "Checks must contain at least one entry.";
                yield break;
            }

            var metadata = config.Metadata ?? new PreflightChecklistMetadata();

            if (metadata.PageSize < 1)
                yield return $"Metadata.PageSize must be at least 1 (was {metadata.PageSize}).";
            if (metadata.AutoPageSize < 1)
                yield return $"Metadata.AutoPageSize must be at least 1 (was {metadata.AutoPageSize}).";

            var declaredGroups = metadata.Groups ?? new List<string>();
            var hasDeclaredGroups = declaredGroups.Count > 0;

            if (hasDeclaredGroups)
            {
                var seenGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in declaredGroups)
                {
                    if (string.IsNullOrWhiteSpace(group))
                        yield return "Metadata.Groups contains a blank group name.";
                    else if (!seenGroupNames.Add(group))
                        yield return $"Metadata.Groups contains a duplicate group name '{group}'.";
                }

                if (metadata.AutoChecksFirst && !string.IsNullOrWhiteSpace(metadata.AutoGroupTitle) &&
                    declaredGroups.Any(g => string.Equals(g, metadata.AutoGroupTitle, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return $"Metadata.Groups contains '{metadata.AutoGroupTitle}', which collides with " +
                                 "Metadata.AutoGroupTitle (the synthetic page auto checks are hoisted onto).";
                }
            }

            var declaredGroupSet = new HashSet<string>(declaredGroups, StringComparer.OrdinalIgnoreCase);
            var knownKeys = knownRegisteredCheckKeys == null
                ? null
                : new HashSet<string>(knownRegisteredCheckKeys, StringComparer.OrdinalIgnoreCase);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var check in config.Checks)
            {
                foreach (var error in ValidateCheck(check, seenIds, knownKeys))
                    yield return error;

                foreach (var error in ValidateGroup(check, hasDeclaredGroups, declaredGroupSet))
                    yield return error;
            }
        }

        static IEnumerable<string> ValidateGroup(PreflightCheckDefinition check, bool hasDeclaredGroups, HashSet<string> declaredGroupSet)
        {
            var label = string.IsNullOrWhiteSpace(check.Id) ? (check.Title ?? "<unnamed check>") : check.Id;

            if (hasDeclaredGroups)
            {
                if (string.IsNullOrWhiteSpace(check.Group))
                    yield return $"Check '{label}' has no Group, but Metadata.Groups is declared - every check needs one.";
                else if (!declaredGroupSet.Contains(check.Group))
                    yield return $"Check '{label}' references group '{check.Group}', which is not in Metadata.Groups.";
            }
            else if (!string.IsNullOrWhiteSpace(check.Group))
            {
                yield return $"Check '{label}' sets Group '{check.Group}', but Metadata.Groups is not declared - " +
                             "either declare Metadata.Groups for the whole checklist or remove Group from every check.";
            }
        }

        static IEnumerable<string> ValidateCheck(PreflightCheckDefinition check, HashSet<string> seenIds, HashSet<string> knownKeys)
        {
            var label = string.IsNullOrWhiteSpace(check.Id) ? (check.Title ?? "<unnamed check>") : check.Id;

            if (string.IsNullOrWhiteSpace(check.Id))
                yield return $"A check (title '{check.Title}') is missing an id.";
            else if (!seenIds.Add(check.Id))
                yield return $"Duplicate check id '{check.Id}'.";

            if (string.IsNullOrWhiteSpace(check.Title))
                yield return $"Check '{label}' is missing a title.";

            if (check.Severity == null)
                yield return $"Check '{label}' is missing Severity (Critical/Warning/Info).";

            if (check.Type == null)
            {
                yield return $"Check '{label}' is missing Type (Manual/Auto/Semi).";
                yield break; // nothing type-specific can be validated without knowing the type
            }

            if (check.Type == CheckType.Manual || check.Type == CheckType.Semi)
            {
                if (string.IsNullOrWhiteSpace(check.Instruction))
                    yield return $"Check '{label}' is type {check.Type} and needs Instruction text.";
            }

            if (check.Type == CheckType.Auto || check.Type == CheckType.Semi)
            {
                if (check.Source == null)
                {
                    yield return $"Check '{label}' is type {check.Type} and needs Source.";
                    yield break;
                }

                var hasGenericShape = !string.IsNullOrWhiteSpace(check.Field) && check.Condition != null && check.Value != null;
                var hasRegisteredShape = !string.IsNullOrWhiteSpace(check.Check);

                if (hasGenericShape == hasRegisteredShape)
                {
                    yield return $"Check '{label}' must set exactly one of (Field+Condition+Value) or Check, not " +
                                 (hasGenericShape ? "both." : "neither.");
                }
                else if (hasRegisteredShape && knownKeys != null && !knownKeys.Contains(check.Check))
                {
                    yield return $"Check '{label}' references unknown registered check '{check.Check}'.";
                }
            }
        }
    }
}
