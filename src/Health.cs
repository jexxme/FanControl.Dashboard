using System;
using System.Runtime.InteropServices;

namespace FanControl.Dashboard
{
    /// <summary>
    /// Memory watchdog and render gate. Fan Control ran out of memory twice (WPF render thread, commit ~58 GB)
    /// while the dashboard was installed, under conditions not reproducible on demand. The view only redraws
    /// while someone can see it, and stops redrawing for good if the process grows past a limit,
    /// so the log shows whether growth continues without the dashboard drawing.
    /// </summary>
    public static class Health
    {
        public const long LimitMb = 2048;
        public static volatile bool Tripped;
        static DateTime _nextLog = DateTime.MinValue, _nextCheck = DateTime.MinValue;
        static long _trippedAt, _lastReported;

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

        /// <summary>Seconds since the last keyboard/mouse input (also counts while the session is locked or the display is off).</summary>
        public static double IdleSeconds()
        {
            var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref i)) return 0;
            return unchecked((uint)Environment.TickCount - i.dwTime) / 1000.0;
        }

        public static long PrivateMb()
        {
            using (var p = System.Diagnostics.Process.GetCurrentProcess()) return p.PrivateMemorySize64 / (1024 * 1024);
        }

        /// <summary>Called once per sampler tick; checks every minute, logs every 30 minutes.</summary>
        public static void Check(Action<string> log)
        {
            var now = DateTime.UtcNow;
            if (now < _nextCheck) return;
            _nextCheck = now.AddMinutes(1);
            long mb = PrivateMb();
            if (now >= _nextLog) { _nextLog = now.AddMinutes(30); log("memory: Fan Control private " + mb + " MB" + (Tripped ? " (dashboard drawing stopped)" : "")); }
            if (!Tripped && mb > LimitMb)
            {
                Tripped = true; _trippedAt = _lastReported = mb;
                log("memory: Fan Control private " + mb + " MB is over " + LimitMb + " MB, dashboard stops drawing until Fan Control restarts");
            }
            else if (Tripped && mb > _lastReported + 1024)
            {
                _lastReported = mb;
                log("memory: still growing with the dashboard not drawing (" + _trippedAt + " -> " + mb + " MB): the leak is not the dashboard view");
            }
        }
    }
}
