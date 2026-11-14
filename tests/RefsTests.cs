using System.Collections.Generic;
using System.Linq;
using FanControl.Dashboard;
using Xunit;

public sealed class RefsTests
{
    static readonly List<SensorInfo> Catalog = new List<SensorInfo>
    {
        new SensorInfo("/amdcpu/0/temperature/2", "Temperature", "Core (Tctl/Tdie)"),
        new SensorInfo("/lpc/nct6798d/fan/4", "Rpm", "Fan #5"),
        new SensorInfo("Mix/Water", "Temperature", "Water"),
        new SensorInfo("/nvme/0/temperature/0", "Temperature", "Composite Temperature"),
        new SensorInfo("ADLX/AMD Radeon RX 7800 XT/0/temp/GPU", "Temperature", "GPU"),
        new SensorInfo("/gpu-nvidia/0/load/0", "UsagePercent", "GPU Core"),
        new SensorInfo("/memory/dimm/1/temperature/0", "Temperature", "DIMM #1"),
        new SensorInfo("/ram/data/0", "Unsupported", "Memory Used"),
    };

    [Fact]
    public void Parsing_and_validity()
    {
        Assert.True(Refs.IsValid("auto:cpu.temp"));
        Assert.False(Refs.IsValid("auto:nope"));
        Assert.True(Refs.IsValid("fc:/x"));
        Assert.False(Refs.IsValid("fc:"));
        Assert.False(Refs.IsValid(null));
        Assert.Equal("cpu.temp", Refs.Key("auto:cpu.temp"));
        Assert.Equal("fc:/x", Refs.Key("fc:/x"));
        Assert.Equal("/x", Refs.SensorId("fc:/x"));
        Assert.Equal("fc:/x", Refs.Sensor("/x"));
    }

    [Fact]
    public void Names_and_units()
    {
        Assert.Equal("CPU temperature", Refs.Name("auto:cpu.temp", Catalog));
        Assert.Equal("Hotspot", Refs.Short("auto:gpu.hot", Catalog));
        Assert.Equal("°C", Refs.Unit("auto:ram.temp", Catalog));
        Assert.Equal("W", Refs.Unit("auto:cpu.power", Catalog));
        Assert.Equal("Water", Refs.Name("fc:Mix/Water", Catalog));
        Assert.Equal("rpm", Refs.Unit("fc:/lpc/nct6798d/fan/4", Catalog));
        Assert.Equal("", Refs.Unit("fc:/unknown", Catalog));
        Assert.Equal("/unknown", Refs.Name("fc:/unknown", Catalog));
    }

    [Fact]
    public void Hardware_groups_and_sources()
    {
        var g = Catalog.ToDictionary(s => s.Id, Refs.Group);
        Assert.Equal("CPU", g["/amdcpu/0/temperature/2"]);
        Assert.Equal("Motherboard", g["/lpc/nct6798d/fan/4"]);
        Assert.Equal("Fan Control", g["Mix/Water"]);
        Assert.Equal("Storage", g["/nvme/0/temperature/0"]);
        Assert.Equal("GPU", g["ADLX/AMD Radeon RX 7800 XT/0/temp/GPU"]);
        Assert.Equal("GPU", g["/gpu-nvidia/0/load/0"]);
        Assert.Equal("Memory", g["/memory/dimm/1/temperature/0"]);
        Assert.Equal("RX 7800 XT", Refs.Source(Catalog[4]));
        Assert.Equal("lpc nct6798d", Refs.Source(Catalog[1]));
        Assert.Equal("amdcpu", Refs.Source(Catalog[0]));
        Assert.Equal("Mix", Refs.Source(Catalog[2]));
        Assert.False(Refs.Usable(Catalog[7]));
        Assert.False(Refs.Usable(new SensorInfo("Dashboard/Dashboard/cpu-load", "Temperature", "CPU load %")));
    }

    [Fact]
    public void Default_colours_are_stable_and_valid()
    {
        Assert.Equal("blue", Palette.DefaultColor("auto:cpu.temp"));
        Assert.Equal("blue", Palette.DefaultColor("auto:cpu.load"));
        Assert.Equal("green", Palette.DefaultColor("auto:gpu.hot"));
        string a = Palette.DefaultColor("fc:Mix/Water");
        Assert.True(Palette.IsChoice(a));
        Assert.Equal(a, Palette.DefaultColor("fc:Mix/Water"));
    }

    [Theory]
    [InlineData("%", 0, 63, 0, 100)]
    [InlineData("%", 0, 140, 0, 150)]
    [InlineData("rpm", 400, 2950, 0, 4000)]
    [InlineData("W", 2, 37, 0, 40)]
    [InlineData("V", 1.02, 1.31, 0, 1.5)]
    public void Axis_ranges(string unit, double min, double max, double lo, double hi)
    {
        double l, h, step;
        ChartMath.Axis(unit, false, min, max, 220, out l, out h, out step);
        Assert.Equal(lo, l, 6);
        Assert.Equal(hi, h, 6);
        Assert.True(h >= max && l <= min);
        Assert.True((h - l) / step <= 8);
    }

    [Fact]
    public void Temperature_axis_keeps_its_familiar_range()
    {
        double l, h, step;
        ChartMath.Axis("°C", false, 38, 72, 220, out l, out h, out step);
        Assert.Equal(30, l); Assert.Equal(100, h);
        ChartMath.Axis("°C", false, double.NaN, double.NaN, 220, out l, out h, out step);
        Assert.Equal(30, l); Assert.Equal(100, h);
        ChartMath.Axis("°C", true, 100, 160, 220, out l, out h, out step);
        Assert.Equal(80, l); Assert.Equal(220, h);
    }

    [Theory]
    [InlineData(0.7, 1)]
    [InlineData(1.2, 2)]
    [InlineData(2.2, 2.5)]
    [InlineData(4, 5)]
    [InlineData(730, 1000)]
    public void Nice_steps(double raw, double nice) { Assert.Equal(nice, ChartMath.Nice(raw), 6); }
}
