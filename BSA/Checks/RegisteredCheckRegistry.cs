using System;
using System.Collections.Generic;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// String-keyed lookup from PreflightCheckDefinition.Check to an IRegisteredCheck implementation.
    /// PreflightChecklistLoader validates JSON "Check" values against Keys at load time, so an unknown
    /// key is a load-time error, never a runtime surprise mid-wizard. An instance (not static/global) so
    /// tests can register fakes without touching the real mission/mpconfig checks.
    /// </summary>
    public class RegisteredCheckRegistry
    {
        readonly Dictionary<string, IRegisteredCheck> _checks =
            new Dictionary<string, IRegisteredCheck>(StringComparer.OrdinalIgnoreCase);

        public void Register(IRegisteredCheck check)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            if (string.IsNullOrWhiteSpace(check.Key)) throw new ArgumentException("IRegisteredCheck.Key must be non-empty.");
            _checks[check.Key] = check;
        }

        public IEnumerable<string> Keys => _checks.Keys;

        public bool TryGet(string key, out IRegisteredCheck check)
        {
            if (key == null)
            {
                check = null;
                return false;
            }
            return _checks.TryGetValue(key, out check);
        }
    }
}
