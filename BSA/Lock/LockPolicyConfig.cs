using System.Collections.Generic;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>
    /// One classification row. Match is a pipe-separated set of glob patterns (only meaningful for
    /// list-shaped actions - ParamWrite/MpSettingChange); single-shaped actions (FirmwareUpload etc.)
    /// leave Match null and just carry a Class. See LockActionMatcher.
    /// </summary>
    public class LockActionRule
    {
        public string Match { get; set; }
        public LockClass? Class { get; set; }
        public bool InvalidatesPreflight { get; set; }
    }

    /// <summary>One row per action id from the WP3 action catalog. List-shaped ids (ParamWrite,
    /// MpSettingChange) can carry multiple rules matched in order; single-shaped ids carry one rule
    /// with no Match (the action id itself is the whole match).</summary>
    public class LockPolicyActions
    {
        public List<LockActionRule> ParamWrite { get; set; } = new List<LockActionRule>();
        public LockActionRule ParamResetDefaults { get; set; }
        public LockActionRule FirmwareUpload { get; set; }
        public List<LockActionRule> MpSettingChange { get; set; } = new List<LockActionRule>();
        public LockActionRule MissionEdit { get; set; }
        public LockActionRule PreflightConfigEdit { get; set; }
        public LockActionRule LockPolicyEdit { get; set; }
    }

    /// <summary>One row of lock_policy.json. Default is the class for any action id/match not covered
    /// by a specific rule below - the doc's example ships Allow (uncontrolled actions proceed normally),
    /// matching WP1/WP2's default-deny-only-where-it-matters philosophy rather than default-deny
    /// everywhere.</summary>
    public class LockPolicyConfig
    {
        public int SchemaVersion { get; set; }
        public string PolicyVersion { get; set; }
        public LockClass? Default { get; set; }
        public LockPolicyActions Actions { get; set; } = new LockPolicyActions();
    }
}
