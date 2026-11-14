using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FanControl.Dashboard
{
    /// <summary>One fan row as configured in Fan Control (enabled controls only).</summary>
    public sealed class FanInfo
    {
        public string Name;
        public string ControlId;
        public string RpmId;
        public string Curve;
    }

    /// <summary>Latest values plus a 30-minute history, sampled once per second on a background thread.</summary>
    public sealed class Sampler
    {
        public const int Capacity = 1800; // 30 min at 1 Hz

        public static readonly string[] HistoryKeys = { "cpu.temp", "gpu.temp", "gpu.hot", "gpu.vram", "cpu.load", "gpu.load", "ram.load", "ram.temp" };

        readonly object _lock = new object();
        readonly HistoryBuffer _history = new HistoryBuffer(HistoryKeys, Capacity);
        Dictionary<string, float> _latest = new Dictionary<string, float>();
        SensorMap _map = new SensorMap();
        List<FanInfo> _fans = new List<FanInfo>();
        int _sampleFailures;
        Thread _thread;
        volatile bool _stop;
        DateTime _lastDiscovery = DateTime.MinValue, _configStamp = DateTime.MinValue;
        string _configPath;
        object _client;
        readonly GpuLoad _gpu = new GpuLoad();
        readonly Action<string> _log;

        public string GpuName { get; private set; }
        public string ConfigName { get; private set; }
        /// <summary>Display names of the memory modules ("DIMM #1", ...), same order as the ram.dimmN keys.</summary>
        public List<string> DimmNames { get; private set; }
        /// <summary>True once Fan Control's sensor list has been read at least once.</summary>
        public bool Discovered { get; private set; }
        /// <summary>When the last complete sample was taken (MinValue: never).</summary>
        public DateTime LastSampleUtc { get; private set; }
        Dictionary<string, string> _names = new Dictionary<string, string>();

        /// <summary>Short name of the sensor behind a key (e.g. "Tctl/Tdie" for cpu.temp), null if none was found.</summary>
        public string SensorName(string key)
        {
            lock (_lock) { string n; return _names.TryGetValue(key, out n) ? n : null; }
        }

        public Sampler(Action<string> log)
        {
            _log = log ?? (s => { });
            DimmNames = new List<string>();
        }

        public void Start()
        {
            if (_thread != null) return;
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Name = "FanControl.Dashboard sampler", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            var t = _thread; _thread = null;
            if (t != null && !t.Join(3000)) _log("sampler did not stop in time");
            _gpu.Dispose();
        }

        public float Get(string key)
        {
            lock (_lock) { float v; return _latest.TryGetValue(key, out v) ? v : float.NaN; }
        }

        public List<FanInfo> Fans { get { lock (_lock) return new List<FanInfo>(_fans); } }

        /// <summary>Copies the last <paramref name="points"/> samples of a series, oldest first (NaN = no data).</summary>
        public float[] History(string key, int points)
        {
            lock (_lock) return _history.Read(key, points);
        }

        /// <summary>
        /// Records one sample from an outside source instead of Fan Control (demo mode of the harness).
        /// Do not mix with <see cref="Start"/>.
        /// </summary>
        internal void Feed(IDictionary<string, float> latest, List<FanInfo> fans, List<string> dimmNames, Dictionary<string, string> names, string gpuName, string configName)
        {
            lock (_lock)
            {
                _latest = new Dictionary<string, float>(latest);
                if (fans != null) _fans = new List<FanInfo>(fans);
                if (dimmNames != null) DimmNames = new List<string>(dimmNames);
                if (names != null) _names = new Dictionary<string, string>(names);
                _history.Push(_latest);
            }
            GpuName = gpuName; ConfigName = configName; Discovered = true; LastSampleUtc = DateTime.UtcNow;
        }

        void Loop()
        {
            var next = DateTime.UtcNow;
            while (!_stop)
            {
                try { Tick(); }
                catch (Exception ex)
                {
                    // an occasional IPC timeout just leaves a one-second gap; log the first few, then every 100th
                    if (++_sampleFailures <= 3 || _sampleFailures % 100 == 0) _log("sample failed (" + _sampleFailures + "x): " + ex.Message);
                    _client = null;
                }
                try { Health.Check(_log); } catch { }
                next = next.AddSeconds(1);
                var wait = next - DateTime.UtcNow;
                if (wait < TimeSpan.Zero) { next = DateTime.UtcNow; wait = TimeSpan.Zero; }
                for (int slept = 0; slept < wait.TotalMilliseconds && !_stop; slept += 100) Thread.Sleep(100);
            }
        }

        void Tick()
        {
            RefreshConfig();
            SensorMap map; lock (_lock) map = _map;
            if (!Discovered || (map.Ids.Count < SensorMatcher.MaxKeys && (DateTime.UtcNow - _lastDiscovery).TotalSeconds > 60))
            {
                Discover();
                lock (_lock) map = _map;
            }

            var wanted = new List<string>(map.Ids.Values);
            wanted.AddRange(map.Dimms.Select(d => d.Id));
            wanted.AddRange(map.DimmLimits.Select(d => d.Id));
            List<FanInfo> fans; lock (_lock) fans = new List<FanInfo>(_fans);
            foreach (var f in fans) { if (f.ControlId != null) wanted.Add(f.ControlId); if (f.RpmId != null) wanted.Add(f.RpmId); }

            Dictionary<string, float> values = Ipc.Read(ref _client, wanted.Distinct().ToList());
            float gpuLoad = _gpu.Sample();

            var latest = new Dictionary<string, float>();
            foreach (var kv in map.Ids) { float v; if (values != null && values.TryGetValue(kv.Value, out v)) latest[kv.Key] = v; }
            foreach (var f in fans)
            {
                float v;
                if (values != null && f.ControlId != null && values.TryGetValue(f.ControlId, out v)) latest["duty:" + f.ControlId] = v;
                if (values != null && f.RpmId != null && values.TryGetValue(f.RpmId, out v)) latest["rpm:" + f.RpmId] = v;
            }
            if (!float.IsNaN(gpuLoad)) latest["gpu.load"] = gpuLoad;
            if (!latest.ContainsKey("cpu.load")) { float c = CpuTimes.Sample(); if (!float.IsNaN(c)) latest["cpu.load"] = c; }
            else CpuTimes.Sample();
            if (!latest.ContainsKey("ram.load")) latest["ram.load"] = CpuTimes.Ram();
            double usedGb, totalGb;
            if (CpuTimes.RamGb(out usedGb, out totalGb)) { latest["ram.used"] = (float)usedGb; latest["ram.total"] = (float)totalGb; }

            float dimmMax = float.NaN;
            for (int d = 0; d < map.Dimms.Count; d++)
            {
                float v;
                if (values == null || !values.TryGetValue(map.Dimms[d].Id, out v) || v <= 0) continue;
                latest["ram.dimm" + d] = v;
                if (float.IsNaN(dimmMax) || v > dimmMax) dimmMax = v;
            }
            if (!float.IsNaN(dimmMax)) latest["ram.temp"] = dimmMax;
            // the lowest "high" threshold any module reports (DDR5 SPD hub, typically 55 °C)
            float limit = float.NaN;
            foreach (var s in map.DimmLimits)
            {
                float v;
                if (values == null || !values.TryGetValue(s.Id, out v) || v < 30 || v > 125) continue;
                if (float.IsNaN(limit) || v < limit) limit = v;
            }
            if (!float.IsNaN(limit)) latest["ram.limit"] = limit;

            lock (_lock)
            {
                _latest = latest;
                _history.Push(latest);
            }
            LastSampleUtc = DateTime.UtcNow;
        }

        void Discover()
        {
            _lastDiscovery = DateTime.UtcNow;
            var all = Ipc.All(ref _client);
            if (all == null) return;
            var map = SensorMatcher.Match(all);
            lock (_lock)
            {
                _map = map;
                _names = new Dictionary<string, string>(map.Names);
                DimmNames = map.Dimms.Select(s => s.Name).ToList();
            }
            if (map.GpuName != null) GpuName = map.GpuName;
            Discovered = true;
        }

        void RefreshConfig()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string cache = Path.Combine(dir, "Configurations", "CACHE");
                if (!File.Exists(cache)) cache = @"C:\Program Files (x86)\FanControl\Configurations\CACHE";
                if (!File.Exists(cache)) return;
                var c = Json.Parse(File.ReadAllText(cache)) as Dictionary<string, object>;
                if (c == null) return;
                string folder = c.ContainsKey("CustomConfigFolder") ? c["CustomConfigFolder"] as string : null;
                if (string.IsNullOrEmpty(folder)) folder = Path.GetDirectoryName(cache);
                string file = c.ContainsKey("CurrentConfigFileName") ? c["CurrentConfigFileName"] as string : null;
                if (string.IsNullOrEmpty(file)) return;
                string path = Path.Combine(folder, file);
                if (!File.Exists(path)) return;
                var stamp = File.GetLastWriteTimeUtc(path);
                if (path == _configPath && stamp == _configStamp) return;
                _configPath = path; _configStamp = stamp; ConfigName = Path.GetFileNameWithoutExtension(file);

                var list = FanList.Parse(Json.Parse(File.ReadAllText(path)) as Dictionary<string, object>);
                lock (_lock) _fans = list;
            }
            catch (Exception ex) { _log("config read failed: " + ex.Message); }
        }
    }

    /// <summary>Reads the enabled fan controls from a Fan Control configuration (.json) document.</summary>
    static class FanList
    {
        public static List<FanInfo> Parse(Dictionary<string, object> cfg)
        {
            var list = new List<FanInfo>();
            var fc = cfg != null && cfg.ContainsKey("FanControl") ? cfg["FanControl"] as Dictionary<string, object> : null;
            var controls = fc != null && fc.ContainsKey("Controls") ? fc["Controls"] as IEnumerable : null;
            if (controls == null) return list;
            foreach (var o in controls)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !(d.ContainsKey("Enable") && d["Enable"] is bool && (bool)d["Enable"])) continue;
                var paired = d.ContainsKey("PairedFanSensor") ? d["PairedFanSensor"] as Dictionary<string, object> : null;
                var curve = d.ContainsKey("SelectedFanCurve") ? d["SelectedFanCurve"] as Dictionary<string, object> : null;
                list.Add(new FanInfo
                {
                    Name = d.ContainsKey("NickName") ? d["NickName"] as string : null,
                    ControlId = d.ContainsKey("Identifier") ? d["Identifier"] as string : null,
                    RpmId = paired != null && paired.ContainsKey("Identifier") ? paired["Identifier"] as string : null,
                    Curve = curve != null && curve.ContainsKey("Name") ? curve["Name"] as string : null,
                });
            }
            return list;
        }
    }

    /// <summary>Talks to Fan Control's own sensor IPC (same pipe the app exposes for tools).</summary>
    static class Ipc
    {
        public static List<SensorInfo> All(ref object client)
        {
            var c = Client(ref client);
            var reply = c.GetAllSensors(new GetAllSensorsRequest(), null, DateTime.UtcNow.AddSeconds(3), CancellationToken.None);
            return reply.Sensors.Select(s => new SensorInfo(s.Identifier, s.Type.ToString(), s.Name)).ToList();
        }

        public static Dictionary<string, float> Read(ref object client, List<string> ids)
        {
            if (ids.Count == 0) return new Dictionary<string, float>();
            var c = Client(ref client);
            var req = new ReadSensorValuesRequest();
            req.Ids.AddRange(ids);
            var reply = c.ReadSensorValues(req, null, DateTime.UtcNow.AddSeconds(2), CancellationToken.None);
            return reply.Values.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        static SensorsRPC.SensorsRPCClient Client(ref object client)
        {
            if (client == null) client = FanControl.IPC.IPCFactory.GetSensorClient();
            return (SensorsRPC.SensorsRPCClient)client;
        }
    }
}
