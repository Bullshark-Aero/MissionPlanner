using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class BsaPluginTrustValidatorTests
    {
        [TestMethod]
        public void ExecutableBundleWithoutTrustStore_FailsClosed()
        {
            var package = new ConfigPackageContents
            {
                Manifest = new PackageManifest
                {
                    Components = new List<PackageComponent>
                    {
                        new PackageComponent { Type = "plugin-payload", Path = "plugins/test.dll" }
                    }
                }
            };

            var ex = Assert.ThrowsException<InvalidDataException>(() =>
                BsaPluginTrustValidator.Validate("not-opened.zip", package, "missing-trust-store.json"));
            StringAssert.Contains(ex.Message, "no BSA plugin trust store");
        }
    }
}
