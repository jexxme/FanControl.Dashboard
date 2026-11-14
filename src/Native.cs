using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FanControl.Dashboard
{
    static class CpuTimes
    {
        static long _idle, _total;
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong a, b, c, d, e, f, g; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        public static float Sample()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return float.NaN;
            long total = kernel + user, dIdle = idle - _idle, dTotal = total - _total;
            _idle = idle; _total = total;
            return dTotal <= 0 ? float.NaN : (float)Math.Max(0, Math.Min(100, 100.0 * (dTotal - dIdle) / dTotal));
        }

        public static bool RamGb(out double used, out double total)
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            used = total = double.NaN;
            if (!GlobalMemoryStatusEx(ref m)) return false;
            total = m.a / 1073741824.0; used = (m.a - m.b) / 1073741824.0;
            return true;
        }

        public static float Ram()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? m.dwMemoryLoad : float.NaN;
        }
    }

    /// <summary>GPU utilisation like Task Manager: busiest engine of the adapter with the most dedicated memory in use.</summary>
    sealed class GpuLoad : IDisposable
    {
        const uint PDH_MORE_DATA = 0x800007D2, PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000;
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string src, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint fmt, ref uint size, out uint count, IntPtr buffer);

        IntPtr _query, _engines, _memory;
        bool _opened;

        public float Sample()
        {
            if (!_opened)
            {
                _opened = true;
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return float.NaN; }
                PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engines);
                PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _memory);
                PdhCollectQueryData(_query);
                return float.NaN;
            }
            if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return float.NaN;
            var mem = Read(_memory); var eng = Read(_engines);
            if (mem == null || eng == null) return float.NaN;
            string gpu = null; double best = -1;
            foreach (var kv in mem) if (kv.Value > best) { best = kv.Value; gpu = Luid(kv.Key); }
            var perEngine = new Dictionary<string, double>();
            foreach (var kv in eng)
            {
                if (gpu != null && Luid(kv.Key) != gpu) continue;
                int p = kv.Key.IndexOf("luid_", StringComparison.Ordinal);
                string key = p >= 0 ? kv.Key.Substring(p) : kv.Key;
                double v; perEngine.TryGetValue(key, out v); perEngine[key] = v + kv.Value;
            }
            double max = 0; foreach (var v in perEngine.Values) max = Math.Max(max, v);
            return (float)Math.Max(0, Math.Min(100, max));
        }

        static string Luid(string s)
        {
            int p = s.IndexOf("luid_", StringComparison.Ordinal); if (p < 0) return s;
            int q = s.IndexOf("_phys", p, StringComparison.Ordinal);
            return q > p ? s.Substring(p, q - p) : s.Substring(p);
        }

        static Dictionary<string, double> Read(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return null;
            uint size = 0, count;
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, IntPtr.Zero) != PDH_MORE_DATA || size == 0) return null;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return null;
                int item = IntPtr.Size == 8 ? 24 : 16, valueOffset = IntPtr.Size == 8 ? 16 : 8;
                var result = new Dictionary<string, double>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + (long)i * item);
                    if ((uint)Marshal.ReadInt32(p, IntPtr.Size) > 1) continue;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p));
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, valueOffset));
                    if (name == null || double.IsNaN(v)) continue;
                    double prev; result.TryGetValue(name, out prev); result[name] = prev + v;
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
            _engines = _memory = IntPtr.Zero; _opened = false;
        }
    }
}
