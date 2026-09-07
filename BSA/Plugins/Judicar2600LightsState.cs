namespace BSA.Judicar2600.MissionPlannerPlugins
{
    internal enum LightsCommandState
    {
        Unknown,
        CommandedOn,
        CommandedOff
    }

    internal static class Judicar2600LightsState
    {
        internal static int NextTargetPwm(
            LightsCommandState state,
            int onPwm,
            int offPwm)
        {
            return state == LightsCommandState.CommandedOn ? offPwm : onPwm;
        }

        internal static LightsCommandState ResolveAfterAttempt(
            int targetPwm,
            int onPwm,
            bool firstAccepted,
            bool secondAccepted,
            bool recoveryFirstAccepted,
            bool recoverySecondAccepted)
        {
            if (firstAccepted && secondAccepted)
            {
                return targetPwm == onPwm
                    ? LightsCommandState.CommandedOn
                    : LightsCommandState.CommandedOff;
            }

            return recoveryFirstAccepted && recoverySecondAccepted
                ? LightsCommandState.CommandedOn
                : LightsCommandState.Unknown;
        }

        internal static bool ConnectionInvalidatesState(
            bool previouslyConnected,
            byte previousSystemId,
            bool connected,
            byte systemId)
        {
            return !connected || !previouslyConnected || previousSystemId != systemId;
        }
    }
}
