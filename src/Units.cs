using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FanControl.Dashboard
{
    /// <summary>Units of Fan Control sensor types and the °C / °F display setting. Values are stored and compared in °C.</summary>
    static class Units
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Set by the view from <see cref="Settings.Fahrenheit"/>.</summary>
        public static bool Fahrenheit;

        public static string Temp { get { return Fahrenheit ? "°F" : "°C"; } }

        /// <summary>A temperature in the display unit.</summary>
        public static double ToDisplay(double celsius) { return Fahrenheit ? celsius * 9 / 5 + 32 : celsius; }
        public static float ToDisplay(float celsius) { return Fahrenheit ? celsius * 9f / 5f + 32f : celsius; }

        /// <summary>A temperature typed in the display unit, back to °C.</summary>
        public static double FromDisplay(double value) { return Fahrenheit ? (value - 32) * 5 / 9 : value; }

        /// <summary>A temperature difference in the display unit.</summary>
        public static double DeltaToDisplay(double celsius) { return Fahrenheit ? celsius * 9 / 5 : celsius; }

        /// <summary>Unit for a Fan Control sensor type as the IPC reports it.</summary>
        public static string ForType(string type)
        {
            switch (type)
            {
                case "Temperature": return "°C";
                case "UsagePercent": case "Control": case "Level": return "%";
                case "Power": return "W";
                case "Rpm": return "rpm";
                case "Frequency": return "MHz";
                case "Voltage": return "V";
                case "Current": return "A";
                case "Flow": return "L/h";
                default: return "";
            }
        }

        /// <summary>"71 °C" (or °F), "53 %", "1200 rpm", "1.05 V"; "–" when there is no value.</summary>
        public static string Format(float value, string unit)
        {
            if (float.IsNaN(value)) return "–";
            if (unit == "°C") return ToDisplay(value).ToString("0", Inv) + " " + Temp;
            string f = unit == "V" || unit == "A" ? "0.00" : unit == "W" && Math.Abs(value) < 10 ? "0.0" : "0";
            return value.ToString(f, Inv) + (unit.Length > 0 ? " " + unit : "");
        }

        /// <summary>The number alone, in the display unit (big card value).</summary>
        public static string Number(float value, string unit)
        {
            if (float.IsNaN(value)) return "–";
            if (unit == "°C") return ToDisplay(value).ToString("0", Inv);
            return value.ToString(unit == "V" || unit == "A" ? "0.00" : "0", Inv);
        }

        public static string DisplayUnit(string unit) { return unit == "°C" ? Temp : unit; }
    }

    /// <summary>Chart line preparation: smoothing and reducing many samples to a few per pixel.</summary>
    static class ChartMath
    {
        /// <summary>Trailing moving average over <paramref name="window"/> samples; NaN gaps stay gaps.</summary>
        public static float[] Smooth(float[] data, int window)
        {
            if (data == null || window <= 1) return data;
            var r = new float[data.Length];
            double sum = 0; int n = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (!float.IsNaN(data[i])) { sum += data[i]; n++; }
                int drop = i - window;
                if (drop >= 0 && !float.IsNaN(data[drop])) { sum -= data[drop]; n--; }
                r[i] = float.IsNaN(data[i]) || n == 0 ? float.NaN : (float)(sum / n);
            }
            return r;
        }

        /// <summary>
        /// Indices to draw so that every bucket of samples keeps its minimum and maximum (in time order): the line keeps
        /// its spikes, but a 3600-sample hour needs only about two points per pixel. -1 marks a gap.
        /// </summary>
        public static List<int> Decimate(float[] data, int buckets)
        {
            var result = new List<int>();
            if (data == null || data.Length == 0) return result;
            if (buckets <= 0 || data.Length <= buckets * 2)
            {
                for (int i = 0; i < data.Length; i++) result.Add(float.IsNaN(data[i]) ? -1 : i);
                return result;
            }
            for (int b = 0; b < buckets; b++)
            {
                int from = (int)((long)b * data.Length / buckets), to = (int)((long)(b + 1) * data.Length / buckets);
                int lo = -1, hi = -1;
                for (int i = from; i < to; i++)
                {
                    if (float.IsNaN(data[i])) continue;
                    if (lo < 0 || data[i] < data[lo]) lo = i;
                    if (hi < 0 || data[i] > data[hi]) hi = i;
                }
                if (lo < 0) { if (result.Count == 0 || result[result.Count - 1] != -1) result.Add(-1); continue; }
                if (lo == hi) result.Add(lo);
                else { result.Add(Math.Min(lo, hi)); result.Add(Math.Max(lo, hi)); }
            }
            return result;
        }
    }

    /// <summary>One row of the Fans card after the user's order, names and visibility are applied.</summary>
    sealed class FanRow
    {
        public FanInfo Fan;
        public string Name;
        public bool Hidden;
    }

    static class FanOrder
    {
        /// <summary>The user's order first (for fans that still exist), then new fans in Fan Control's order.</summary>
        public static List<FanRow> Apply(IList<FanInfo> fans, IList<FanPref> prefs)
        {
            var rows = new List<FanRow>();
            var byId = new Dictionary<string, FanPref>();
            foreach (var p in prefs ?? new List<FanPref>()) if (p.ControlId != null && !byId.ContainsKey(p.ControlId)) byId[p.ControlId] = p;
            var ordered = fans.Select((f, i) => new
            {
                Fan = f,
                Rank = f.ControlId != null && byId.ContainsKey(f.ControlId) ? IndexOf(prefs, f.ControlId) : 100000 + i,
            }).OrderBy(x => x.Rank);
            foreach (var x in ordered)
            {
                FanPref p; byId.TryGetValue(x.Fan.ControlId ?? "", out p);
                string original = string.IsNullOrEmpty(x.Fan.Name) ? x.Fan.ControlId : x.Fan.Name;
                rows.Add(new FanRow { Fan = x.Fan, Name = p != null && !string.IsNullOrWhiteSpace(p.Name) ? p.Name.Trim() : original, Hidden = p != null && p.Hidden });
            }
            return rows;
        }

        static int IndexOf(IList<FanPref> prefs, string id)
        {
            for (int i = 0; i < prefs.Count; i++) if (prefs[i].ControlId == id) return i;
            return int.MaxValue;
        }

        /// <summary>Moves a fan one place in the user's order, writing the full current order into the preferences.</summary>
        public static void Move(Settings settings, IList<FanInfo> fans, string controlId, int delta)
        {
            var order = Apply(fans, settings.Fans).Select(r => r.Fan.ControlId).Where(id => id != null).ToList();
            int i = order.IndexOf(controlId), j = i + delta;
            if (i < 0 || j < 0 || j >= order.Count) return;
            order.RemoveAt(i); order.Insert(j, controlId);
            var prefs = order.Select(id => settings.Fan(id)).ToList();
            settings.Fans = prefs.Concat(settings.Fans.Where(p => !order.Contains(p.ControlId))).ToList();
        }
    }
}
