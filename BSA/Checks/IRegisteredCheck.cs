using MissionPlanner.BSA.Core;

namespace MissionPlanner.BSA.Checks
{
    /// <summary>
    /// A named check that can't be expressed as a single field+comparator+value test - mission sanity
    /// (empty/non-empty, takeoff+landing present, home plausibility, ...) and the mpconfig
    /// approved-package match are inherently multi-field/imperative. Referenced from JSON via
    /// PreflightCheckDefinition.Check and resolved through RegisteredCheckRegistry.
    /// </summary>
    public interface IRegisteredCheck
    {
        /// <summary>Stable key referenced from JSON, e.g. "mission.nonempty". Namespaced by convention:
        /// "mission.*", "mpconfig.*".</summary>
        string Key { get; }

        /// <summary>Never throws - any internal failure should be caught and reported as Unknown with
        /// an explanatory detail.</summary>
        CheckOutcome Evaluate(PreflightCheckDefinition check, out string detail);
    }
}
