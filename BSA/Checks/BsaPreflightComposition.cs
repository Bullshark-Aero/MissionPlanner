using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Composition root: the one place BSA code reaches for real Mission Planner globals
    /// (MainV2.comPort, Settings.Instance) to assemble a real run. Everything it wires together
    /// (providers, registry, engine) stays constructor-injected and independently testable - this class
    /// itself is what BSA/UI calls, matching the plan doc's "just call StartRun()" simplicity from the
    /// wizard's point of view.
    /// </summary>
    public static class BsaPreflightComposition
    {
        const string DefaultChecklistRelativePath = "BSA\\DefaultConfig\\preflight_checks.default.json";
        const string UserChecklistFileName = "preflight_checks.json";

        /// <summary>
        /// Returns the path to the user's editable checklist, seeding it from the shipped default on
        /// first run (mirrors Controls.PreFlight.CheckListControl's shipped-default / user-override
        /// two-tier pattern). Editing the seeded copy never touches the shipped default.
        /// </summary>
        public static string ResolveChecklistPath()
        {
            var userPath = Path.Combine(BsaPaths.ConfigDirectory, UserChecklistFileName);
            if (!File.Exists(userPath))
            {
                var shippedPath = Path.Combine(Settings.GetRunningDirectory(), DefaultChecklistRelativePath);
                Directory.CreateDirectory(BsaPaths.ConfigDirectory);
                File.Copy(shippedPath, userPath);
            }
            return userPath;
        }

        static Dictionary<int, Locationwp> ToLocationwps(IEnumerable<KeyValuePair<int, MAVLink.mavlink_mission_item_int_t>> source)
        {
            return source.ToDictionary(kv => kv.Key, kv => (Locationwp)kv.Value);
        }

        public static RegisteredCheckRegistry BuildRegistry(string missionBaselineHash)
        {
            var registry = new RegisteredCheckRegistry();

            foreach (var check in MissionSanityChecks.CreateAll(
                         () => ToLocationwps(MainV2.comPort.MAV.wps),
                         () => ToLocationwps(MainV2.comPort.MAV.fencepoints),
                         () => ToLocationwps(MainV2.comPort.MAV.rallypoints),
                         () => new PointLatLngAlt(MainV2.comPort.MAV.cs.lat, MainV2.comPort.MAV.cs.lng),
                         missionBaselineHash))
            {
                registry.Register(check);
            }

            registry.Register(MpConfigApprovedPackageCheck.Create());
            return registry;
        }

        public static AutoCheckEvaluator BuildEvaluator()
        {
            var providers = new Dictionary<CheckSource, IValueProvider>
            {
                [CheckSource.Telemetry] = new TelemetryValueProvider(MainV2.comPort.MAV.cs),
                [CheckSource.Param] = new ParamValueProvider(name =>
                    MainV2.comPort.MAV.param.ContainsKey(name)
                        ? (true, (double)MainV2.comPort.MAV.param[name].Value)
                        : (false, 0.0)),
                [CheckSource.MpConfig] = new SettingsValueProvider(key => Settings.Instance[key])
            };
            return new AutoCheckEvaluator(providers);
        }

        public static PreflightRunEngine StartDefaultRun(string operatorName)
        {
            var missionBaselineHash = MissionSanityChecks.HashWaypoints(ToLocationwps(MainV2.comPort.MAV.wps));

            var registry = BuildRegistry(missionBaselineHash);
            var config = PreflightChecklistLoader.Load(ResolveChecklistPath(), registry.Keys);
            var evaluator = BuildEvaluator();

            return BsaPreflightService.Instance.StartRun(config.Checks, evaluator, registry, operatorName, missionBaselineHash);
        }
    }
}
