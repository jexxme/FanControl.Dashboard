using System;
using System.Collections.Generic;
using System.Linq;

namespace FanControl.Dashboard
{
    /// <summary>One sensor as Fan Control reports it over IPC.</summary>
    sealed class SensorInfo
    {
        public string Id, Type, Name;
        public SensorInfo() { }
        public SensorInfo(string id, string type, string name) { Id = id ?? ""; Type = type ?? ""; Name = name ?? ""; }
    }

    /// <summary>What the dashboard found among Fan Control's sensors.</summary>
    sealed class SensorMap
    {
        /// <summary>Dashboard key ("cpu.temp", "gpu.hot", ...) to Fan Control sensor id.</summary>
        public readonly Dictionary<string, string> Ids = new Dictionary<string, string>();
        /// <summary>Dashboard key to the sensor's display name (shown as card detail, e.g. "Tctl/Tdie").</summary>
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        /// <summary>Memory module temperature sensors, one per module, ordered by id.</summary>
        public List<SensorInfo> Dimms = new List<SensorInfo>();
        /// <summary>The modules' own "high" thresholds (SPD hub), same order as <see cref="Dimms"/> where present.</summary>
        public List<SensorInfo> DimmLimits = new List<SensorInfo>();
        public string GpuName;
    }

    /// <summary>
    /// Picks the dashboard's sensors from Fan Control's sensor list. Pure function of the list, so it can be
    /// tested with sensor dumps from different hardware (AMD / Intel CPUs, AMD (ADLX or LibreHardwareMonitor) /
    /// NVIDIA / Intel GPUs, DDR4 / DDR5).
    /// </summary>
    static class SensorMatcher
    {
        /// <summary>Number of keys the matcher can fill; fewer found means re-discovery is worth retrying.</summary>
        public const int MaxKeys = 7;

