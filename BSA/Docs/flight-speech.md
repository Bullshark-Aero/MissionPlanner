# Flight speech

Two portable MP settings opt into these announcements. Both default to false.
The global speech switch must also be enabled. Restart after importing settings.

| Setting | Behaviour |
| --- | --- |
| `speechflightairspeedenabled` | Rounded airspeed in m/s, number only, every 3 seconds while airborne in forward flight or either VTOL transition. No safe-band suppression. |
| `speechenginefailureenabled` | "Engine failure" after RPM1 remains below 1000 rpm for more than 1 second while airborne, including hover. Repeat every 10 seconds until recovery. |

The engine warning takes priority within this service. It waits for the speech
engine to become ready and does not interrupt existing announcements. Numeric
callouts use the latest value, never a backlog. Disable the older low-speed speech
option when using these callouts to avoid duplicate announcements.

Airborne means armed and EXTENDED_SYS_STATE IN_AIR, TAKEOFF or LANDING. Forward
flight means VTOL state FW, TRANSITION_TO_FW or TRANSITION_TO_MC, including these
phases inside AUTO and RTL. Ground, unknown state, disconnection and log replay
are silent. This implementation targets QuadPlane, not conventional fixed wing
autopilots that do not publish a VTOL state.

Existing packet-cache data is used without requesting messages or changing stream
rates. Heartbeat and extended state expire after 3 seconds, airspeed and RPM after
2 seconds. At least two distinct low RPM samples are needed; one isolated sample
cannot age into an alert. Missing/invalid RPM resets the failure timer; it is not an engine
failure detector when RPM telemetry is absent. Check telemetry-loss warnings
separately. Evaluate at 10 Hz; the hold is therefore checked within approximately
100 ms of the threshold, subject to scheduler and speech availability.

Before flight, verify audio output on the actual GCS and confirm its received
EXTENDED_SYS_STATE and RPM cadence. Use simulated telemetry for engine-warning
acceptance, never stop an airborne engine to test it. These settings do not
command the aircraft or change any failsafe.
