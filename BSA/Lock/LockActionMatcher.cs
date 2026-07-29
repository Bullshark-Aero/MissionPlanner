using System.Collections.Generic;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>The evaluated outcome for one action check - Class drives BLOCK/WARN/ALLOW/AUTHORISE
    /// behavior, InvalidatesPreflight only matters when the action actually proceeds (Allow, or Warn/
    /// Authorise after being let through).</summary>
    public class LockDecision
    {
        public LockClass Class { get; }
        public bool InvalidatesPreflight { get; }

        public LockDecision(LockClass @class, bool invalidatesPreflight)
        {
            Class = @class;
            InvalidatesPreflight = invalidatesPreflight;
        }
    }

    /// <summary>
    /// Resolves a policy decision for one action. List-shaped actions (ParamWrite, MpSettingChange) are
    /// matched by name via GlobMatcher (first rule wins, else policy.Default); single-shaped actions
    /// (FirmwareUpload etc.) just return their one configured rule directly - LockPolicyLoader
    /// guarantees all six are present with a Class, so ResolveSingle's Default fallback is defensive,
    /// not a real code path for a validated policy.
    /// </summary>
    public static class LockActionMatcher
    {
        public static LockDecision MatchParamWrite(string paramName, LockPolicyConfig policy) =>
            MatchList(paramName, policy?.Actions?.ParamWrite, policy);

        public static LockDecision MatchMpSettingChange(string transition, LockPolicyConfig policy) =>
            MatchList(transition, policy?.Actions?.MpSettingChange, policy);

        public static LockDecision ResolveSingle(LockActionRule rule, LockPolicyConfig policy)
        {
            if (rule?.Class == null)
                return new LockDecision(policy?.Default ?? LockClass.Allow, false);

            return new LockDecision(rule.Class.Value, rule.InvalidatesPreflight);
        }

        static LockDecision MatchList(string value, List<LockActionRule> rules, LockPolicyConfig policy)
        {
            if (!string.IsNullOrEmpty(value) && rules != null)
            {
                foreach (var rule in rules)
                {
                    if (GlobMatcher.MatchesAny(value, rule.Match))
                        return new LockDecision(rule.Class ?? policy?.Default ?? LockClass.Allow, rule.InvalidatesPreflight);
                }
            }

            return new LockDecision(policy?.Default ?? LockClass.Allow, false);
        }
    }
}
