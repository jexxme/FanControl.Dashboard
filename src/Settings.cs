using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FanControl.Dashboard
{
    public enum ChartLayout { SideBySide = 0, Stacked = 1, Single = 2 }

    /// <summary>A sensor the user added: shown as its own card and/or as a line in a chart.</summary>
    public sealed class CustomItem
    {
        public string Id;
        public string Name;
        public string SensorId;
        /// <summary>Unit of the sensor ("°C", "%", "W", "rpm", ...), taken from its type when it was picked.</summary>
        public string Unit;
        public bool ShowCard = true;
        /// <summary>0 = not in a chart, 1 = temperature chart, 2 = load chart.</summary>
        public int Chart;
        /// <summary>Optional limit (°C for temperatures), NaN for none: headroom in the card, a line in the chart.</summary>
        public double Limit = double.NaN;
        /// <summary>Colour name from <see cref="Palette.Choices"/>; null picks one automatically.</summary>
        public string Color;

        public string Key { get { return "custom:" + Id; } }
        public string CardId { get { return "custom:" + Id; } }
    }

    /// <summary>How one fan control appears in the Fans card.</summary>
    public sealed class FanPref
    {
        public string ControlId;
        /// <summary>Name shown in the dashboard; null or empty keeps Fan Control's name.</summary>
        public string Name;
        public bool Hidden;
    }

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

        // ---- cards ----
        /// <summary>Card order: "cpu", "gpu", "ram", "fans" and "custom:&lt;id&gt;".</summary>
        public List<string> CardOrder { get; set; }
        public bool ShowCpuPower { get; set; }
        public bool ShowGpuDetails { get; set; }
        public bool ShowRamUsage { get; set; }
        public bool ShowRamModules { get; set; }

        // ---- sensors ----
        /// <summary>Sensor chosen by the user for a built-in value (key such as "cpu.temp" to Fan Control sensor id); missing = automatic.</summary>
        public Dictionary<string, string> Sensors { get; set; }
        public List<CustomItem> Custom { get; set; }

        // ---- fans ----
        /// <summary>Fan order, names and visibility; fans not listed keep Fan Control's order after the listed ones.</summary>
        public List<FanPref> Fans { get; set; }

        // ---- look ----
        public bool Fahrenheit { get; set; }
        public bool Compact { get; set; }
        /// <summary>Moving average over this many seconds for chart lines (0 = off). Cards always show the latest value.</summary>
        public int Smoothing { get; set; }
        /// <summary>Line colour per series key (colour names from <see cref="Palette.Choices"/>).</summary>
        public Dictionary<string, string> Colors { get; set; }

        public static readonly int[] Ranges = { 2, 5, 10, 30, 60 };
        public static readonly int[] Heights = { 160, 220, 300 };
        public static readonly int[] SmoothingSteps = { 0, 3, 10, 30 };
        public static readonly string[] BuiltInCards = { "cpu", "gpu", "ram", "fans" };
        /// <summary>Built-in values whose sensor can be chosen: key, label, sensor type that fits.</summary>
        public static readonly string[][] SensorSlots =
        {
            new[] { "cpu.temp", "CPU temperature", "Temperature" }, new[] { "cpu.load", "CPU load", "UsagePercent" },
            new[] { "cpu.power", "CPU power", "Power" }, new[] { "gpu.temp", "GPU temperature", "Temperature" },
            new[] { "gpu.hot", "GPU hotspot", "Temperature" }, new[] { "gpu.vram", "GPU memory (VRAM)", "Temperature" },
            new[] { "gpu.load", "GPU load", "UsagePercent" }, new[] { "ram.temp", "RAM temperature", "Temperature" },
            new[] { "ram.load", "RAM used", "UsagePercent" },
        };
        public const double CpuLimitMin = 50, CpuLimitMax = 115, RamWarningMin = 30, RamWarningMax = 95;
        public const double DefaultCpuLimit = 95, DefaultRamWarning = 55;
        public const int MaxCustom = 16;

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
            CardOrder = new List<string>(BuiltInCards);
            ShowCpuPower = ShowGpuDetails = ShowRamUsage = ShowRamModules = true;
            Sensors = new Dictionary<string, string>();
            Custom = new List<CustomItem>();
            Fans = new List<FanPref>();
            Colors = new Dictionary<string, string>();
        }

        /// <summary>Where to save; null keeps the settings in memory only (harness).</summary>
        public string FilePath;

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
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) s = FromJson(File.ReadAllText(path));
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

        public bool IsCardVisible(string id)
        {
            switch (id)
            {
                case "cpu": return ShowCpu;
                case "gpu": return ShowGpu;
                case "ram": return ShowRam;
                case "fans": return ShowFans;
                default:
                    var c = FindCustom(id);
                    return c != null && c.ShowCard;
            }
        }

        public void SetCardVisible(string id, bool visible)
        {
            switch (id)
            {
                case "cpu": ShowCpu = visible; break;
                case "gpu": ShowGpu = visible; break;
                case "ram": ShowRam = visible; break;
                case "fans": ShowFans = visible; break;
                default: var c = FindCustom(id); if (c != null) c.ShowCard = visible; break;
            }
        }

        /// <summary>The custom item for "custom:&lt;id&gt;" (card id or series key) or a bare id.</summary>
        public CustomItem FindCustom(string idOrKey)
        {
            if (idOrKey == null) return null;
            string id = idOrKey.StartsWith("custom:", StringComparison.Ordinal) ? idOrKey.Substring(7) : idOrKey;
            return Custom.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>Moves a card one place up (-1) or down (+1).</summary>
        public void MoveCard(string id, int delta)
        {
            int i = CardOrder.IndexOf(id), j = i + delta;
            if (i < 0 || j < 0 || j >= CardOrder.Count) return;
            CardOrder.RemoveAt(i); CardOrder.Insert(j, id);
        }

        public CustomItem AddCustom(string sensorId, string name, string unit)
        {
            if (Custom.Count >= MaxCustom) return null;
            string id;
            var rnd = new Random();
            do id = rnd.Next(0x1000, 0xFFFF).ToString("x4", CultureInfo.InvariantCulture); while (FindCustom(id) != null);
            var c = new CustomItem
            {
                Id = id, Name = string.IsNullOrEmpty(name) ? "Sensor" : name, SensorId = sensorId, Unit = unit ?? "",
                Chart = unit == "°C" ? 1 : unit == "%" ? 2 : 0,
            };
            Custom.Add(c);
            CardOrder.Add(c.CardId);
            return c;
        }

        public void RemoveCustom(string idOrKey)
        {
            var c = FindCustom(idOrKey);
            if (c == null) return;
            Custom.Remove(c);
            CardOrder.Remove(c.CardId);
            Colors.Remove(c.Key);
            HiddenSeries.RemoveAll(h => h.EndsWith(":" + c.Key, StringComparison.Ordinal));
        }

        /// <summary>The preference for a fan, created on first use.</summary>
        public FanPref Fan(string controlId)
        {
            var f = Fans.FirstOrDefault(p => p.ControlId == controlId);
            if (f == null) { f = new FanPref { ControlId = controlId }; Fans.Add(f); }
            return f;
        }

        internal void Sanitize()
        {
            if (Array.IndexOf(Ranges, RangeMinutes) < 0) RangeMinutes = 10;
            if (ChartHeight < 120 || ChartHeight > 500) ChartHeight = 220;
            if (!Enum.IsDefined(typeof(ChartLayout), Layout)) Layout = ChartLayout.SideBySide;
            CpuLimit = ClampCpuLimit(CpuLimit);
            RamWarning = ClampRamWarning(RamWarning);
            if (SingleMode != 1) SingleMode = 0;
            if (Array.IndexOf(SmoothingSteps, Smoothing) < 0) Smoothing = 0;
            if (HiddenSeries == null) HiddenSeries = new List<string>();
            HiddenSeries.RemoveAll(string.IsNullOrEmpty);
            if (Sensors == null) Sensors = new Dictionary<string, string>();
            foreach (var k in Sensors.Where(kv => string.IsNullOrEmpty(kv.Value) || !SensorSlots.Any(s => s[0] == kv.Key)).Select(kv => kv.Key).ToList()) Sensors.Remove(k);
            if (Colors == null) Colors = new Dictionary<string, string>();
            foreach (var k in Colors.Where(kv => !Palette.IsChoice(kv.Value)).Select(kv => kv.Key).ToList()) Colors.Remove(k);
            if (Fans == null) Fans = new List<FanPref>();
            Fans = Fans.Where(f => f != null && !string.IsNullOrEmpty(f.ControlId)).GroupBy(f => f.ControlId).Select(g => g.First()).ToList();

            if (Custom == null) Custom = new List<CustomItem>();
            Custom = Custom.Where(c => c != null && !string.IsNullOrEmpty(c.Id) && !string.IsNullOrEmpty(c.SensorId))
                           .GroupBy(c => c.Id).Select(g => g.First()).Take(MaxCustom).ToList();
            foreach (var c in Custom)
            {
                if (string.IsNullOrEmpty(c.Name)) c.Name = "Sensor";
                if (c.Unit == null) c.Unit = "";
                if (c.Chart < 0 || c.Chart > 2 || (c.Chart == 1 && c.Unit != "°C") || (c.Chart == 2 && c.Unit != "%")) c.Chart = 0;
                if (double.IsInfinity(c.Limit)) c.Limit = double.NaN;
                if (!Palette.IsChoice(c.Color)) c.Color = null;
            }

            // every built-in and every custom card exactly once, in the saved order
            if (CardOrder == null) CardOrder = new List<string>();
            var known = new HashSet<string>(BuiltInCards.Concat(Custom.Select(c => c.CardId)));
            CardOrder = CardOrder.Where(known.Contains).Distinct().ToList();
            foreach (var id in BuiltInCards.Concat(Custom.Select(c => c.CardId))) if (!CardOrder.Contains(id)) CardOrder.Add(id);
        }

        public bool IsHidden(int mode, string key) { return HiddenSeries.Contains(mode + ":" + key); }

        public void ToggleHidden(int mode, string key)
        {
            string id = mode + ":" + key;
            if (!HiddenSeries.Remove(id)) HiddenSeries.Add(id);
        }

        /// <summary>
        /// Restores every option except whether the section is collapsed and what the user created or named:
        /// custom sensors and fan names stay (their order, visibility and colours are reset).
        /// </summary>
        public void Reset()
        {
            var d = new Settings();
            ShowCpu = d.ShowCpu; ShowGpu = d.ShowGpu; ShowRam = d.ShowRam; ShowFans = d.ShowFans;
            ShowCharts = d.ShowCharts; Layout = d.Layout; SingleMode = d.SingleMode; RangeMinutes = d.RangeMinutes;
            ChartHeight = d.ChartHeight; ShowLimits = d.ShowLimits; CpuLimit = d.CpuLimit; RamWarning = d.RamWarning;
            RamWarningAuto = d.RamWarningAuto;
            HiddenSeries = new List<string>();
            CardOrder = new List<string>(BuiltInCards);
            ShowCpuPower = ShowGpuDetails = ShowRamUsage = ShowRamModules = true;
            Sensors = new Dictionary<string, string>();
            Colors = new Dictionary<string, string>();
            Fahrenheit = d.Fahrenheit; Compact = d.Compact; Smoothing = d.Smoothing;
            foreach (var c in Custom) { c.ShowCard = true; c.Color = null; }
            foreach (var f in Fans) f.Hidden = false;
            Fans = Fans.Where(f => !string.IsNullOrEmpty(f.Name)).ToList();
            Sanitize();
        }

        /// <summary>True when nothing would change on <see cref="Reset"/> (the reset button is disabled then).</summary>
        public bool IsDefault
        {
            get
            {
                var me = FromJson(ToJson()); me.Sanitize();
                var reset = FromJson(ToJson()); reset.Sanitize(); reset.Reset();
                return reset.ToJson() == me.ToJson();
            }
        }

        /// <summary>
        /// Reads the settings file. Unknown fields are ignored, and a field with the wrong type keeps its default;
        /// text that is not a JSON object throws (the caller falls back to defaults).
        /// </summary>
        internal static Settings FromJson(string text)
        {
            var d = Json.Parse(text) as Dictionary<string, object>;
            if (d == null) throw new FormatException("settings.json does not contain a JSON object");
            var s = new Settings();
            bool b; double n;
            if (Bool(d, "Collapsed", out b)) s.Collapsed = b;
            if (Bool(d, "ShowCpu", out b)) s.ShowCpu = b;
            if (Bool(d, "ShowGpu", out b)) s.ShowGpu = b;
            if (Bool(d, "ShowRam", out b)) s.ShowRam = b;
            if (Bool(d, "ShowFans", out b)) s.ShowFans = b;
            if (Bool(d, "ShowCharts", out b)) s.ShowCharts = b;
            if (Number(d, "Layout", out n)) s.Layout = (ChartLayout)(int)n;
            if (Number(d, "SingleMode", out n)) s.SingleMode = (int)n;
            if (Number(d, "RangeMinutes", out n)) s.RangeMinutes = (int)n;
            if (Number(d, "ChartHeight", out n)) s.ChartHeight = (int)n;
            if (Bool(d, "ShowLimits", out b)) s.ShowLimits = b;
            if (Number(d, "CpuLimit", out n)) s.CpuLimit = n;
            if (Number(d, "RamWarning", out n)) s.RamWarning = n;
            if (Bool(d, "RamWarningAuto", out b)) s.RamWarningAuto = b;
            foreach (var id in Strings(d, "HiddenSeries")) s.HiddenSeries.Add(id);

            var order = Strings(d, "CardOrder").ToList();
            if (order.Count > 0) s.CardOrder = order;
            if (Bool(d, "ShowCpuPower", out b)) s.ShowCpuPower = b;
            if (Bool(d, "ShowGpuDetails", out b)) s.ShowGpuDetails = b;
            if (Bool(d, "ShowRamUsage", out b)) s.ShowRamUsage = b;
            if (Bool(d, "ShowRamModules", out b)) s.ShowRamModules = b;
            s.Sensors = StringMap(d, "Sensors");
            s.Colors = StringMap(d, "Colors");
            if (Bool(d, "Fahrenheit", out b)) s.Fahrenheit = b;
            if (Bool(d, "Compact", out b)) s.Compact = b;
            if (Number(d, "Smoothing", out n)) s.Smoothing = (int)n;

            foreach (var o in Objects(d, "Custom"))
            {
                var c = new CustomItem { Id = Str(o, "Id"), Name = Str(o, "Name"), SensorId = Str(o, "SensorId"), Unit = Str(o, "Unit"), Color = Str(o, "Color") };
                if (Bool(o, "ShowCard", out b)) c.ShowCard = b;
                if (Number(o, "Chart", out n)) c.Chart = (int)n;
                if (Number(o, "Limit", out n)) c.Limit = n;
                s.Custom.Add(c);
            }
            foreach (var o in Objects(d, "Fans"))
            {
                var f = new FanPref { ControlId = Str(o, "ControlId"), Name = Str(o, "Name") };
                if (Bool(o, "Hidden", out b)) f.Hidden = b;
                s.Fans.Add(f);
            }
            return s;
        }

        static bool Bool(Dictionary<string, object> d, string key, out bool value)
        {
            object o; value = false;
            if (!d.TryGetValue(key, out o) || !(o is bool)) return false;
            value = (bool)o; return true;
        }

        static bool Number(Dictionary<string, object> d, string key, out double value)
        {
            object o; value = 0;
            if (!d.TryGetValue(key, out o) || !(o is int || o is long || o is double)) return false;
            value = Convert.ToDouble(o, CultureInfo.InvariantCulture);
            return !double.IsNaN(value) && Math.Abs(value) < 1e9;
        }

        static string Str(Dictionary<string, object> d, string key) { object o; return d.TryGetValue(key, out o) ? o as string : null; }

        static IEnumerable<string> Strings(Dictionary<string, object> d, string key)
        {
            object o;
            var list = d.TryGetValue(key, out o) ? o as List<object> : null;
            return list == null ? Enumerable.Empty<string>() : list.OfType<string>();
        }

        static IEnumerable<Dictionary<string, object>> Objects(Dictionary<string, object> d, string key)
        {
            object o;
            var list = d.TryGetValue(key, out o) ? o as List<object> : null;
            return list == null ? Enumerable.Empty<Dictionary<string, object>>() : list.OfType<Dictionary<string, object>>();
        }

        static Dictionary<string, string> StringMap(Dictionary<string, object> d, string key)
        {
            object o;
            var map = new Dictionary<string, string>();
            var src = d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null;
            if (src != null) foreach (var kv in src) { var v = kv.Value as string; if (v != null) map[kv.Key] = v; }
            return map;
        }

        /// <summary>The JSON written to disk (stable field order, enum as number). The first fields are the original format.</summary>
        internal string ToJson()
        {
            var copy = new Dictionary<string, object>
            {
                { "Collapsed", Collapsed }, { "ShowCpu", ShowCpu }, { "ShowGpu", ShowGpu }, { "ShowRam", ShowRam },
                { "ShowFans", ShowFans }, { "ShowCharts", ShowCharts }, { "Layout", (int)Layout }, { "SingleMode", SingleMode },
                { "RangeMinutes", RangeMinutes }, { "ChartHeight", ChartHeight }, { "ShowLimits", ShowLimits },
                { "CpuLimit", CpuLimit }, { "RamWarning", RamWarning }, { "RamWarningAuto", RamWarningAuto }, { "HiddenSeries", HiddenSeries },
                { "CardOrder", CardOrder }, { "ShowCpuPower", ShowCpuPower }, { "ShowGpuDetails", ShowGpuDetails },
                { "ShowRamUsage", ShowRamUsage }, { "ShowRamModules", ShowRamModules },
                { "Sensors", Sorted(Sensors) }, { "Colors", Sorted(Colors) },
                { "Fahrenheit", Fahrenheit }, { "Compact", Compact }, { "Smoothing", Smoothing },
                { "Custom", Custom.Select(c =>
                    {
                        var o = new Dictionary<string, object>
                        {
                            { "Id", c.Id }, { "Name", c.Name }, { "SensorId", c.SensorId }, { "Unit", c.Unit }, { "ShowCard", c.ShowCard },
                            { "Chart", c.Chart }, { "Color", c.Color },
                        };
                        if (!double.IsNaN(c.Limit)) o["Limit"] = c.Limit;
                        return o;
                    }).ToList() },
                { "Fans", Fans.Select(f => new Dictionary<string, object> { { "ControlId", f.ControlId }, { "Name", f.Name }, { "Hidden", f.Hidden } }).ToList() },
            };
            return Json.Write(copy);
        }

        static SortedDictionary<string, string> Sorted(Dictionary<string, string> d) { return new SortedDictionary<string, string>(d, StringComparer.Ordinal); }

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
