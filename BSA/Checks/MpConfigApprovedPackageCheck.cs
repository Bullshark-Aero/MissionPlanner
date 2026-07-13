using System;
using System.IO;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// WP1.6's "MP config matches approved package" check - was a permanent NotApplicable stub pending
    /// WP2 (plan decision #4); now backed by WP2's real compare engine. Still resolves NotApplicable,
    /// not a fake Pass/Fail, until an operator has designated an approved package on this machine
    /// (BsaPaths.ApprovedConfigPackagePath) - that designation is itself a WP2 Phase A stopgap, not the
    /// org-level "who signs an approved package" decision the WP2 doc still flags as open.
    /// </summary>
    public static class MpConfigApprovedPackageCheck
    {
        public const string Key = "mpconfig.approvedPackageMatch";

        public static IRegisteredCheck Create(Func<KeyPolicyConfig> loadPolicy, Func<string> approvedPackagePathProvider)
        {
            return new DelegateRegisteredCheck(Key, check =>
            {
                var path = approvedPackagePathProvider();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return (CheckOutcome.NotApplicable, "No approved MP config package has been designated on this machine yet.");

                var policy = loadPolicy();
                var result = ConfigCompareEngine.CompareToPackage(path, policy);

                return result.IsMatch
                    ? (CheckOutcome.Pass, "Live MP config matches the approved package.")
                    : (CheckOutcome.Fail,
                        $"Live MP config differs from the approved package: {result.MismatchedKeys.Count} changed, " +
                        $"{result.LiveOnlyKeys.Count} added, {result.PackageOnlyKeys.Count} missing.");
            });
        }
    }
}
