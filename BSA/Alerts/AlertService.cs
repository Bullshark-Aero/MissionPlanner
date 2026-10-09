using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public sealed class LiveAlertTelemetry : IAlertTelemetry
    {
        static readonly ConcurrentDictionary<string, PropertyInfo> Properties =
            new ConcurrentDictionary<string, PropertyInfo>(StringComparer.Ordinal);

        readonly MAVLinkInterface _port;
        readonly NamedValueTracker _tracker;

        public LiveAlertTelemetry(MAVLinkInterface port, NamedValueTracker tracker)
        {
            _port = port;
            _tracker = tracker;
        }

        public int SysId => _port.sysidcurrent;

        public bool TryReadField(string property, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(property)) return false;
            var info = Properties.GetOrAdd(property, name => typeof(CurrentState).GetProperty(name));
            if (info == null) return false;
            try
            {
                var raw = info.GetValue(_port.MAV.cs, null);
                if (raw == null || raw is string) return false;
                value = Convert.ToDouble(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public DateTime? PacketUtc(uint messageId)
        {
            var packet = _port.MAV.getPacketLast(messageId);
            if (packet == null || packet.rxtime == DateTime.MinValue) return null;
            return packet.rxtime;
        }

        public NamedValueSample Named(string name) => _tracker.Get(SysId, name);

        public string NamedValueSlot(string name)
        {
            var wanted = "MAV_" + AlertSource.NormalizeName(name);
            try
            {
                var names = CurrentState.custom_field_names;
                if (names == null) return null;
                return names.ToList().FirstOrDefault(entry => entry.Value == wanted).Key;
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class AlertService : IDisposable
    {
        const int PeriodMilliseconds = 100;
        const double ReadoutSpeakingSeconds = 6;

        readonly NamedValueTracker _tracker = new NamedValueTracker();
        readonly AlertEvaluator _evaluator = new AlertEvaluator();
        readonly FlightPhaseRequester _requester;
        readonly Timer _timer;
        readonly object _subscriptionSync = new object();
        MAVLinkInterface _subscribed;
        object _vehicle;
        int _busy;
        bool _lastSpokeReadout;
        DateTime _lastSpokenUtc = DateTime.MinValue;
        bool _disposed;

        public AlertService()
        {
            _requester = new FlightPhaseRequester(sysId =>
                MainV2.comPort.sendPacket(FlightPhaseRequester.Command(sysId), sysId,
                    (int)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1));
            _timer = new Timer(Tick, null, PeriodMilliseconds, PeriodMilliseconds);
        }

        void Subscribe(MAVLinkInterface port)
        {
            lock (_subscriptionSync)
            {
                if (_disposed || ReferenceEquals(port, _subscribed)) return;
                if (_subscribed != null) _subscribed.OnPacketReceived -= OnPacketReceived;
                _subscribed = port;
                if (_subscribed != null) _subscribed.OnPacketReceived += OnPacketReceived;
            }
        }

        void OnPacketReceived(object sender, MAVLink.MAVLinkMessage message)
        {
            try
            {
                if (message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.NAMED_VALUE_FLOAT) return;
                var selected = MainV2.comPort.sysidcurrent;
                if (selected <= 0 || message.sysid != selected) return;
                var packet = message.ToStructure<MAVLink.mavlink_named_value_float_t>();
                var name = Encoding.UTF8.GetString(packet.name);
                var end = name.IndexOf('\0');
                if (end >= 0) name = name.Substring(0, end);
                _tracker.Record(message.sysid, name, packet.value, DateTime.UtcNow);
            }
            catch
            {
            }
        }

        void Tick(object ignored)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                if (_disposed) return;
                var port = MainV2.comPort;
                if (port == null) return;
                Subscribe(port);

                var now = DateTime.UtcNow;
                var mav = port.MAV;
                if (!ReferenceEquals(mav, _vehicle))
                {
                    _vehicle = mav;
                    RaiseColours(_evaluator.Reset());
                }

                var linkOpen = port.BaseStream != null && port.BaseStream.IsOpen && !port.logreadmode;
                if (!linkOpen || mav == null)
                {
                    RaiseColours(_evaluator.Reset());
                    _requester.Tick(now, false, false, 0, false);
                    return;
                }

                List<CustomWarning> alerts;
                var shared = WarningEngine.warnings;
                lock (shared) alerts = shared.Where(w => w?.Alert != null).ToList();

                var context = ReadContext(mav, now);
                var needed = alerts.Any(w => w.Alert.Enabled && FlightPhase.NeedsPhase(w.Alert.Phase));
                _requester.Tick(now, needed, !port.giveComport, port.sysidcurrent, context.PhaseKnown);

                var telemetry = new LiveAlertTelemetry(port, _tracker);
                var result = _evaluator.Evaluate(alerts, context, alert => AlertSourceResolver.Read(alert, telemetry, now));
                RaiseColours(result.Colours);
                Speak(result.Decisions, mav.cs, now);
            }
            catch (Exception ex)
            {
                Trace.TraceError("BSA alert tick failed: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        static AlertContext ReadContext(MAVState mav, DateTime now)
        {
            var context = new AlertContext { NowUtc = now, Armed = mav.cs.armed };
            var packet = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE);
            if (packet != null && AlertSourceResolver.IsFresh(packet.rxtime, now, FlightPhase.PhaseFreshSeconds))
            {
                var state = packet.ToStructure<MAVLink.mavlink_extended_sys_state_t>();
                context.PhaseKnown = true;
                context.LandedState = state.landed_state;
                context.VtolState = state.vtol_state;
            }
            return context;
        }

        void Speak(List<AlertDecision> decisions, CurrentState cs, DateTime now)
        {
            var speech = MainV2.speechEngine;
            if (speech == null || !MainV2.speechEnabled())
            {
                foreach (var due in decisions.Where(d => d.SpeakText != null))
                {
                    if (due.IsAlarm) cs.messageHigh = due.SpeakText;
                    _evaluator.MarkSpoken(due.Warning, now);
                }
                return;
            }

            var ready = speech.IsReady;
            var readoutSpeaking = !ready && _lastSpokeReadout &&
                                  (now - _lastSpokenUtc).TotalSeconds < ReadoutSpeakingSeconds;
            var chosen = AlertSpeechArbiter.Choose(decisions, ready, readoutSpeaking, out var cancelFirst);
            if (chosen == null) return;
            if (cancelFirst) speech.SpeakAsyncCancelAll();
            speech.SpeakAsync(chosen.SpeakText);
            _lastSpokeReadout = !chosen.IsAlarm;
            _lastSpokenUtc = now;
            _evaluator.MarkSpoken(chosen.Warning, now);
        }

        static void RaiseColours(IEnumerable<AlertColourChange> colours)
        {
            foreach (var change in colours)
            {
                try
                {
                    WarningEngine.RaiseQuickPanelColoring(change.Field, change.Colour);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            lock (_subscriptionSync)
            {
                if (_disposed) return;
                _disposed = true;
                if (_subscribed != null) _subscribed.OnPacketReceived -= OnPacketReceived;
                _subscribed = null;
            }
            _timer.Dispose();
        }
    }

    public static class AlertComposition
    {
        static AlertService _service;

        public static void Initialize()
        {
            if (_service != null) return;
            try
            {
                _service = new AlertService();
            }
            catch (Exception ex)
            {
                Trace.TraceError("BSA alert service did not start: " + ex);
                _service = null;
            }
        }

        public static void Shutdown()
        {
            _service?.Dispose();
            _service = null;
        }
    }
}
