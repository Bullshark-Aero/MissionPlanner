using System.Collections.Generic;

namespace MissionPlanner.BSA.Config
{
    public class BsaQuickViewProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public int Rows { get; set; }
        public int Columns { get; set; }
        public List<BsaQuickViewCell> Cells { get; set; } = new List<BsaQuickViewCell>();
    }

    public class BsaQuickViewCell
    {
        public int Position { get; set; }
        public string SourceId { get; set; }
        public string Label { get; set; }
        public string NumberFormat { get; set; }
        public string LabelColor { get; set; }
        public string ValueColor { get; set; }
        public bool Visible { get; set; } = true;
    }

    public class BsaTelemetryBindings
    {
        public int SchemaVersion { get; set; } = 1;
        public List<BsaTelemetryBinding> Bindings { get; set; } = new List<BsaTelemetryBinding>();
    }

    public class BsaTelemetryBinding
    {
        public string FieldId { get; set; }
        public string SourceKind { get; set; }
        public string Units { get; set; }
        public double ExpectedCadenceHz { get; set; }
        public double FreshnessSeconds { get; set; }
        public List<string> Aliases { get; set; } = new List<string>();
        public bool Supported { get; set; }
    }

    public class BsaWarningProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProfileId { get; set; }
        public string OwnerPackageId { get; set; }
        public string ApplyMode { get; set; }
        public List<BsaWarningRule> Rules { get; set; } = new List<BsaWarningRule>();
    }

    public class BsaWarningRule
    {
        public string RuleId { get; set; }
        public string Text { get; set; }
        public int RepeatSeconds { get; set; }
        public bool ArmedOnly { get; set; }
        public BsaWarningCondition Condition { get; set; }
        public List<string> RequiredFieldIds { get; set; } = new List<string>();
    }

    public class BsaWarningCondition
    {
        public string FieldId { get; set; }
        public string Operator { get; set; }
        public double Value { get; set; }
    }

    public class BsaHealthRuleSet
    {
        public int SchemaVersion { get; set; } = 1;
        public double EvaluationHz { get; set; }
        public List<BsaHealthRule> Rules { get; set; } = new List<BsaHealthRule>();
    }

    public class BsaHealthRule
    {
        public string RuleId { get; set; }
        public string Kind { get; set; }
        public string OutputFieldId { get; set; }
        public double FreshnessSeconds { get; set; }
        public double ArmedGraceSeconds { get; set; }
        public List<string> InputFieldIds { get; set; } = new List<string>();
    }

    public class BsaPluginDescriptor
    {
        public int SchemaVersion { get; set; } = 1;
        public string PluginId { get; set; }
        public string PublisherKeyId { get; set; }
        public string Version { get; set; }
        public string EntryType { get; set; }
        public PackageCompatibility Compatibility { get; set; }
        public string PayloadPath { get; set; }
        public string PayloadSha256 { get; set; }
        public List<string> Capabilities { get; set; } = new List<string>();
        public List<string> ProducedFieldIds { get; set; } = new List<string>();
        public List<string> Dependencies { get; set; } = new List<string>();
        public bool RestartRequired { get; set; }
        public List<string> ReplacesPluginIds { get; set; } = new List<string>();
    }

    public class BsaBundleProfile
    {
        public BsaQuickViewProfile QuickView { get; set; }
        public BsaTelemetryBindings TelemetryBindings { get; set; }
        public BsaWarningProfile Warnings { get; set; }
        public BsaHealthRuleSet HealthRules { get; set; }
    }
}
