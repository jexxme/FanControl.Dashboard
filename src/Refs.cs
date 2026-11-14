using System;
using System.Collections.Generic;
using System.Linq;

namespace FanControl.Dashboard
{
    /// <summary>
    /// A value the dashboard can show: "auto:&lt;key&gt;" is found automatically (e.g. auto:cpu.temp, the CPU package
    /// temperature on any CPU), "fc:&lt;id&gt;" is one particular Fan Control sensor. The sampler records both under the ref's key.
    /// </summary>
    static class Refs
    {
        sealed class AutoValue
        {
            public string Key, Name, Short, Unit, Group;
            public AutoValue(string key, string name, string shortName, string unit, string group) { Key = key; Name = name; Short = shortName; Unit = unit; Group = group; }
        }

        static readonly AutoValue[] Autos =
        {
            new AutoValue("cpu.temp", "CPU temperature", "CPU", "°C", "CPU"),
            new AutoValue("cpu.load", "CPU load", "Load", "%", "CPU"),
            new AutoValue("cpu.power", "CPU package power", "Power", "W", "CPU"),
            new AutoValue("gpu.temp", "GPU temperature", "GPU", "°C", "GPU"),
            new AutoValue("gpu.hot", "GPU hotspot", "Hotspot", "°C", "GPU"),
            new AutoValue("gpu.vram", "GPU memory temperature", "VRAM", "°C", "GPU"),
            new AutoValue("gpu.load", "GPU load (like Task Manager)", "Load", "%", "GPU"),
            new AutoValue("ram.temp", "RAM temperature (hottest module)", "RAM", "°C", "Memory"),
            new AutoValue("ram.load", "RAM in use", "Used", "%", "Memory"),
            new AutoValue("ram.used", "RAM in use (GB)", "Used", "GB", "Memory"),
            new AutoValue("ram.modules", "Each memory module's temperature", "Modules", "°C", "Memory"),
        };

        public static IEnumerable<string> AutoRefs { get { return Autos.Select(a => "auto:" + a.Key); } }

        public static bool IsAuto(string r) { return r != null && r.StartsWith("auto:", StringComparison.Ordinal); }
        public static bool IsSensor(string r) { return r != null && r.StartsWith("fc:", StringComparison.Ordinal) && r.Length > 3; }
        public static bool IsValid(string r) { return IsSensor(r) || (IsAuto(r) && Autos.Any(a => "auto:" + a.Key == r)); }
        public static string Sensor(string id) { return "fc:" + id; }
        public static string SensorId(string r) { return IsSensor(r) ? r.Substring(3) : null; }

        /// <summary>The sampler key the value is recorded under.</summary>
        public static string Key(string r) { return IsAuto(r) ? r.Substring(5) : r; }

        static AutoValue Auto(string r) { return Autos.FirstOrDefault(a => "auto:" + a.Key == r); }
        static SensorInfo Find(string r, IList<SensorInfo> catalog) { var id = SensorId(r); return id == null || catalog == null ? null : catalog.FirstOrDefault(c => c.Id == id); }

        public static string Unit(string r, IList<SensorInfo> catalog)
        {
            var a = Auto(r);
            if (a != null) return a.Unit;
            var s = Find(r, catalog);
            return s == null ? "" : Units.ForType(s.Type);
        }

        /// <summary>Long name for lists and editors: "CPU temperature", "Water".</summary>
        public static string Name(string r, IList<SensorInfo> catalog)
        {
            var a = Auto(r);
            if (a != null) return a.Name;
            var s = Find(r, catalog);
            return s != null ? s.Name : SensorId(r) ?? "";
        }

        /// <summary>Short label for details, legends and chart labels: "Hotspot", "Water".</summary>
        public static string Short(string r, IList<SensorInfo> catalog)
        {
            var a = Auto(r);
            return a != null ? a.Short : Name(r, catalog);
        }

        public static string Group(string r, IList<SensorInfo> catalog)
        {
            var a = Auto(r);
            if (a != null) return a.Group;
            var s = Find(r, catalog);
            return s == null ? "Other" : Group(s);
        }

        public static readonly string[] Groups = { "CPU", "GPU", "Memory", "Motherboard", "Storage", "Fan Control", "Other" };

        /// <summary>Which hardware a sensor belongs to, from LibreHardwareMonitor / ADLX / Fan Control identifiers.</summary>
        public static string Group(SensorInfo s)
        {
            string id = s.Id ?? "";
            if (id.StartsWith("/amdcpu", StringComparison.Ordinal) || id.StartsWith("/intelcpu", StringComparison.Ordinal) || id.Contains("cpu/")) return "CPU";
            if (id.StartsWith("/gpu-", StringComparison.Ordinal) || id.StartsWith("ADLX/", StringComparison.Ordinal) || id.StartsWith("NvAPI", StringComparison.Ordinal)) return "GPU";
            if (id.StartsWith("/ram", StringComparison.Ordinal) || id.StartsWith("/vram", StringComparison.Ordinal) || id.StartsWith("/memory", StringComparison.Ordinal)) return "Memory";
            if (id.StartsWith("/lpc", StringComparison.Ordinal) || id.StartsWith("/mainboard", StringComparison.Ordinal) || id.StartsWith("/ec", StringComparison.Ordinal) || id.StartsWith("/smbus", StringComparison.Ordinal)) return "Motherboard";
            if (id.StartsWith("/nvme", StringComparison.Ordinal) || id.StartsWith("/hdd", StringComparison.Ordinal) || id.StartsWith("/ssd", StringComparison.Ordinal) || id.StartsWith("/storage", StringComparison.Ordinal)) return "Storage";
            if (!id.StartsWith("/", StringComparison.Ordinal)) return "Fan Control"; // mix, custom, time-average and plugin sensors
            return "Other";
        }

        /// <summary>Where a sensor comes from, for telling similar names apart: "lpc it8688e", "RX 7800 XT", "Mix".</summary>
        public static string Source(SensorInfo s)
        {
            var parts = (s.Id ?? "").Trim('/').Split('/');
            if (s.Id.StartsWith("ADLX/", StringComparison.Ordinal)) return parts.Length > 1 ? parts[1].Replace("AMD Radeon ", "") : "ADLX";
            if (s.Id.StartsWith("/", StringComparison.Ordinal)) return parts.Length > 1 && parts[1] != "0" ? parts[0] + " " + parts[1] : parts[0];
            return parts[0];
        }

        /// <summary>Sensor types the dashboard can show (Fan Control also lists unusable "Unsupported" values).</summary>
        public static bool Usable(SensorInfo s) { return s.Type != "Unsupported" && !s.Id.StartsWith("Dashboard/", StringComparison.Ordinal); }
    }
}
