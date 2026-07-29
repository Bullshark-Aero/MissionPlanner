using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.BSA.Checks;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Tests
{
    [TestClass]
    public class AutoCheckEvaluatorTests
    {
        class FakeProvider : IValueProvider
        {
            readonly Dictionary<string, object> _values;
            public FakeProvider(Dictionary<string, object> values) { _values = values; }
            public bool TryGetValue(string field, out object value) => _values.TryGetValue(field, out value);
        }

        static PreflightCheckDefinition FieldCheck(CheckSource source, string field, CheckCondition condition, object value) =>
            new PreflightCheckDefinition
            {
                Id = "c1",
                Title = "test",
                Type = CheckType.Auto,
                Severity = CheckSeverity.Warning,
                Source = source,
                Field = field,
                Condition = condition,
                Value = value
            };

        static AutoCheckEvaluator NewEvaluator(Dictionary<string, object> telemetryValues)
        {
            var providers = new Dictionary<CheckSource, IValueProvider>
            {
                [CheckSource.Telemetry] = new FakeProvider(telemetryValues)
            };
            return new AutoCheckEvaluator(providers);
        }

        [TestMethod]
        public void Pass_IncludesFieldValueAndThreshold_InDetail()
        {
            var evaluator = NewEvaluator(new Dictionary<string, object> { ["linkqualitygcs"] = 100.0 });
            var check = FieldCheck(CheckSource.Telemetry, "linkqualitygcs", CheckCondition.GTEQ, 80.0);

            var (outcome, detail) = evaluator.Evaluate(check);

            Assert.AreEqual(CheckOutcome.Pass, outcome);
            StringAssert.Contains(detail, "linkqualitygcs");
            StringAssert.Contains(detail, "100");
            StringAssert.Contains(detail, ">=");
            StringAssert.Contains(detail, "80");
        }

        [TestMethod]
        public void Fail_StillProducesDetail()
        {
            var evaluator = NewEvaluator(new Dictionary<string, object> { ["linkqualitygcs"] = 10.0 });
            var check = FieldCheck(CheckSource.Telemetry, "linkqualitygcs", CheckCondition.GTEQ, 80.0);

            var (outcome, detail) = evaluator.Evaluate(check);

            Assert.AreEqual(CheckOutcome.Fail, outcome);
            StringAssert.Contains(detail, "10");
        }

        [TestMethod]
        public void UnavailableField_IsUnknown_WithExplanatoryDetail()
        {
            var evaluator = NewEvaluator(new Dictionary<string, object>());
            var check = FieldCheck(CheckSource.Telemetry, "missingField", CheckCondition.GTEQ, 80.0);

            var (outcome, detail) = evaluator.Evaluate(check);

            Assert.AreEqual(CheckOutcome.Unknown, outcome);
            StringAssert.Contains(detail, "missingField");
        }

        [TestMethod]
        public void UnknownSource_IsUnknown_NeverThrows()
        {
            var evaluator = NewEvaluator(new Dictionary<string, object> { ["x"] = 1.0 });
            var check = FieldCheck(CheckSource.Param, "x", CheckCondition.EQ, 1.0); // Param has no registered provider here

            var (outcome, _) = evaluator.Evaluate(check);

            Assert.AreEqual(CheckOutcome.Unknown, outcome);
        }

        [TestMethod]
        public void MissingConditionOrValue_IsUnknown()
        {
            var evaluator = NewEvaluator(new Dictionary<string, object> { ["x"] = 1.0 });
            var check = new PreflightCheckDefinition
            {
                Id = "c1", Title = "t", Type = CheckType.Auto, Severity = CheckSeverity.Warning,
                Source = CheckSource.Telemetry, Field = "x"
                // Condition/Value deliberately left null
            };

            var (outcome, _) = evaluator.Evaluate(check);

            Assert.AreEqual(CheckOutcome.Unknown, outcome);
        }
    }
}
