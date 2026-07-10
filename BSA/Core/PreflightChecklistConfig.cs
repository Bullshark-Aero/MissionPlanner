using System.Collections.Generic;

namespace MissionPlanner.BSA.Core
{
    public class PreflightChecklistMetadata
    {
        public string Name { get; set; }
        public string ConfigVersion { get; set; }
    }

    public class PreflightChecklistConfig
    {
        public int SchemaVersion { get; set; }
        public PreflightChecklistMetadata Metadata { get; set; }
        public List<PreflightCheckDefinition> Checks { get; set; } = new List<PreflightCheckDefinition>();
    }
}
