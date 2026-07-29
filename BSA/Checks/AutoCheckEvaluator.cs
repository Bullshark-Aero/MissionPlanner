using System.Collections.Generic;
using System.Globalization;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Evaluates the generic field-comparator shape (Field+Condition+Value) for Auto/Semi checks whose
    /// Source is Telemetry/Param/MpConfig. Checks using the named-registered-check shape (Check key) go
    /// through RegisteredCheckRegistry instead - PreflightRunEngine picks whichever applies per check.
    /// </summary>
    public class AutoCheckEvaluator
    {
        readonly IReadOnlyDictionary<CheckSource, IValueProvider> _providers;

        public AutoCheckEvaluator(IReadOnlyDictionary<CheckSource, IValueProvider> providers)
        {
            _providers = providers;
        }

        /// <summary>
        /// Never throws - any failure to resolve source/field/condition/value maps to Unknown, with a
        /// detail string explaining why. On a real comparison, detail shows the actual value read and
        /// the threshold it was compared against (e.g. "linkqualitygcs = 100 (need &gt;= 80)"), so the
        /// wizard's evidence display has something to show the operator beyond bare pass/fail.
        /// </summary>
        public (CheckOutcome outcome, string detail) Evaluate(PreflightCheckDefinition check)
        {
            if (check?.Source == null || _providers == null || !_providers.TryGetValue(check.Source.Value, out var provider))
                return (CheckOutcome.Unknown, $"No value source configured for source '{check?.Source}'.");

            if (string.IsNullOrWhiteSpace(check.Field) || !provider.TryGetValue(check.Field, out var actual))
                return (CheckOutcome.Unknown, $"'{check.Field}' is not currently available.");

            if (check.Condition == null || check.Value == null)
                return (CheckOutcome.Unknown, "Check definition is missing Condition/Value.");

            try
            {
                var pass = ConditionEvaluator.Evaluate(actual, check.Condition.Value, check.Value);
                var detail = $"{check.Field} = {FormatValue(actual)} (need {Symbol(check.Condition.Value)} {FormatValue(check.Value)})";
                return (pass ? CheckOutcome.Pass : CheckOutcome.Fail, detail);
            }
            catch
            {
                return (CheckOutcome.Unknown, $"Could not evaluate '{check.Field}'.");
            }
        }

        static string FormatValue(object value)
        {
            if (value == null) return "null";
            if (value is double d) return d.ToString("0.##", CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString("0.##", CultureInfo.InvariantCulture);
            return value.ToString();
        }

        static string Symbol(CheckCondition condition)
        {
            switch (condition)
            {
                case CheckCondition.LT: return "<";
                case CheckCondition.LTEQ: return "<=";
                case CheckCondition.EQ: return "=";
                case CheckCondition.GT: return ">";
                case CheckCondition.GTEQ: return ">=";
                case CheckCondition.NEQ: return "!=";
                default: return "?";
            }
        }
    }
}
