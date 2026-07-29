using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Classification of a Settings.config key for export purposes. Only Portable keys ever leave
    /// this machine in a package. MachineSpecific and Volatile keys are excluded from packages and
    /// from compare hashes (a Volatile-key change, e.g. window position, must never register as a
    /// config mismatch). Secret keys are never exported and their presence in an export request is
    /// treated as a defensive-abort condition, not silently skipped.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum KeyClass
    {
        Portable,
        MachineSpecific,
        Secret,
        Volatile
    }
}
