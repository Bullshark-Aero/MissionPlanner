using System;
using System.Collections.Generic;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Writes an operator-approved set of key/value pairs into a live config dictionary. Takes the
    /// dictionary as an injected, mutable IDictionary (not a direct Settings.config reference) so this
    /// is unit-testable with a plain fake, mirroring BsaConfigExporter's injection pattern - the real
    /// Settings.config is wired in only at BsaConfigComposition, the composition root. Never touches
    /// Settings.Instance.Save() itself - persistence is the composition root's job, since it also has
    /// to handle the concurrent-write retry (see BsaConfigComposition's doc comment).
    /// </summary>
    public static class ConfigApplier
    {
        /// <returns>The keys whose value actually changed - excludes no-op writes where the approved
        /// value was already identical, so callers can report an accurate "N settings applied" count.</returns>
        public static List<string> Apply(IDictionary<string, string> liveConfig, IReadOnlyDictionary<string, string> approvedValues)
        {
            if (liveConfig == null) throw new ArgumentNullException(nameof(liveConfig));
            if (approvedValues == null) throw new ArgumentNullException(nameof(approvedValues));

            var changed = new List<string>();
            foreach (var kv in approvedValues)
            {
                if (!liveConfig.TryGetValue(kv.Key, out var currentValue) || !string.Equals(currentValue, kv.Value, StringComparison.Ordinal))
                {
                    liveConfig[kv.Key] = kv.Value;
                    changed.Add(kv.Key);
                }
            }

            return changed;
        }
    }
}
