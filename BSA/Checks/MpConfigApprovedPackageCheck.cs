using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Stub for WP1.6's "MP config matches approved package" check. The real implementation needs WP2's
    /// CompareToPackage(), which doesn't exist yet (plan decision #4). Always returns NotApplicable with
    /// an explicit sentinel note - never a fake Pass/Fail, so a report can never be misread as having
    /// actually verified a package match.
    /// </summary>
    public static class MpConfigApprovedPackageCheck
    {
        public const string Key = "mpconfig.approvedPackageMatch";
        public const string PendingWp2Sentinel = "pending-wp2";

        public static IRegisteredCheck Create()
        {
            return new DelegateRegisteredCheck(Key, check =>
                (CheckOutcome.NotApplicable, $"Not evaluated: approved-package comparison requires WP2 ({PendingWp2Sentinel})."));
        }
    }
}
