using System;
using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// Adapts a plain evaluation function to IRegisteredCheck, avoiding a class-per-check for simple
    /// registered checks (mission sanity, mpconfig package-match). Centralizes the "never throws"
    /// contract in one place: any exception from the delegate is caught here and reported as Unknown
    /// with an explanatory detail, so individual check implementations don't each need their own
    /// defensive try/catch.
    /// </summary>
    public class DelegateRegisteredCheck : IRegisteredCheck
    {
        readonly Func<PreflightCheckDefinition, (CheckOutcome outcome, string detail)> _evaluate;

        public string Key { get; }

        public DelegateRegisteredCheck(string key, Func<PreflightCheckDefinition, (CheckOutcome outcome, string detail)> evaluate)
        {
            Key = key;
            _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        }

        public CheckOutcome Evaluate(PreflightCheckDefinition check, out string detail)
        {
            try
            {
                var result = _evaluate(check);
                detail = result.detail;
                return result.outcome;
            }
            catch (Exception ex)
            {
                detail = $"Check '{Key}' failed to evaluate: {ex.Message}";
                return CheckOutcome.Unknown;
            }
        }
    }
}