        public static SensorMap Match(IList<SensorInfo> all)
        {
            var map = new SensorMap();
            if (all == null) return map;
            var temps = all.Where(s => s.Type == "Temperature" && !IsPluginOrCustom(s)).ToList();

            // CPU package temperature: AMD "Core (Tctl/Tdie)", Intel "CPU Package", fallbacks for other LHM CPU naming.
            var cpuTemp = temps.FirstOrDefault(s => IsCpu(s) && s.Name == "Core (Tctl/Tdie)")
                       ?? temps.FirstOrDefault(s => IsCpu(s) && Has(s.Name, "Package"))
                       ?? temps.FirstOrDefault(s => IsCpu(s) && Has(s.Name, "Tctl"))
                       ?? temps.FirstOrDefault(s => IsCpu(s) && (Has(s.Name, "Core Max") || Has(s.Name, "Core Average")));
            Put(map, "cpu.temp", cpuTemp, CpuDetail(cpuTemp));
            Put(map, "cpu.load", all.FirstOrDefault(s => s.Type == "UsagePercent" && IsCpu(s) && s.Name == "CPU Total"), null);
            Put(map, "cpu.power", all.FirstOrDefault(s => s.Type == "Power" && IsCpu(s) && (s.Name == "Package" || s.Name == "CPU Package")), null);

            // GPU: prefer AMD ADLX ids (they carry the GPU name), then LibreHardwareMonitor's /gpu-* ids.
            var gpuTemp = temps.FirstOrDefault(s => s.Id.StartsWith("ADLX/", StringComparison.Ordinal) && s.Id.EndsWith("/temp/GPU", StringComparison.Ordinal))
                       ?? temps.FirstOrDefault(s => IsGpu(s) && s.Name == "GPU Core");
            string gpuRoot = GpuRoot(gpuTemp);
            Func<SensorInfo, bool> sameGpu = s => gpuRoot == null || s.Id.StartsWith(gpuRoot, StringComparison.Ordinal);
            Put(map, "gpu.temp", gpuTemp, gpuTemp == null ? null : gpuTemp.Name == "GPU" ? "edge" : gpuTemp.Name == "GPU Core" ? "core" : gpuTemp.Name);
            Put(map, "gpu.hot", temps.FirstOrDefault(s => sameGpu(s) && s.Id.StartsWith("ADLX/", StringComparison.Ordinal) && s.Id.EndsWith("/temp/Hotspot", StringComparison.Ordinal))
                              ?? temps.FirstOrDefault(s => sameGpu(s) && IsGpu(s) && Has(s.Name, "Hot Spot")), null);
            Put(map, "gpu.vram", temps.FirstOrDefault(s => sameGpu(s) && s.Id.StartsWith("ADLX/", StringComparison.Ordinal) && s.Id.EndsWith("/temp/Memory", StringComparison.Ordinal))
                               ?? temps.FirstOrDefault(s => sameGpu(s) && IsGpu(s) && s.Name.StartsWith("GPU Memory", StringComparison.Ordinal)), null);
            if (gpuTemp != null && gpuTemp.Id.StartsWith("ADLX/", StringComparison.Ordinal))
            {
                var parts = gpuTemp.Id.Split('/');
                if (parts.Length > 1 && parts[1].Length > 0) map.GpuName = parts[1];
            }

            Put(map, "ram.load", all.FirstOrDefault(s => s.Type == "UsagePercent" && s.Id.StartsWith("/ram/", StringComparison.Ordinal)), null);

            // DDR5 SPD hub: /memory/dimm/N/temperature/0 is the module temperature, /temperature/3 its "high" threshold.
            map.Dimms = temps.Where(s => s.Id.StartsWith("/memory/dimm/", StringComparison.Ordinal) && s.Id.EndsWith("/temperature/0", StringComparison.Ordinal))
                             .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            map.DimmLimits = temps.Where(s => s.Id.StartsWith("/memory/dimm/", StringComparison.Ordinal) && s.Id.EndsWith("/temperature/3", StringComparison.Ordinal)
                                              && Has(s.Name, "High Limit") && !Has(s.Name, "Critical"))
                                  .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            return map;
        }

        /// <summary>Short label for the CPU card: "Tctl/Tdie", "Package", or the sensor name.</summary>
        public static string CpuDetail(SensorInfo s)
        {
            if (s == null) return null;
            if (Has(s.Name, "Tctl")) return "Tctl/Tdie";
            if (Has(s.Name, "Package")) return "Package";
            return s.Name;
        }

        static void Put(SensorMap map, string key, SensorInfo s, string name)
        {
            if (s == null) return;
            map.Ids[key] = s.Id;
            map.Names[key] = name ?? s.Name;
        }

        static bool IsCpu(SensorInfo s) { return s.Id.StartsWith("/amdcpu/", StringComparison.Ordinal) || s.Id.StartsWith("/intelcpu/", StringComparison.Ordinal) || s.Id.Contains("cpu/"); }
        static bool IsGpu(SensorInfo s) { return s.Id.Contains("/gpu-") || s.Id.StartsWith("ADLX/", StringComparison.Ordinal); }
        static bool IsPluginOrCustom(SensorInfo s) { return s.Id.StartsWith("Dashboard/", StringComparison.Ordinal); }
        static bool Has(string s, string part) { return s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0; }

        /// <summary>"/gpu-nvidia/0/" or "ADLX/&lt;name&gt;/&lt;id&gt;/" so hotspot and memory come from the same card.</summary>
        static string GpuRoot(SensorInfo s)
        {
            if (s == null) return null;
            var parts = s.Id.Split('/');
            if (s.Id.StartsWith("ADLX/", StringComparison.Ordinal)) return parts.Length > 3 ? string.Join("/", parts, 0, 3) + "/" : null;
            return parts.Length > 3 ? string.Join("/", parts, 0, 3) + "/" : null; // "", "gpu-nvidia", "0"
        }
    }
}
