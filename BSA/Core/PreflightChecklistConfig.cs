using System.Collections.Generic;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// Grouping/pagination fields (Groups, PageSize, AutoPageSize, AutoChecksFirst, AutoGroupTitle)
    /// are all additive and optional - SchemaVersion stays 1, and a checklist with none of them set
    /// loads exactly as it did before this feature existed (PreflightPagePlan treats an ungrouped
    /// checklist as one implicit group). See PreflightPagePlan and PreflightChecklistLoader's
    /// grouping validation rules.
    /// </summary>
    public class PreflightChecklistMetadata
    {
        public string Name { get; set; }
        public string ConfigVersion { get; set; }

        /// <summary>Declared group names, in display order. Null/empty means "ungrouped" - every
        /// check must then leave Group unset too (loader-enforced).</summary>
        public List<string> Groups { get; set; }

        /// <summary>Operator (Manual/Semi) checks per wizard page.</summary>
        public int PageSize { get; set; } = 5;

        /// <summary>Auto checks per page, on the synthetic leading auto-checks page.</summary>
        public int AutoPageSize { get; set; } = 12;

        /// <summary>When true (default), every Type=Auto check is hoisted out of its authored Group
        /// onto one synthetic leading page. Semi checks are never hoisted.</summary>
        public bool AutoChecksFirst { get; set; } = true;

        /// <summary>Display name for the synthetic auto-checks page. Must not collide with a
        /// declared Group name (loader-enforced).</summary>
        public string AutoGroupTitle { get; set; } = "System checks";
    }

    public class PreflightChecklistConfig
    {
        public int SchemaVersion { get; set; }
        public PreflightChecklistMetadata Metadata { get; set; }
        public List<PreflightCheckDefinition> Checks { get; set; } = new List<PreflightCheckDefinition>();
    }
}
