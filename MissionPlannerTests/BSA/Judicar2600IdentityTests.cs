using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Telemetry;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class Judicar2600IdentityTests
    {
        readonly object _link = new object();

        [TestMethod]
        public void IsJudicarField_RecognisesEveryJudicarFieldWithOrWithoutThePrefix()
        {
            foreach (var field in Judicar2600BundleProfile.NamedFields)
            {
                Assert.IsTrue(Judicar2600Identity.IsJudicarField(field), field);
                Assert.IsTrue(Judicar2600Identity.IsJudicarField(field.Substring(4)), field.Substring(4));
            }
        }

        [TestMethod]
        public void IsJudicarField_RejectsOtherNames()
        {
            foreach (var name in new[] { null, "", "  ", "RPM", "MAV_RPM", "ESC_HOT2", "esc_hot" })
                Assert.IsFalse(Judicar2600Identity.IsJudicarField(name), name ?? "(null)");
        }

        [TestMethod]
        public void StartsUnidentified()
        {
            Assert.IsFalse(new Judicar2600Identity().IsIdentified(_link, 1, true));
        }

        [TestMethod]
        public void JudicarTelemetry_IdentifiesThatLinkAndVehicleOnly()
        {
            var identity = new Judicar2600Identity();
            identity.Record(_link, 1, "ESC_HOT");

            Assert.IsTrue(identity.IsIdentified(_link, 1, true));
            Assert.IsFalse(identity.IsIdentified(_link, 2, true), "another vehicle on the same link");
            Assert.IsFalse(identity.IsIdentified(new object(), 1, true), "another link");
        }

        [TestMethod]
        public void OtherTelemetry_DoesNotIdentify()
        {
            var identity = new Judicar2600Identity();
            identity.Record(_link, 1, "RPM");
            identity.Record(_link, 1, "BATT_TEMP");

            Assert.IsFalse(identity.IsIdentified(_link, 1, true));
        }

        [TestMethod]
        public void InvalidSourceIsIgnored()
        {
            var identity = new Judicar2600Identity();
            identity.Record(null, 1, "ESC_HOT");
            identity.Record(_link, 0, "ESC_HOT");

            Assert.IsFalse(identity.IsIdentified(_link, 1, true));
            Assert.IsFalse(identity.IsIdentified(_link, 0, true));
        }

        [TestMethod]
        public void ClosingTheLink_ForgetsTheAircraft_UntilItIdentifiesAgain()
        {
            var identity = new Judicar2600Identity();
            identity.Record(_link, 1, "LIFT_HDR");

            Assert.IsFalse(identity.IsIdentified(_link, 1, false));
            Assert.IsFalse(identity.IsIdentified(_link, 1, true), "a reconnect must not inherit the old identification");

            identity.Record(_link, 1, "LIFT_HDR");
            Assert.IsTrue(identity.IsIdentified(_link, 1, true));
        }
    }
}
