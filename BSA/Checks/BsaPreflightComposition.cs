using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;
using MissionPlanner.Utilities;
using Newtonsoft.Json;

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

            registry.Register(MpConfigApprovedPackageCheck.Create(
                () => KeyPolicyLoader.Load(BsaConfigComposition.ResolveKeyPolicyPath()),
                () => BsaPaths.ApprovedConfigPackagePath));
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

        /// <summary>Metadata.ConfigVersion of a checklist file as a parseable Version, or null if the
        /// file/JSON/version is missing or malformed. Public for test visibility.</summary>
        public static Version ReadChecklistConfigVersion(string path)
        {
            try
            {
                var config = JsonConvert.DeserializeObject<PreflightChecklistConfig>(File.ReadAllText(path));
                return Version.TryParse(config?.Metadata?.ConfigVersion ?? "", out var version) ? version : null;
            }
            catch
            {
                return null;
            }
        }

        static bool _checklistStalenessNotified;

        /// <summary>
        /// The seeded user checklist is never overwritten once it exists (operator edits must survive
        /// upgrades), which means a newer shipped default - new checks included - is silently invisible
        /// to existing installs. This surfaces that, once per app session: tell the operator their local
        /// checklist is older and how to adopt the new default. Never blocks or fails a run.
        /// </summary>
        static void WarnIfSeededChecklistStale(string userPath)
        {
            if (_checklistStalenessNotified)
                return;

            try
            {
                var shippedPath = Path.Combine(Settings.GetRunningDirectory(), DefaultChecklistRelativePath);
                var userVersion = ReadChecklistConfigVersion(userPath);
                var shippedVersion = ReadChecklistConfigVersion(shippedPath);

                if (userVersion != null && shippedVersion != null && shippedVersion > userVersion)
                {
                    _checklistStalenessNotified = true;
                    CustomMessageBox.Show(
                        $"Your preflight checklist (v{userVersion}) is older than the shipped default (v{shippedVersion}).\n\n" +
                        "New default checks will not appear until it is updated. To adopt the shipped default, delete:\n" +
                        userPath + "\n" +
                        "and start BSA Preflight again. Local edits to that file will be lost - merge them manually if you have any.",
                        "BSA Preflight - checklist out of date");
                }
            }
            catch
            {
                // A staleness notice must never block or fail a preflight run (including headless
                // contexts where CustomMessageBox has no UI handler wired).
            }
        }

        public static PreflightRunEngine StartDefaultRun(string operatorName)
        {
            var missionBaselineHash = MissionSanityChecks.HashWaypoints(ToLocationwps(MainV2.comPort.MAV.wps));

            var registry = BuildRegistry(missionBaselineHash);
            var checklistPath = ResolveChecklistPath();
            WarnIfSeededChecklistStale(checklistPath);
            var config = PreflightChecklistLoader.Load(checklistPath, registry.Keys);
            var evaluator = BuildEvaluator();

            return BsaPreflightService.Instance.StartRun(config.Checks, evaluator, registry, operatorName, missionBaselineHash);
        }
    }
}
