using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Config;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class KeyClassifierTests
    {
        static KeyPolicyConfig Policy(params (string match, KeyClass @class)[] rules) => new KeyPolicyConfig
        {
            SchemaVersion = 1,
            Rules = new List<KeyPolicyRule>(System.Array.ConvertAll(rules, r => new KeyPolicyRule { Match = r.match, Class = r.@class })),
            Default = KeyClass.MachineSpecific
        };

        [TestMethod]
        public void StarGlob_MatchesPrefix()
        {
            var policy = Policy(("speech*", KeyClass.Portable));
            Assert.AreEqual(KeyClass.Portable, KeyClassifier.Classify("speechenable", policy));
        }

        [TestMethod]
        public void StarGlob_AnchoredNotSubstring()
        {
            // "speech*" must not match a key that merely contains "speech" mid-string.
            var policy = Policy(("speech*", KeyClass.Portable));
            Assert.AreEqual(KeyClass.MachineSpecific, KeyClassifier.Classify("myspeechenable", policy));
        }

        [TestMethod]
        public void PipeSeparated_MatchesAnyAlternative()
        {
            var policy = Policy(("distunits|speedunits|altunits", KeyClass.Portable));
            Assert.AreEqual(KeyClass.Portable, KeyClassifier.Classify("speedunits", policy));
        }

        [TestMethod]
        public void CaseInsensitive()
        {
            var policy = Policy(("SPEECH*", KeyClass.Portable));
            Assert.AreEqual(KeyClass.Portable, KeyClassifier.Classify("speechenable", policy));
        }

        [TestMethod]
        public void NoRuleMatches_FallsToDefault()
        {
            var policy = Policy(("speech*", KeyClass.Portable));
            Assert.AreEqual(KeyClass.MachineSpecific, KeyClassifier.Classify("comport", policy));
        }

        [TestMethod]
        public void FirstMatchingRule_Wins()
        {
            var policy = Policy(("password_protect", KeyClass.Secret), ("*password*", KeyClass.Secret));
            Assert.AreEqual(KeyClass.Secret, KeyClassifier.Classify("password_protect", policy));
        }

        [TestMethod]
        public void RealSecretKeys_ClassifyAsSecret()
        {
            var policy = Policy(("*apikey*|*api_key*|*password*|*psk*|*token*|*signing*|*secret*|*credential*", KeyClass.Secret));
            foreach (var key in new[]
                     {
                         "AirMarket_password", "Dowding_password", "Dowding_token", "Dowding_onvifpassword",
                         "DigitalSky_Password", "ex_api_psk", "GoogleApiKey", "password", "password_protect"
                     })
            {
                Assert.AreEqual(KeyClass.Secret, KeyClassifier.Classify(key, policy), $"'{key}' should classify as Secret");
            }
        }

        [TestMethod]
        public void ExApiPsk_NearMiss_FallsToDefault_WhenSecretGlobNarrower()
        {
            // A deliberately narrower glob (missing "*psk*") should NOT catch ex_api_psk - proves the
            // classifier does exact glob matching, not a fuzzy "looks secret-ish" heuristic.
            var policy = Policy(("*apikey*|*password*|*signing*", KeyClass.Secret));
            Assert.AreEqual(KeyClass.MachineSpecific, KeyClassifier.Classify("ex_api_psk", policy));
        }

        [TestMethod]
        public void QuestionMark_MatchesSingleChar()
        {
            var policy = Policy(("com?ort", KeyClass.MachineSpecific));
            Assert.AreEqual(KeyClass.MachineSpecific, KeyClassifier.Classify("comport", policy));
        }

        [TestMethod]
        public void EmptyRules_AlwaysFallsToDefault()
        {
            var policy = new KeyPolicyConfig { SchemaVersion = 1, Rules = new List<KeyPolicyRule>(), Default = KeyClass.Volatile };
            Assert.AreEqual(KeyClass.Volatile, KeyClassifier.Classify("anything", policy));
        }

        [TestMethod]
        public void FindMatchingRule_ReturnsTheMatchedRule()
        {
            var policy = Policy(("guided_alt*", KeyClass.Portable));
            var rule = KeyClassifier.FindMatchingRule("guided_alt_frame", policy);
            Assert.IsNotNull(rule);
            Assert.AreEqual("guided_alt*", rule.Match);
        }

        [TestMethod]
        public void FindMatchingRule_NoMatch_ReturnsNull()
        {
            var policy = Policy(("speech*", KeyClass.Portable));
            Assert.IsNull(KeyClassifier.FindMatchingRule("comport", policy));
        }

        [TestMethod]
        public void RuleWithNullClass_FallsToPolicyDefault()
        {
            var policy = new KeyPolicyConfig
            {
                SchemaVersion = 1,
                Rules = new List<KeyPolicyRule> { new KeyPolicyRule { Match = "foo*", Class = null } },
                Default = KeyClass.Secret
            };
            Assert.AreEqual(KeyClass.Secret, KeyClassifier.Classify("foobar", policy));
        }
    }
}
