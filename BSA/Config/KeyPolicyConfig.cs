using System.Collections.Generic;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// One classification rule. Match is a pipe-separated set of glob patterns ("*"/"?"), matched
    /// case-insensitively against a Settings.config key name - see KeyClassifier.
    /// </summary>
    public class KeyPolicyRule
    {
        public string Match { get; set; }
        public KeyClass? Class { get; set; }
    }

    /// <summary>
    /// One row of bsa_key_policy.default.json (or a user override). Default-deny: a key matching no
    /// rule falls to Default, which should be MachineSpecific/Secret, never Portable - an unclassified
    /// upstream key must never silently leak into an exported package.
    /// </summary>
    public class KeyPolicyConfig
    {
        public int SchemaVersion { get; set; }
        public List<KeyPolicyRule> Rules { get; set; } = new List<KeyPolicyRule>();
        public KeyClass? Default { get; set; }
    }
}
