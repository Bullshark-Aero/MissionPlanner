using System;
using System.Globalization;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Reproduces Controls.PreFlight.CheckListItem's comparison semantics (CheckValue()) as a pure
    /// function over already-resolved values, decoupled from any particular value source. Numeric
    /// comparison is tried first (covers telemetry/param values and numeric JSON values); if either side
    /// can't be read as a number, falls back to case-insensitive string equality, which is what
    /// EQ/NEQ need for string mpconfig values like speechenable = "True". LT/LTEQ/GT/GTEQ are not
    /// meaningful for non-numeric values and evaluate to false rather than throwing.
    /// </summary>
    public static class ConditionEvaluator
    {
        public static bool Evaluate(object actual, CheckCondition condition, object expected)
        {
            if (TryCompareNumeric(actual, expected, out var comparison))
                return EvaluateComparison(comparison, condition);

            var actualText = Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty;
            var expectedText = Convert.ToString(expected, CultureInfo.InvariantCulture) ?? string.Empty;
            var equal = string.Equals(actualText, expectedText, StringComparison.OrdinalIgnoreCase);

            switch (condition)
            {
                case CheckCondition.EQ: return equal;
                case CheckCondition.NEQ: return !equal;
                default: return false;
            }
        }

        static bool TryCompareNumeric(object actual, object expected, out int comparison)
        {
            comparison = 0;
            if (!TryToDouble(actual, out var a) || !TryToDouble(expected, out var e))
                return false;
            comparison = a.CompareTo(e);
            return true;
        }

        static bool TryToDouble(object value, out double result)
        {
            result = 0;
            if (value == null)
                return false;

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                return false;
            }
        }

        static bool EvaluateComparison(int comparison, CheckCondition condition)
        {
            switch (condition)
            {
                case CheckCondition.LT: return comparison < 0;
                case CheckCondition.LTEQ: return comparison <= 0;
                case CheckCondition.EQ: return comparison == 0;
                case CheckCondition.GT: return comparison > 0;
                case CheckCondition.GTEQ: return comparison >= 0;
                case CheckCondition.NEQ: return comparison != 0;
                default: return false;
            }
        }
    }
}
