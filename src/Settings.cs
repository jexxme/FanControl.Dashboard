using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FanControl.Dashboard
{
    /// <summary>
    /// A card on the dashboard. A sensor card shows a main value (big), an optional bar (a percentage),
    /// optional details (small values) and an optional limit. The CPU, GPU and RAM cards are presets of it.
    /// Values are referenced by <see cref="Refs"/>: "auto:&lt;key&gt;" (detected automatically) or "fc:&lt;sensor id&gt;".
    /// </summary>
    public sealed class TileConfig
    {
        public string Id;
        /// <summary>"sensor" or "fans".</summary>
        public string Kind = "sensor";
        public string Title;
        public string Main;
        /// <summary>Shown instead of <see cref="Main"/> when that has no sensor (RAM without module temperatures shows the amount in use).</summary>
        public string Fallback;
        public string Bar;
        public string BarLabel;
        public List<string> Details = new List<string>();
        /// <summary>Limit in the main value's unit (°C for temperatures); NaN for none.</summary>
        public double Limit = double.NaN;
        /// <summary>Use the limit the memory modules report themselves (DDR5) when there is one.</summary>
        public bool LimitFromModules;
        public bool Hidden;

        public bool IsFans { get { return Kind == "fans"; } }
    }

    public sealed class LineConfig
    {
        public string Ref;
        public bool Hidden;
    }

    public sealed class ChartConfig
    {
        public string Id;
        public string Title;
        public List<LineConfig> Lines = new List<LineConfig>();
        public bool Hidden;
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
        public const int CurrentVersion = 2;
        public bool Collapsed { get; set; }
        public int RangeMinutes { get; set; }
        public int ChartHeight { get; set; }
        /// <summary>Charts one below the other instead of side by side when there is room.</summary>
        public bool ChartsStacked { get; set; }
        public bool ShowLimits { get; set; }
        public bool Fahrenheit { get; set; }
        public bool Compact { get; set; }
        /// <summary>Moving average over this many seconds for chart lines (0 = off). Cards always show the latest value.</summary>
        public int Smoothing { get; set; }
        public List<TileConfig> Tiles { get; set; }
        public List<ChartConfig> Charts { get; set; }
        /// <summary>Colour per value (ref to colour name from <see cref="Palette.Choices"/>); the card and its chart line share it.</summary>
        public Dictionary<string, string> Colors { get; set; }
        /// <summary>Fan order, names and visibility; fans not listed keep Fan Control's order after the listed ones.</summary>
        public List<FanPref> Fans { get; set; }

        public static readonly int[] Ranges = { 2, 5, 10, 30, 60 };
        public static readonly int[] Heights = { 160, 220, 300 };
        public static readonly int[] SmoothingSteps = { 0, 3, 10, 30 };
        public const double DefaultCpuLimit = 95, DefaultRamWarning = 55;
        public const int MaxTiles = 24, MaxCharts = 8, MaxLines = 12, MaxDetails = 8;

        public Settings()
        {
            RangeMinutes = 10;
            ChartHeight = 220;
            ShowLimits = true;
            Tiles = DefaultTiles();
            Charts = DefaultCharts();
            Colors = new Dictionary<string, string>();
            Fans = new List<FanPref>();
        }

        // ---------------------------------------------------------------- presets

        public static TileConfig Preset(string id)
        {
            switch (id)
            {
                case "cpu":
                    return new TileConfig { Id = "cpu", Title = "CPU", Main = "auto:cpu.temp", Bar = "auto:cpu.load", BarLabel = "Load", Details = { "auto:cpu.power" }, Limit = DefaultCpuLimit };
                case "gpu":
                    return new TileConfig { Id = "gpu", Title = "GPU", Main = "auto:gpu.temp", Bar = "auto:gpu.load", BarLabel = "Load", Details = { "auto:gpu.hot", "auto:gpu.vram" } };
                case "ram":
                    return new TileConfig { Id = "ram", Title = "RAM", Main = "auto:ram.temp", Fallback = "auto:ram.used", Bar = "auto:ram.load", BarLabel = "Used", Details = { "auto:ram.used", "auto:ram.modules" }, Limit = DefaultRamWarning, LimitFromModules = true };
                case "fans":
                    return new TileConfig { Id = "fans", Kind = "fans", Title = "Fans" };
                default: return null;
            }
        }

        public static readonly string[] PresetIds = { "cpu", "gpu", "ram", "fans" };

        static List<TileConfig> DefaultTiles() { return PresetIds.Select(Preset).ToList(); }

        static List<ChartConfig> DefaultCharts()
        {
            return new List<ChartConfig>
            {
                new ChartConfig { Id = "temp", Title = "Temperature", Lines = new[] { "cpu.temp", "gpu.temp", "gpu.hot", "gpu.vram", "ram.temp" }.Select(k => new LineConfig { Ref = "auto:" + k }).ToList() },
                new ChartConfig { Id = "load", Title = "Load", Lines = new[] { "cpu.load", "gpu.load", "ram.load" }.Select(k => new LineConfig { Ref = "auto:" + k }).ToList() },
            };
        }

        // ---------------------------------------------------------------- persistence

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

        // ---------------------------------------------------------------- queries and edits

        public TileConfig Tile(string id) { return Tiles.FirstOrDefault(t => t.Id == id); }
        public ChartConfig Chart(string id) { return Charts.FirstOrDefault(c => c.Id == id); }

        public string NewId(string prefix)
        {
            var rnd = new Random();
            string id;
            do id = prefix + rnd.Next(0x1000, 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);
            while (Tiles.Any(t => t.Id == id) || Charts.Any(c => c.Id == id));
            return id;
        }

        /// <summary>Moves an item in a list to another index (clamped). Used for drag and the arrow buttons.</summary>
        public static void Move<T>(List<T> list, T item, int toIndex)
        {
            int from = list.IndexOf(item);
            if (from < 0) return;
            toIndex = Math.Max(0, Math.Min(list.Count - 1, toIndex));
            if (toIndex == from) return;
            list.RemoveAt(from);
            list.Insert(toIndex, item);
        }

        /// <summary>
        /// Points a card at another main value. Charts that drew the old value draw the new one instead, so a card and
        /// "its" line never disagree; the colour moves along.
        /// </summary>
        public void ReplaceMain(TileConfig tile, string newRef)
        {
            string old = tile.Main;
            if (old == newRef || newRef == null) return;
            tile.Main = newRef;
            tile.Fallback = null;
            tile.LimitFromModules = false;
            if (old == null) return;
            foreach (var chart in Charts)
                foreach (var line in chart.Lines)
                    if (line.Ref == old && chart.Lines.All(l => l.Ref != newRef)) line.Ref = newRef;
            string color;
            if (Colors.TryGetValue(old, out color) && !Colors.ContainsKey(newRef)) Colors[newRef] = color;
        }

        /// <summary>The limit in effect for a card: the modules' own threshold when chosen and known, else the set value.</summary>
        public static double EffectiveLimit(TileConfig tile, float moduleLimit)
        {
            if (tile == null) return double.NaN;
            if (tile.LimitFromModules && !float.IsNaN(moduleLimit) && moduleLimit >= 30 && moduleLimit <= 125) return moduleLimit;
            return tile.Limit;
        }

        /// <summary>The preference for a fan, created on first use.</summary>
        public FanPref Fan(string controlId)
        {
            var f = Fans.FirstOrDefault(p => p.ControlId == controlId);
            if (f == null) { f = new FanPref { ControlId = controlId }; Fans.Add(f); }
            return f;
        }

        /// <summary>Back to the default layout; the user's fan names are kept.</summary>
        public void Reset()
        {
            var d = new Settings();
            RangeMinutes = d.RangeMinutes; ChartHeight = d.ChartHeight; ChartsStacked = d.ChartsStacked; ShowLimits = d.ShowLimits;
            Fahrenheit = d.Fahrenheit; Compact = d.Compact; Smoothing = d.Smoothing;
            Tiles = d.Tiles; Charts = d.Charts; Colors = d.Colors;
            Fans = Fans.Where(f => !string.IsNullOrEmpty(f.Name)).Select(f => new FanPref { ControlId = f.ControlId, Name = f.Name }).ToList();
            Sanitize();
        }

        public bool IsDefault
        {
            get
            {
                var r = FromJson(ToJson()); r.Sanitize(); r.Reset(); r.Collapsed = Collapsed;
                var me = FromJson(ToJson()); me.Sanitize();
                return r.ToJson() == me.ToJson();
            }
        }

        internal void Sanitize()
        {
            if (Array.IndexOf(Ranges, RangeMinutes) < 0) RangeMinutes = 10;
            if (ChartHeight < 120 || ChartHeight > 500) ChartHeight = 220;
            if (Array.IndexOf(SmoothingSteps, Smoothing) < 0) Smoothing = 0;
            if (Colors == null) Colors = new Dictionary<string, string>();
            foreach (var k in Colors.Where(kv => !Palette.IsChoice(kv.Value) || !Refs.IsValid(kv.Key)).Select(kv => kv.Key).ToList()) Colors.Remove(k);
            if (Fans == null) Fans = new List<FanPref>();
            Fans = Fans.Where(f => f != null && !string.IsNullOrEmpty(f.ControlId)).GroupBy(f => f.ControlId).Select(g => g.First()).ToList();

            if (Tiles == null) Tiles = new List<TileConfig>();
            Tiles = Tiles.Where(t => t != null && !string.IsNullOrEmpty(t.Id)).GroupBy(t => t.Id).Select(g => g.First()).Take(MaxTiles).ToList();
            foreach (var t in Tiles)
            {
                if (t.Kind != "fans") t.Kind = "sensor";
                if (string.IsNullOrWhiteSpace(t.Title)) t.Title = t.IsFans ? "Fans" : "Sensor";
                if (!Refs.IsValid(t.Main)) t.Main = null;
                if (!Refs.IsValid(t.Fallback)) t.Fallback = null;
                if (!Refs.IsValid(t.Bar)) t.Bar = null;
                if (t.Details == null) t.Details = new List<string>();
                t.Details = t.Details.Where(Refs.IsValid).Distinct().Take(MaxDetails).ToList();
                if (double.IsInfinity(t.Limit) || Math.Abs(t.Limit) > 1e7) t.Limit = double.NaN;
            }
            // a card without anything to show is useless: drop sensor cards that lost their main value
            Tiles.RemoveAll(t => !t.IsFans && t.Main == null);

            if (Charts == null) Charts = new List<ChartConfig>();
            Charts = Charts.Where(c => c != null && !string.IsNullOrEmpty(c.Id)).GroupBy(c => c.Id).Select(g => g.First()).Take(MaxCharts).ToList();
            foreach (var c in Charts)
            {
                if (string.IsNullOrWhiteSpace(c.Title)) c.Title = "Chart";
                if (c.Lines == null) c.Lines = new List<LineConfig>();
                c.Lines = c.Lines.Where(l => l != null && Refs.IsValid(l.Ref)).GroupBy(l => l.Ref).Select(g => g.First()).Take(MaxLines).ToList();
            }
        }

        // ---------------------------------------------------------------- JSON

        /// <summary>
        /// Reads the settings file. Unknown fields are ignored, a field with the wrong type keeps its default, and files of the
        /// first version (ShowCpu, CpuLimit, HiddenSeries, ...) are converted. Text that is not a JSON object throws.
        /// </summary>
        internal static Settings FromJson(string text)
        {
            var d = Json.Parse(text) as Dictionary<string, object>;
            if (d == null) throw new FormatException("settings.json does not contain a JSON object");
            var s = new Settings();
            bool b; double n;
            if (Bool(d, "Collapsed", out b)) s.Collapsed = b;
            if (Number(d, "RangeMinutes", out n)) s.RangeMinutes = (int)n;
            if (Number(d, "ChartHeight", out n)) s.ChartHeight = (int)n;
            if (Bool(d, "ShowLimits", out b)) s.ShowLimits = b;
            if (Bool(d, "ChartsStacked", out b)) s.ChartsStacked = b;
            if (Bool(d, "Fahrenheit", out b)) s.Fahrenheit = b;
            if (Bool(d, "Compact", out b)) s.Compact = b;
            if (Number(d, "Smoothing", out n)) s.Smoothing = (int)n;
            s.Colors = StringMap(d, "Colors");
            foreach (var o in Objects(d, "Fans"))
            {
                var f = new FanPref { ControlId = Str(o, "ControlId"), Name = Str(o, "Name") };
                if (Bool(o, "Hidden", out b)) f.Hidden = b;
                s.Fans.Add(f);
            }

            if (Number(d, "Version", out n) && n >= 2)
            {
                if (d.ContainsKey("Tiles"))
                {
                    s.Tiles = new List<TileConfig>();
                    foreach (var o in Objects(d, "Tiles"))
                    {
                        var t = new TileConfig
                        {
                            Id = Str(o, "Id"), Kind = Str(o, "Kind") ?? "sensor", Title = Str(o, "Title"), Main = Str(o, "Main"), Fallback = Str(o, "Fallback"),
                            Bar = Str(o, "Bar"), BarLabel = Str(o, "BarLabel"), Details = Strings(o, "Details").ToList(),
                        };
                        if (Number(o, "Limit", out n)) t.Limit = n;
                        if (Bool(o, "LimitFromModules", out b)) t.LimitFromModules = b;
                        if (Bool(o, "Hidden", out b)) t.Hidden = b;
                        s.Tiles.Add(t);
                    }
                }
                if (d.ContainsKey("Charts"))
                {
                    s.Charts = new List<ChartConfig>();
                    foreach (var o in Objects(d, "Charts"))
                    {
                        var c = new ChartConfig { Id = Str(o, "Id"), Title = Str(o, "Title") };
                        if (Bool(o, "Hidden", out b)) c.Hidden = b;
                        foreach (var l in Objects(o, "Lines"))
                        {
                            var line = new LineConfig { Ref = Str(l, "Ref") };
                            if (Bool(l, "Hidden", out b)) line.Hidden = b;
                            c.Lines.Add(line);
                        }
                        s.Charts.Add(c);
                    }
                }
                return s;
            }

            // ---- version 1: fixed CPU / GPU / RAM / Fans cards and two charts ----
            Action<string, string> hideTile = (key, id) => { if (Bool(d, key, out b) && !b) s.Tile(id).Hidden = true; };
            hideTile("ShowCpu", "cpu"); hideTile("ShowGpu", "gpu"); hideTile("ShowRam", "ram"); hideTile("ShowFans", "fans");
            if (Number(d, "CpuLimit", out n)) s.Tile("cpu").Limit = n;
            if (Number(d, "RamWarning", out n)) s.Tile("ram").Limit = n;
            if (Bool(d, "RamWarningAuto", out b)) s.Tile("ram").LimitFromModules = b;
            double layout = 0, single = 0;
            Number(d, "Layout", out layout); Number(d, "SingleMode", out single);
            if ((int)layout == 1) s.ChartsStacked = true;
            if ((int)layout == 2) s.Chart((int)single == 1 ? "temp" : "load").Hidden = true; // "one chart" showed only the other one
            if (Bool(d, "ShowCharts", out b) && !b) foreach (var c in s.Charts) c.Hidden = true;
            foreach (var h in Strings(d, "HiddenSeries"))
            {
                // "0:gpu.vram" = temperature chart, "1:cpu.load" = load chart
                int colon = h.IndexOf(':');
                if (colon < 1) continue;
                var chart = s.Chart(h.Substring(0, colon) == "0" ? "temp" : "load");
                var line = chart == null ? null : chart.Lines.FirstOrDefault(l => l.Ref == "auto:" + h.Substring(colon + 1));
                if (line != null) line.Hidden = true;
            }
            return s;
        }

        internal string ToJson()
        {
            var copy = new Dictionary<string, object>
            {
                { "Version", CurrentVersion }, { "Collapsed", Collapsed }, { "RangeMinutes", RangeMinutes }, { "ChartHeight", ChartHeight },
                { "ChartsStacked", ChartsStacked }, { "ShowLimits", ShowLimits }, { "Fahrenheit", Fahrenheit }, { "Compact", Compact },
                { "Smoothing", Smoothing },
                { "Tiles", Tiles.Select(t =>
                    {
                        var o = new Dictionary<string, object>
                        {
                            { "Id", t.Id }, { "Kind", t.Kind }, { "Title", t.Title }, { "Main", t.Main }, { "Fallback", t.Fallback },
                            { "Bar", t.Bar }, { "BarLabel", t.BarLabel }, { "Details", t.Details }, { "LimitFromModules", t.LimitFromModules }, { "Hidden", t.Hidden },
                        };
                        if (!double.IsNaN(t.Limit)) o["Limit"] = t.Limit;
                        return o;
                    }).ToList() },
                { "Charts", Charts.Select(c => new Dictionary<string, object>
                    {
                        { "Id", c.Id }, { "Title", c.Title }, { "Hidden", c.Hidden },
                        { "Lines", c.Lines.Select(l => new Dictionary<string, object> { { "Ref", l.Ref }, { "Hidden", l.Hidden } }).ToList() },
                    }).ToList() },
                { "Colors", new SortedDictionary<string, string>(Colors, StringComparer.Ordinal) },
                { "Fans", Fans.Select(f => new Dictionary<string, object> { { "ControlId", f.ControlId }, { "Name", f.Name }, { "Hidden", f.Hidden } }).ToList() },
            };
            return Json.Write(copy);
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
    }
}
