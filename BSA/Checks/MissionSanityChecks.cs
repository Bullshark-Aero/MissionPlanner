using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Mission sanity checks (WP1 T6), registered under "mission.*". Takes plain delegates for reading
    /// the currently-loaded mission/fence/rally/position rather than touching MainV2/MAVLinkInterface
    /// directly, so these are unit-testable against hand-built waypoint dictionaries. Distance math
    /// reuses PointLatLngAlt.GetDistance rather than a new formula.
    /// </summary>
    public static class MissionSanityChecks
    {
        public const string NonEmptyKey = "mission.nonempty";
        public const string HasTakeoffAndLandingKey = "mission.hasTakeoffAndLanding";
        public const string HomePlausibleKey = "mission.homePlausible";
        public const string UnchangedDuringPreflightKey = "mission.unchangedDuringPreflight";
        public const string FencePresentKey = "mission.fencePresent";
        public const string RallyPresentKey = "mission.rallyPresent";

        static readonly HashSet<ushort> TakeoffCommands = new HashSet<ushort>
        {
            (ushort)MAVLink.MAV_CMD.TAKEOFF,
            (ushort)MAVLink.MAV_CMD.VTOL_TAKEOFF
        };

        static readonly HashSet<ushort> LandingCommands = new HashSet<ushort>
        {
            (ushort)MAVLink.MAV_CMD.LAND,
            (ushort)MAVLink.MAV_CMD.VTOL_LAND
        };

        const double HomePlausibleMaxDistanceMeters = 5000;

        /// <param name="missionBaselineHash">
        /// Pre-computed via HashWaypoints() at run start (before this factory is called) - see
        /// PreflightRun.MissionBaselineHash. Passed as a plain value, not recomputed lazily, so the
        /// "unchanged" check compares against a value fixed at the moment the run began.
        /// </param>
        public static IEnumerable<IRegisteredCheck> CreateAll(
            Func<IReadOnlyDictionary<int, Locationwp>> getWaypoints,
            Func<IReadOnlyDictionary<int, Locationwp>> getFencePoints,
            Func<IReadOnlyDictionary<int, Locationwp>> getRallyPoints,
            Func<PointLatLngAlt> getCurrentPosition,
            string missionBaselineHash)
        {
            yield return new DelegateRegisteredCheck(NonEmptyKey,
                check => EvaluateNonEmpty(getWaypoints()));
            yield return new DelegateRegisteredCheck(HasTakeoffAndLandingKey,
                check => EvaluateHasTakeoffAndLanding(getWaypoints()));
            yield return new DelegateRegisteredCheck(HomePlausibleKey,
                check => EvaluateHomePlausible(getWaypoints(), getCurrentPosition()));
            yield return new DelegateRegisteredCheck(UnchangedDuringPreflightKey,
                check => EvaluateUnchanged(getWaypoints(), missionBaselineHash));
            yield return new DelegateRegisteredCheck(FencePresentKey,
                check => EvaluatePointsPresent(getFencePoints(), "fence"));
            yield return new DelegateRegisteredCheck(RallyPresentKey,
                check => EvaluatePointsPresent(getRallyPoints(), "rally"));
        }

        public static (CheckOutcome outcome, string detail) EvaluateNonEmpty(IReadOnlyDictionary<int, Locationwp> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0)
                return (CheckOutcome.Fail, "No mission is loaded.");
            return (CheckOutcome.Pass, $"{waypoints.Count} mission item(s) loaded.");
        }

        public static (CheckOutcome outcome, string detail) EvaluateHasTakeoffAndLanding(IReadOnlyDictionary<int, Locationwp> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0)
                return (CheckOutcome.Unknown, "No mission is loaded.");

            var commands = waypoints.Values.Select(w => (ushort)w.id).ToList();
            var hasTakeoff = commands.Any(c => TakeoffCommands.Contains(c));
            var hasLanding = commands.Any(c => LandingCommands.Contains(c));

            if (hasTakeoff && hasLanding)
                return (CheckOutcome.Pass, "Takeoff and landing commands both present.");

            var missing = new List<string>();
            if (!hasTakeoff) missing.Add("takeoff");
            if (!hasLanding) missing.Add("landing");
            return (CheckOutcome.Fail, "Mission is missing: " + string.Join(", ", missing) + ".");
        }

        public static (CheckOutcome outcome, string detail) EvaluateHomePlausible(
            IReadOnlyDictionary<int, Locationwp> waypoints, PointLatLngAlt currentPosition)
        {
            if (waypoints == null || !waypoints.TryGetValue(0, out var home))
                return (CheckOutcome.Unknown, "No home/item-0 waypoint found in the loaded mission.");

            if (currentPosition == null)
                return (CheckOutcome.Unknown, "Current GPS position is not available to compare against.");

            var distance = new PointLatLngAlt(home).GetDistance(currentPosition);

            if (distance > HomePlausibleMaxDistanceMeters)
                return (CheckOutcome.Fail,
                    $"Mission home is {distance:F0} m from the current position (threshold {HomePlausibleMaxDistanceMeters:F0} m).");

            return (CheckOutcome.Pass, $"Mission home is {distance:F0} m from the current position.");
        }

        public static (CheckOutcome outcome, string detail) EvaluateUnchanged(
            IReadOnlyDictionary<int, Locationwp> waypoints, string baselineHash)
        {
            if (string.IsNullOrEmpty(baselineHash))
                return (CheckOutcome.Unknown, "No mission baseline was captured at run start.");

            var currentHash = HashWaypoints(waypoints);
            return currentHash == baselineHash
                ? (CheckOutcome.Pass, "Mission unchanged since the preflight run started.")
                : (CheckOutcome.Fail, "Mission changed since the preflight run started.");
        }

        public static (CheckOutcome outcome, string detail) EvaluatePointsPresent(
            IReadOnlyDictionary<int, Locationwp> points, string label)
        {
            var count = points?.Count ?? 0;
            return count > 0
                ? (CheckOutcome.Pass, $"{count} {label} point(s) loaded.")
                : (CheckOutcome.Fail, $"No {label} points loaded.");
        }

        /// <summary>
        /// Stable hash of a mission's logical content, independent of ConcurrentDictionary enumeration
        /// order (sorted by sequence number before hashing - array order is semantically meaningful for
        /// a mission, unlike JSON object keys, which BsaHash already canonicalizes). Used both to capture
        /// the run-start baseline and, later, to re-check it - both call sites must use this exact
        /// function or the comparison is meaningless.
        /// </summary>
        public static string HashWaypoints(IReadOnlyDictionary<int, Locationwp> waypoints)
        {
            var normalized = (waypoints ?? new Dictionary<int, Locationwp>())
                .OrderBy(kv => kv.Key)
                .Select(kv => new { seq = kv.Key, kv.Value.id, kv.Value.lat, kv.Value.lng, kv.Value.alt, kv.Value.frame })
                .ToList();

            return BsaHash.HashObject(normalized);
        }
    }
}
