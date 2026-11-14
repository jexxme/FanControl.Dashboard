using System.Collections.Generic;
using FanControl.Dashboard;
using Xunit;

/// <summary>Sensor lists modelled on what Fan Control (LibreHardwareMonitor + ADLX) reports on different hardware.</summary>
public sealed class SensorMatcherTests
{
    static SensorInfo T(string id, string name) { return new SensorInfo(id, "Temperature", name); }
    static SensorInfo U(string id, string name) { return new SensorInfo(id, "UsagePercent", name); }
    static SensorInfo P(string id, string name) { return new SensorInfo(id, "Power", name); }

    [Fact]
    public void Amd_cpu_amd_gpu_via_adlx_and_ddr5()
    {
        var all = new List<SensorInfo>
        {
            new SensorInfo("Dashboard/Dashboard/cpu-load", "Temperature", "CPU load %"), // our own plugin sensor must be ignored
            U("/amdcpu/0/load/0", "CPU Total"), U("/amdcpu/0/load/1", "CPU Core Max"),
            P("/amdcpu/0/power/0", "Package"), P("/amdcpu/0/power/1", "Core #1 (SMU)"),
            T("/amdcpu/0/temperature/9", "CCD1 (Tdie)"), T("/amdcpu/0/temperature/2", "Core (Tctl/Tdie)"),
            U("/ram/load/0", "Memory"), U("/vram/load/1", "Memory"),
            T("/memory/dimm/3/temperature/0", "DIMM #3"), T("/memory/dimm/3/temperature/3", "Thermal Sensor High Limit"),
            T("/memory/dimm/1/temperature/0", "DIMM #1"), T("/memory/dimm/1/temperature/3", "Thermal Sensor High Limit"),
            T("/memory/dimm/1/temperature/5", "Thermal Sensor Critical High Limit"),
            T("ADLX/AMD Radeon RX 7800 XT/768/temp/Hotspot", "Hotspot"), T("ADLX/AMD Radeon RX 7800 XT/768/temp/GPU", "GPU"),
            T("ADLX/AMD Radeon RX 7800 XT/768/temp/Memory", "Memory"),
            T("/lpc/it8688e/temperature/1", "Temperature #1"),
        };
        var m = SensorMatcher.Match(all);
        Assert.Equal("/amdcpu/0/temperature/2", m.Ids["cpu.temp"]);
        Assert.Equal("Tctl/Tdie", m.Names["cpu.temp"]);
        Assert.Equal("/amdcpu/0/load/0", m.Ids["cpu.load"]);
        Assert.Equal("/amdcpu/0/power/0", m.Ids["cpu.power"]);
        Assert.Equal("ADLX/AMD Radeon RX 7800 XT/768/temp/GPU", m.Ids["gpu.temp"]);
        Assert.Equal("edge", m.Names["gpu.temp"]);
        Assert.Equal("ADLX/AMD Radeon RX 7800 XT/768/temp/Hotspot", m.Ids["gpu.hot"]);
        Assert.Equal("ADLX/AMD Radeon RX 7800 XT/768/temp/Memory", m.Ids["gpu.vram"]);
        Assert.Equal("AMD Radeon RX 7800 XT", m.GpuName);
        Assert.Equal("/ram/load/0", m.Ids["ram.load"]);
        Assert.Equal(new[] { "/memory/dimm/1/temperature/0", "/memory/dimm/3/temperature/0" }, m.Dimms.ConvertAll(d => d.Id));
        Assert.Equal(new[] { "/memory/dimm/1/temperature/3", "/memory/dimm/3/temperature/3" }, m.DimmLimits.ConvertAll(d => d.Id));
        Assert.Equal(SensorMatcher.MaxKeys, m.Ids.Count);
    }

    [Fact]
    public void Intel_cpu_nvidia_gpu_and_ddr4()
    {
        var all = new List<SensorInfo>
        {
            U("/intelcpu/0/load/0", "CPU Total"),
            T("/intelcpu/0/temperature/0", "CPU Core #1"), T("/intelcpu/0/temperature/8", "CPU Package"),
            P("/intelcpu/0/power/0", "CPU Package"), P("/intelcpu/0/power/1", "CPU Cores"),
            T("/gpu-nvidia/0/temperature/0", "GPU Core"), T("/gpu-nvidia/0/temperature/2", "GPU Hot Spot"),
            T("/gpu-nvidia/0/temperature/3", "GPU Memory Junction"),
            U("/ram/load/0", "Memory"),
        };
        var m = SensorMatcher.Match(all);
        Assert.Equal("/intelcpu/0/temperature/8", m.Ids["cpu.temp"]);
        Assert.Equal("Package", m.Names["cpu.temp"]);
        Assert.Equal("/intelcpu/0/power/0", m.Ids["cpu.power"]);
        Assert.Equal("/gpu-nvidia/0/temperature/0", m.Ids["gpu.temp"]);
        Assert.Equal("core", m.Names["gpu.temp"]);
        Assert.Equal("/gpu-nvidia/0/temperature/2", m.Ids["gpu.hot"]);
        Assert.Equal("/gpu-nvidia/0/temperature/3", m.Ids["gpu.vram"]);
        Assert.Null(m.GpuName);
        Assert.Empty(m.Dimms);
        Assert.Empty(m.DimmLimits);
    }

    [Fact]
    public void Hotspot_and_memory_come_from_the_same_gpu_as_the_main_temperature()
    {
        var all = new List<SensorInfo>
        {
            T("/gpu-nvidia/1/temperature/2", "GPU Hot Spot"),        // second card listed first
            T("/gpu-nvidia/0/temperature/0", "GPU Core"),
            T("/gpu-nvidia/1/temperature/0", "GPU Core"),
            T("/gpu-nvidia/0/temperature/2", "GPU Hot Spot"),
        };
        var m = SensorMatcher.Match(all);
        Assert.Equal("/gpu-nvidia/0/temperature/0", m.Ids["gpu.temp"]);
        Assert.Equal("/gpu-nvidia/0/temperature/2", m.Ids["gpu.hot"]);
        Assert.False(m.Ids.ContainsKey("gpu.vram"));
    }

    [Fact]
    public void Nothing_found_gives_an_empty_map()
    {
        var m = SensorMatcher.Match(new List<SensorInfo> { T("/lpc/it8688e/temperature/0", "Temperature #1") });
        Assert.Empty(m.Ids);
        Assert.Empty(m.Dimms);
        Assert.Empty(SensorMatcher.Match(null).Ids);
    }

    [Theory]
    [InlineData("Core (Tctl/Tdie)", "Tctl/Tdie")]
    [InlineData("CPU Package", "Package")]
    [InlineData("Core Max", "Core Max")]
    public void Cpu_detail_label(string sensorName, string expected)
    {
        Assert.Equal(expected, SensorMatcher.CpuDetail(new SensorInfo("/amdcpu/0/temperature/0", "Temperature", sensorName)));
    }
}
