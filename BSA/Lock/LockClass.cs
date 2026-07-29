using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MissionPlanner.BSA.Lock
{
    /// <summary>
    /// Per-action policy classification. Allow proceeds normally; Warn proceeds but is logged and can
    /// invalidate the preflight; Block refuses the action outright; Authorise requires an inline
    /// Engineering-Mode passphrase to proceed as Allow. All four are audit-logged while the lock is On -
    /// nothing here is evaluated while the lock is Off (see BsaLockService's fail-open invariant).
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LockClass
    {
        Allow,
        Warn,
        Block,
        Authorise
    }
}
