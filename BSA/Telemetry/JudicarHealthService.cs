using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MissionPlanner.BSA.Config;
using MissionPlanner.BSA.Core;
using Newtonsoft.Json;

namespace MissionPlanner.BSA.Telemetry
{
    public sealed class JudicarHealthService : IDisposable
    {
        readonly JudicarHealthEvaluator _evaluator;
        readonly Timer _timer;
        readonly object _durationSync = new object();
        readonly long[] _recentEvaluationTicks = new long[256];
        long _evaluationCount;
        long _totalEvaluationTicks;
        int _recentEvaluationCount;
        int _recentEvaluationIndex;
        bool _disposed;

        public JudicarHealthService(BsaHealthRuleSet rules)
        {
            _evaluator = new JudicarHealthEvaluator(rules);
            var period = Math.Max(250, (int)Math.Ceiling(1000.0 / rules.EvaluationHz));
            MainV2.comPort.OnPacketReceived += OnPacketReceived;
            _timer = new Timer(Evaluate, null, period, period);
        }

        public long EvaluationCount => Interlocked.Read(ref _evaluationCount);
        public double MeanEvaluationMilliseconds => EvaluationCount == 0 ? 0 :
            Interlocked.Read(ref _totalEvaluationTicks) * 1000.0 / Stopwatch.Frequency / EvaluationCount;

        public double P95EvaluationMilliseconds
        {
            get
            {
                long[] sample;
                lock (_durationSync)
                    sample = _recentEvaluationTicks.Take(_recentEvaluationCount).OrderBy(value => value).ToArray();
                if (sample.Length == 0) return 0;
                var index = Math.Min(sample.Length - 1, (int)Math.Ceiling(sample.Length * 0.95) - 1);
                return sample[index] * 1000.0 / Stopwatch.Frequency;
            }
        }

        void OnPacketReceived(object sender, MAVLink.MAVLinkMessage message)
        {
            if (message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.NAMED_VALUE_FLOAT) return;
            var selectedSystem = MainV2.comPort.sysidcurrent;
            if (selectedSystem <= 0 || message.sysid != selectedSystem) return;
            var packet = message.ToStructure<MAVLink.mavlink_named_value_float_t>();
            var name = Encoding.UTF8.GetString(packet.name).TrimEnd('\0');
            _evaluator.RecordNamedValue(name, packet.value, DateTime.UtcNow);
        }

        void Evaluate(object ignored)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var state = MainV2.comPort.MAV.cs;
                var values = _evaluator.Evaluate(state.armed, state.gpsstatus, state.gpsstatus2, DateTime.UtcNow);
                state.J26_DATA_OK = values.DataOk;
                state.J26_ESC_OK = values.EscOk;
                state.J26_GPS_RED_OK = values.GpsRedundancyOk;
            }
            catch
            {
                try
                {
                    var state = MainV2.comPort.MAV.cs;
                    if (state.armed) state.J26_DATA_OK = state.J26_ESC_OK = state.J26_GPS_RED_OK = 0;
                }
                catch { }
            }
            finally
            {
                var elapsed = Stopwatch.GetTimestamp() - started;
                Interlocked.Increment(ref _evaluationCount);
                Interlocked.Add(ref _totalEvaluationTicks, elapsed);
                lock (_durationSync)
                {
                    _recentEvaluationTicks[_recentEvaluationIndex] = elapsed;
                    _recentEvaluationIndex = (_recentEvaluationIndex + 1) % _recentEvaluationTicks.Length;
                    if (_recentEvaluationCount < _recentEvaluationTicks.Length) _recentEvaluationCount++;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            MainV2.comPort.OnPacketReceived -= OnPacketReceived;
            _timer.Dispose();
        }
    }

    public static class JudicarHealthComposition
    {
        static JudicarHealthService _service;

        public static void Initialize()
        {
            if (_service != null || !File.Exists(BsaPaths.ActiveHealthRulesPath)) return;
            try
            {
                var rules = JsonConvert.DeserializeObject<BsaHealthRuleSet>(File.ReadAllText(BsaPaths.ActiveHealthRulesPath));
                _service = new JudicarHealthService(rules);
            }
            catch (Exception ex)
            {
                Trace.TraceError("BSA health service did not start: " + ex);
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
