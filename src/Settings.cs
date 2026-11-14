using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace FanControl.Dashboard
{
    public enum ChartLayout { SideBySide = 0, Stacked = 1, Single = 2 }

    /// <summary>User options for the dashboard, stored as JSON in %AppData%\FanControl.Dashboard\settings.json.</summary>
    public sealed class Settings
    {
        public bool Collapsed { get; set; }
        public bool ShowCpu { get; set; }
        public bool ShowGpu { get; set; }
        public bool ShowRam { get; set; }
        public bool ShowFans { get; set; }
        public bool ShowCharts { get; set; }
        public ChartLayout Layout { get; set; }
        /// <summary>Chart in the single layout: 0 = temperature, 1 = load.</summary>
        public int SingleMode { get; set; }
        public int RangeMinutes { get; set; }
        public int ChartHeight { get; set; }
        public bool ShowLimits { get; set; }
        /// <summary>CPU temperature limit line and headroom (°C). Set it to your CPU's Tjmax.</summary>
        public double CpuLimit { get; set; }
        /// <summary>RAM warning (°C), used when <see cref="RamWarningAuto"/> is off or the modules report no threshold.</summary>
        public double RamWarning { get; set; }
        /// <summary>Use the lowest "high" threshold the memory modules report themselves (DDR5 SPD hub).</summary>
        public bool RamWarningAuto { get; set; }
        /// <summary>Series hidden by clicking the legend, as "mode:key" (e.g. "0:gpu.vram").</summary>
        public List<string> HiddenSeries { get; set; }

        public static readonly int[] Ranges = { 2, 5, 10, 30 };
        public static readonly int[] Heights = { 160, 220, 300 };
        public const double CpuLimitMin = 50, CpuLimitMax = 115, RamWarningMin = 30, RamWarningMax = 95;
        public const double DefaultCpuLimit = 95, DefaultRamWarning = 55;

        public Settings()
        {
            ShowCpu = ShowGpu = ShowRam = ShowFans = ShowCharts = true;
            Layout = ChartLayout.SideBySide;
            RangeMinutes = 10;
            ChartHeight = 220;
            ShowLimits = true;
            CpuLimit = DefaultCpuLimit;     // AMD Ryzen 7000/9000 Tjmax; Intel desktop CPUs are usually 100
            RamWarning = DefaultRamWarning; // DDR5 SPD "high" threshold most modules report
            RamWarningAuto = true;
            HiddenSeries = new List<string>();
        }

        /// <summary>Where to save; null keeps the settings in memory only (harness).</summary>
        [ScriptIgnore] public string FilePath;

        public event Action Changed;

        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FanControl.Dashboard", "settings.json"); }
        }

        public static Settings Load(string path, Action<string> log)
        {
            Settings s = null;
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
            }
            catch (Exception ex) { if (log != null) log("settings unreadable, using defaults: " + ex.Message); }
            s = s ?? new Settings();
            s.FilePath = path;
            s.Sanitize();
            return s;
        }

        /// <summary>The RAM warning in effect: the modules' own threshold when enabled and known, else the manual value.</summary>
        public double EffectiveRamWarning(float moduleLimit)
        {
            return RamWarningAuto && !float.IsNaN(moduleLimit) && moduleLimit >= RamWarningMin && moduleLimit <= RamWarningMax ? moduleLimit : RamWarning;
        }

        public static double ClampCpuLimit(double v) { return Clamp(v, CpuLimitMin, CpuLimitMax, DefaultCpuLimit); }
        public static double ClampRamWarning(double v) { return Clamp(v, RamWarningMin, RamWarningMax, DefaultRamWarning); }

        static double Clamp(double v, double min, double max, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            return Math.Round(Math.Max(min, Math.Min(max, v)));
        }

        internal void Sanitize()
        {
            if (Array.IndexOf(Ranges, RangeMinutes) < 0) RangeMinutes = 10;
            if (ChartHeight < 120 || ChartHeight > 500) ChartHeight = 220;
            if (!Enum.IsDefined(typeof(ChartLayout), Layout)) Layout = ChartLayout.SideBySide;
            CpuLimit = ClampCpuLimit(CpuLimit);
            RamWarning = ClampRamWarning(RamWarning);
            if (SingleMode != 1) SingleMode = 0;
            if (HiddenSeries == null) HiddenSeries = new List<string>();
            HiddenSeries.RemoveAll(string.IsNullOrEmpty);
        }

        public bool IsHidden(int mode, string key) { return HiddenSeries.Contains(mode + ":" + key); }

        public void ToggleHidden(int mode, string key)
        {
            string id = mode + ":" + key;
            if (!HiddenSeries.Remove(id)) HiddenSeries.Add(id);
        }

        /// <summary>Restores every option except whether the section is collapsed.</summary>
        public void Reset()
        {
            var d = new Settings();
            ShowCpu = d.ShowCpu; ShowGpu = d.ShowGpu; ShowRam = d.ShowRam; ShowFans = d.ShowFans;
            ShowCharts = d.ShowCharts; Layout = d.Layout; SingleMode = d.SingleMode; RangeMinutes = d.RangeMinutes;
            ChartHeight = d.ChartHeight; ShowLimits = d.ShowLimits; CpuLimit = d.CpuLimit; RamWarning = d.RamWarning;
            RamWarningAuto = d.RamWarningAuto;
            HiddenSeries = new List<string>();
        }

        /// <summary>True when every option equals its default (the reset button is disabled then).</summary>
        public bool IsDefault
        {
            get
            {
                var d = new Settings();
                return ShowCpu == d.ShowCpu && ShowGpu == d.ShowGpu && ShowRam == d.ShowRam && ShowFans == d.ShowFans && ShowCharts == d.ShowCharts
                    && Layout == d.Layout && SingleMode == d.SingleMode && RangeMinutes == d.RangeMinutes && ChartHeight == d.ChartHeight
                    && ShowLimits == d.ShowLimits && CpuLimit == d.CpuLimit && RamWarning == d.RamWarning && RamWarningAuto == d.RamWarningAuto
                    && HiddenSeries.Count == 0;
            }
        }

        /// <summary>The JSON written to disk (stable field order, enum as number).</summary>
        internal string ToJson()
        {
            var copy = new Dictionary<string, object>
            {
                { "Collapsed", Collapsed }, { "ShowCpu", ShowCpu }, { "ShowGpu", ShowGpu }, { "ShowRam", ShowRam },
                { "ShowFans", ShowFans }, { "ShowCharts", ShowCharts }, { "Layout", (int)Layout }, { "SingleMode", SingleMode },
                { "RangeMinutes", RangeMinutes }, { "ChartHeight", ChartHeight }, { "ShowLimits", ShowLimits },
                { "CpuLimit", CpuLimit }, { "RamWarning", RamWarning }, { "RamWarningAuto", RamWarningAuto }, { "HiddenSeries", HiddenSeries },
            };
            return new JavaScriptSerializer().Serialize(copy);
        }

        /// <summary>Notifies the view and writes the file (errors are ignored: settings are a convenience).</summary>
        public void Save()
        {
            Sanitize();
            var h = Changed; if (h != null) h();
            if (string.IsNullOrEmpty(FilePath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                // write a temp file and swap it in, so a crash mid-write never leaves a truncated settings file
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, ToJson());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }
}
