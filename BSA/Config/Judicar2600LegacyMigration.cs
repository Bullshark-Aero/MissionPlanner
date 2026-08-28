using System;
using System.Collections.Generic;
using System.IO;

namespace MissionPlanner.BSA.Config
{
    /// <summary>Explicit one-time adapter for the reviewed v0.2.4 Judicar operator export.</summary>
    public static class Judicar2600LegacyMigration
    {
        public static readonly IReadOnlyDictionary<string, string> ApprovedBindingMap =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customfield0"] = "MAV_VTOL_RES", ["customfield1"] = "MAV_VTOL_MAR",
                ["customfield2"] = "MAV_AV_RES", ["customfield3"] = "MAV_AV_MAR",
                ["customfield4"] = "MAV_ESC_HOT", ["customfield5"] = "MAV_CHT_HOT",
                ["customfield6"] = "MAV_FR_MOT_T", ["customfield7"] = "MAV_LIFT_HDR",
                ["customfield8"] = "MAV_SURF_HDR", ["customfield9"] = "MAV_ATT_ERR5",
                ["customfield10"] = "MAV_ALT_ERR5", ["customfield11"] = "MAV_LIDAR_M",
                ["customfield12"] = "MAV_AS_DIF5"
            };

        public static BsaBundleProfile CreateProfile(ConfigPackageContents legacyPackage)
        {
            if (legacyPackage == null || !legacyPackage.IsLegacy)
                throw new InvalidDataException("Only a validated legacy package can use the Judicar migration adapter.");
            var settings = new Dictionary<string, string>(legacyPackage.ConfigSubset, StringComparer.Ordinal);
            if (!settings.ContainsKey("quickViewRows")) settings["quickViewRows"] = "6";
            if (!settings.ContainsKey("quickViewCols")) settings["quickViewCols"] = "5";
            var quickView = BsaQuickViewCodec.Export(settings, ApprovedBindingMap);
            return Judicar2600BundleProfile.Create(quickView);
        }
    }
}
