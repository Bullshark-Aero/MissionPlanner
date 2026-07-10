using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class ConditionEvaluatorTests
    {
        [TestMethod]
        public void LT_True() => Assert.IsTrue(ConditionEvaluator.Evaluate(5.0, CheckCondition.LT, 10.0));

        [TestMethod]
        public void LT_FalseAtEquality() => Assert.IsFalse(ConditionEvaluator.Evaluate(10.0, CheckCondition.LT, 10.0));

        [TestMethod]
        public void LTEQ_TrueAtEquality() => Assert.IsTrue(ConditionEvaluator.Evaluate(10.0, CheckCondition.LTEQ, 10.0));

        [TestMethod]
        public void LTEQ_FalseAboveThreshold() => Assert.IsFalse(ConditionEvaluator.Evaluate(10.1, CheckCondition.LTEQ, 10.0));

        [TestMethod]
        public void EQ_True() => Assert.IsTrue(ConditionEvaluator.Evaluate(10.0, CheckCondition.EQ, 10.0));

        [TestMethod]
        public void EQ_False() => Assert.IsFalse(ConditionEvaluator.Evaluate(10.0, CheckCondition.EQ, 10.1));

        [TestMethod]
        public void GT_True() => Assert.IsTrue(ConditionEvaluator.Evaluate(15.0, CheckCondition.GT, 10.0));

        [TestMethod]
        public void GT_FalseAtEquality() => Assert.IsFalse(ConditionEvaluator.Evaluate(10.0, CheckCondition.GT, 10.0));

        [TestMethod]
        public void GTEQ_TrueAtEquality() => Assert.IsTrue(ConditionEvaluator.Evaluate(10.0, CheckCondition.GTEQ, 10.0));

        [TestMethod]
        public void GTEQ_FalseBelowThreshold() => Assert.IsFalse(ConditionEvaluator.Evaluate(9.9, CheckCondition.GTEQ, 10.0));

        [TestMethod]
        public void NEQ_True() => Assert.IsTrue(ConditionEvaluator.Evaluate(10.0, CheckCondition.NEQ, 10.1));

        [TestMethod]
        public void NEQ_False() => Assert.IsFalse(ConditionEvaluator.Evaluate(10.0, CheckCondition.NEQ, 10.0));

        [TestMethod]
        public void StringEquality_CaseInsensitive()
        {
            Assert.IsTrue(ConditionEvaluator.Evaluate("True", CheckCondition.EQ, "true"));
            Assert.IsFalse(ConditionEvaluator.Evaluate("True", CheckCondition.NEQ, "true"));
        }

        [TestMethod]
        public void StringInequality()
        {
            Assert.IsTrue(ConditionEvaluator.Evaluate("False", CheckCondition.NEQ, "True"));
            Assert.IsFalse(ConditionEvaluator.Evaluate("False", CheckCondition.EQ, "True"));
        }

        [TestMethod]
        public void NonNumericString_OrderingComparison_IsFalse_NotThrown()
        {
            Assert.IsFalse(ConditionEvaluator.Evaluate("True", CheckCondition.LT, "False"));
        }

        [TestMethod]
        public void BoolActual_VsNumericStringExpected_ComparesNumerically()
        {
            Assert.IsTrue(ConditionEvaluator.Evaluate(true, CheckCondition.EQ, "1"));
        }

        [TestMethod]
        public void BoolActual_VsWordExpected_FallsBackToStringComparison()
        {
            // bool converts to a number (1/0) but "true" does not parse as one, so numeric comparison
            // must not apply here - falls back to case-insensitive string comparison instead.
            Assert.IsTrue(ConditionEvaluator.Evaluate(true, CheckCondition.EQ, "true"));
        }

        [TestMethod]
        public void NullActual_NeverThrows_EvaluatesFalse()
        {
            Assert.IsFalse(ConditionEvaluator.Evaluate(null, CheckCondition.EQ, 1.0));
        }

        [TestMethod]
        public void NumericStringVsNumber_ComparesNumerically()
        {
            Assert.IsTrue(ConditionEvaluator.Evaluate("80", CheckCondition.GTEQ, 80.0));
        }
    }
}
