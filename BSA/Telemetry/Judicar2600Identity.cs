using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Telemetry
{
    public sealed class Judicar2600Identity
    {
        static readonly HashSet<string> JudicarFields = new HashSet<string>(
            Judicar2600BundleProfile.NamedFields.Select(StripPrefix), StringComparer.Ordinal);

        readonly object _sync = new object();
        object _link;
        int _sysId = -1;

        public static bool IsJudicarField(string namedValueName) =>
            !string.IsNullOrWhiteSpace(namedValueName) && JudicarFields.Contains(StripPrefix(namedValueName.Trim()));

        public void Record(object link, int sysId, string namedValueName)
        {
            if (link == null || sysId <= 0 || !IsJudicarField(namedValueName)) return;
            lock (_sync)
            {
                _link = link;
                _sysId = sysId;
            }
        }

        public bool IsIdentified(object link, int sysId, bool linkOpen)
        {
            lock (_sync)
            {
                if (!linkOpen)
                {
                    _link = null;
                    _sysId = -1;
                    return false;
                }
                return _link != null && ReferenceEquals(_link, link) && _sysId == sysId;
            }
        }

        static string StripPrefix(string name) =>
            name.StartsWith("MAV_", StringComparison.OrdinalIgnoreCase) ? name.Substring(4) : name;
    }
}
