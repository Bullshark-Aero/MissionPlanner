using System.Collections.Generic;

namespace MissionPlanner.BSA.Config
{
    /// <summary>The reviewed Judicar 2600 first-hover GCS profile.</summary>
    public static class Judicar2600BundleProfile
    {
        public const string PackageId = "aero.bullshark.judicar2600.first-hover";

        public static readonly string[] NamedFields =
        {
            "MAV_VTOL_RES", "MAV_VTOL_MAR", "MAV_AV_RES", "MAV_AV_MAR", "MAV_ESC_HOT",
            "MAV_CHT_HOT", "MAV_FR_MOT_T", "MAV_LIFT_HDR", "MAV_SURF_HDR", "MAV_ATT_ERR5",
            "MAV_ALT_ERR5", "MAV_LIDAR_M"
        };

        public static BsaBundleProfile Create(BsaQuickViewProfile quickView)
        {
            var bindings = new BsaTelemetryBindings();
            foreach (var field in NamedFields)
            {
                bindings.Bindings.Add(new BsaTelemetryBinding
                {
                    FieldId = field,
                    SourceKind = "NAMED_VALUE_FLOAT",
                    ExpectedCadenceHz = 1,
                    FreshnessSeconds = 5,
                    Supported = true
                });
            }

            var health = new BsaHealthRuleSet { EvaluationHz = 4 };
            health.Rules.Add(new BsaHealthRule
            {
                RuleId = "judicar-display-data",
                Kind = "any-named-value-fresh",
                OutputFieldId = "J26_DATA_OK",
                FreshnessSeconds = 5,
                ArmedGraceSeconds = 5,
                InputFieldIds = new List<string>(NamedFields)
            });
            health.Rules.Add(new BsaHealthRule
            {
                RuleId = "judicar-esc-summary",
                Kind = "finite-named-value-fresh",
                OutputFieldId = "J26_ESC_OK",
                FreshnessSeconds = 5,
                ArmedGraceSeconds = 5,
                InputFieldIds = new List<string> { "MAV_ESC_HOT" }
            });
            health.Rules.Add(new BsaHealthRule
            {
                RuleId = "judicar-gps-redundancy",
                Kind = "not-exactly-one-gps-fix",
                OutputFieldId = "J26_GPS_RED_OK",
                ArmedGraceSeconds = 5,
                InputFieldIds = new List<string> { "gpsstatus", "gpsstatus2" }
            });

            return new BsaBundleProfile
            {
                QuickView = quickView,
                TelemetryBindings = bindings,
                Warnings = new BsaWarningProfile
                {
                    ProfileId = "judicar2600-first-hover-health-v1",
                    OwnerPackageId = PackageId,
                    ApplyMode = "replace-owned",
                    Rules = new List<BsaWarningRule>
                    {
                        Warning("judicar-display-data-lost", "Aircraft display data lost", "J26_DATA_OK"),
                        Warning("judicar-esc-telemetry-incomplete", "ESC telemetry incomplete", "J26_ESC_OK"),
                        Warning("judicar-gps-redundancy-lost", "GPS redundancy lost", "J26_GPS_RED_OK")
                    }
                },
                HealthRules = health
            };
        }

        static BsaWarningRule Warning(string id, string text, string field) => new BsaWarningRule
        {
            RuleId = id,
            Text = text,
            RepeatSeconds = 10,
            ArmedOnly = true,
            Condition = new BsaWarningCondition { FieldId = field, Operator = "LT", Value = 0.5 },
            RequiredFieldIds = new List<string> { field, "armed" }
        };
    }
}
