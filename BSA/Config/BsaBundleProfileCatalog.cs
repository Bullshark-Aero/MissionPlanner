using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.BSA.Config
{
    public sealed class BsaBundleProfileOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string PackageId { get; set; }
        public Func<BsaQuickViewProfile, BsaBundleProfile> CreateProfile { get; set; }
        public bool CarriesProfile => CreateProfile != null;
    }

    public static class BsaBundleProfileCatalog
    {
        public const string NoProfileId = "none";

        public static IReadOnlyList<BsaBundleProfileOption> Options { get; } = new List<BsaBundleProfileOption>
        {
            new BsaBundleProfileOption
            {
                Id = Judicar2600BundleProfile.PackageId,
                DisplayName = "Judicar 2600 - first hover",
                Description = "Settings, the quick panel, the 13 Judicar telemetry bindings and the 3 health rules. " +
                              "Import it only on a ground station that flies a Judicar 2600.",
                PackageId = Judicar2600BundleProfile.PackageId,
                CreateProfile = Judicar2600BundleProfile.Create
            },
            new BsaBundleProfileOption
            {
                Id = NoProfileId,
                DisplayName = "No aircraft profile",
                Description = "Settings, the quick panel and the BSA files only - no aircraft-specific telemetry " +
                              "bindings or health rules. Use this for any other aircraft."
            }
        };

        public static BsaBundleProfileOption Find(string id) =>
            Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("Unknown bundle profile '" + id + "'.", nameof(id));

        public static BsaBundleProfile CreateFor(BsaBundleProfileOption option, Func<BsaQuickViewProfile> exportQuickView)
        {
            if (option == null) throw new ArgumentNullException(nameof(option));
            return option.CarriesProfile ? option.CreateProfile(exportQuickView()) : null;
        }
    }
}
