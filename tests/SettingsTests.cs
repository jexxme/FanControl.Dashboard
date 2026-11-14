using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FanControl.Dashboard;
using Xunit;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "fcdash-tests-" + Guid.NewGuid().ToString("N"));
    string FilePath { get { return Path.Combine(_dir, "settings.json"); } }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Defaults_when_file_is_missing()
    {
        var s = Settings.Load(FilePath, null);
        Assert.Equal(new[] { "cpu", "gpu", "ram", "fans" }, s.Tiles.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { "temp", "load" }, s.Charts.Select(c => c.Id).ToArray());
        Assert.Equal("auto:cpu.temp", s.Tile("cpu").Main);
        Assert.Equal(Settings.DefaultCpuLimit, s.Tile("cpu").Limit);
        Assert.True(s.Tile("ram").LimitFromModules);
        Assert.Equal("auto:ram.used", s.Tile("ram").Fallback);
        Assert.True(s.Tile("fans").IsFans);
        Assert.Equal(10, s.RangeMinutes);
        Assert.Equal(220, s.ChartHeight);
        Assert.True(s.ShowLimits);
        Assert.False(s.Fahrenheit || s.Compact || s.ChartsStacked || s.Collapsed);
        Assert.True(s.IsDefault);
        Assert.False(File.Exists(FilePath)); // loading never writes
    }

    [Fact]
    public void Everything_round_trips()
    {
        var s = Settings.Load(FilePath, null);
        s.Collapsed = true; s.RangeMinutes = 60; s.ChartHeight = 300; s.ChartsStacked = true; s.ShowLimits = false;
        s.Fahrenheit = true; s.Compact = true; s.Smoothing = 10;
        s.Tile("gpu").Hidden = true;
        s.Tile("cpu").Limit = 89;
        s.Tiles.Add(new TileConfig { Id = "water", Title = "Water", Main = "fc:Mix/Water", Limit = 40, Details = { "fc:/lpc/x/fan/4" } });
        Settings.Move(s.Tiles, s.Tile("water"), 0);
        s.Charts.Add(new ChartConfig { Id = "rpm", Title = "Fans", Lines = { new LineConfig { Ref = "fc:/lpc/x/fan/4", Hidden = true } } });
        s.Colors["fc:Mix/Water"] = "teal";
        s.Fan("c1").Name = "Front"; s.Fan("c0").Hidden = true;
        s.Save();

        var r = Settings.Load(FilePath, null);
        Assert.Equal(s.ToJson(), r.ToJson());
        Assert.Equal(new[] { "water", "cpu", "gpu", "ram", "fans" }, r.Tiles.Select(t => t.Id).ToArray());
        var w = r.Tile("water");
        Assert.Equal("fc:Mix/Water", w.Main); Assert.Equal(40, w.Limit); Assert.Equal(new[] { "fc:/lpc/x/fan/4" }, w.Details.ToArray());
        Assert.True(double.IsNaN(r.Tile("gpu").Limit));
        Assert.True(r.Tile("gpu").Hidden);
        Assert.True(r.Chart("rpm").Lines.Single().Hidden);
        Assert.Equal("teal", r.Colors["fc:Mix/Water"]);
        Assert.Equal("Front", r.Fans.Single(f => f.ControlId == "c1").Name);
        Assert.True(r.Fahrenheit && r.Compact && r.ChartsStacked && r.Collapsed);
        Assert.Equal(10, r.Smoothing);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_logs()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        string logged = null;
        var s = Settings.Load(FilePath, m => logged = m);
        Assert.True(s.IsDefault);
        Assert.Contains("defaults", logged);
    }

    [Fact]
    public void Version_1_files_are_converted()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"Collapsed\":false,\"ShowCpu\":true,\"ShowGpu\":false,\"ShowRam\":true,\"ShowFans\":true,\"ShowCharts\":true,\"Layout\":1,\"SingleMode\":0," +
            "\"RangeMinutes\":5,\"ChartHeight\":160,\"ShowLimits\":true,\"CpuLimit\":89,\"RamWarning\":60,\"RamWarningAuto\":false,\"HiddenSeries\":[\"0:gpu.vram\",\"1:ram.load\"]}");
        var s = Settings.Load(FilePath, null);
        Assert.True(s.Tile("gpu").Hidden);
        Assert.False(s.Tile("cpu").Hidden);
        Assert.Equal(89, s.Tile("cpu").Limit);
        Assert.Equal(60, s.Tile("ram").Limit);
        Assert.False(s.Tile("ram").LimitFromModules);
        Assert.True(s.ChartsStacked);
        Assert.Equal(5, s.RangeMinutes);
        Assert.Equal(160, s.ChartHeight);
        Assert.True(s.Chart("temp").Lines.Single(l => l.Ref == "auto:gpu.vram").Hidden);
        Assert.True(s.Chart("load").Lines.Single(l => l.Ref == "auto:ram.load").Hidden);
        Assert.False(s.Chart("temp").Lines.Single(l => l.Ref == "auto:cpu.temp").Hidden);
    }

    [Theory]
    [InlineData(0, "load")] // "one chart" showing temperature: the load chart is hidden
    [InlineData(1, "temp")]
    public void Version_1_single_chart_layout(int singleMode, string hiddenChart)
    {
        var s = Settings.FromJson("{\"Layout\":2,\"SingleMode\":" + singleMode + "}");
        Assert.True(s.Chart(hiddenChart).Hidden);
        Assert.Single(s.Charts.Where(c => !c.Hidden));
    }

    [Fact]
    public void Version_1_without_charts()
    {
        var s = Settings.FromJson("{\"ShowCharts\":false}");
        Assert.All(s.Charts, c => Assert.True(c.Hidden));
    }

    [Fact]
    public void Sanitize_repairs_bad_input()
    {
        var s = Settings.FromJson(@"{ ""Version"": 2, ""RangeMinutes"": 7, ""ChartHeight"": 9000, ""Smoothing"": 4,
            ""Tiles"": [
                { ""Id"": ""a"", ""Main"": ""fc:/x"", ""Details"": [""nonsense"", ""auto:cpu.load"", ""auto:cpu.load"", ""auto:nope""], ""Bar"": ""weird"" },
                { ""Id"": ""a"", ""Main"": ""fc:/dup"" },
                { ""Id"": ""b"", ""Main"": ""not a ref"" },
                { ""Id"": ""f"", ""Kind"": ""fans"", ""Title"": """" },
                { ""Id"": """", ""Main"": ""fc:/x"" } ],
            ""Charts"": [ { ""Id"": ""c"", ""Lines"": [ { ""Ref"": ""fc:/a"" }, { ""Ref"": ""fc:/a"" }, { ""Ref"": ""bad"" } ] } ],
            ""Colors"": { ""fc:/a"": ""red"", ""fc:/b"": ""chartreuse"", ""junk"": ""red"" },
            ""Fans"": [ { ""ControlId"": ""c1"" }, { ""ControlId"": ""c1"", ""Hidden"": true }, { ""Name"": ""no id"" } ] }");
        s.Sanitize();
        Assert.Equal(10, s.RangeMinutes); Assert.Equal(220, s.ChartHeight); Assert.Equal(0, s.Smoothing);
        Assert.Equal(new[] { "a", "f" }, s.Tiles.Select(t => t.Id).ToArray()); // "b" lost its value, duplicates and blank ids dropped
        Assert.Equal(new[] { "auto:cpu.load" }, s.Tile("a").Details.ToArray());
        Assert.Null(s.Tile("a").Bar);
        Assert.Equal("Sensor", s.Tile("a").Title);
        Assert.Equal("Fans", s.Tile("f").Title);
        Assert.Equal(new[] { "fc:/a" }, s.Chart("c").Lines.Select(l => l.Ref).ToArray());
        Assert.Equal("Chart", s.Chart("c").Title);
        Assert.Equal(new[] { "fc:/a" }, s.Colors.Keys.ToArray());
        Assert.Single(s.Fans);
    }

    [Fact]
    public void Replacing_a_cards_value_updates_its_chart_line_and_colour()
    {
        var s = new Settings();
        s.Colors["auto:cpu.temp"] = "pink";
        s.ReplaceMain(s.Tile("cpu"), "fc:/amdcpu/0/temperature/3");
        Assert.Equal("fc:/amdcpu/0/temperature/3", s.Tile("cpu").Main);
        Assert.Contains(s.Chart("temp").Lines, l => l.Ref == "fc:/amdcpu/0/temperature/3");
        Assert.DoesNotContain(s.Chart("temp").Lines, l => l.Ref == "auto:cpu.temp");
        Assert.Equal("pink", s.Colors["fc:/amdcpu/0/temperature/3"]);

        // RAM: a chosen sensor replaces the module logic
        s.ReplaceMain(s.Tile("ram"), "fc:/lpc/x/temperature/2");
        Assert.Null(s.Tile("ram").Fallback);
        Assert.False(s.Tile("ram").LimitFromModules);
    }

    [Fact]
    public void Replacing_keeps_charts_free_of_duplicates()
    {
        var s = new Settings();
        s.Chart("temp").Lines.Add(new LineConfig { Ref = "fc:/x" });
        s.ReplaceMain(s.Tile("cpu"), "fc:/x");
        Assert.Single(s.Chart("temp").Lines, l => l.Ref == "fc:/x");
    }

    [Theory]
    [InlineData(true, 55f, 60, 55)]        // modules report: their value
    [InlineData(true, float.NaN, 60, 60)]  // nothing reported: the set value
    [InlineData(false, 55f, 60, 60)]       // not chosen
    [InlineData(true, 200f, 60, 60)]       // implausible report is ignored
    public void Effective_limit(bool fromModules, float reported, double set, double expected)
    {
        Assert.Equal(expected, Settings.EffectiveLimit(new TileConfig { Limit = set, LimitFromModules = fromModules }, reported));
    }

    [Fact]
    public void Move_clamps_and_ignores_unknown_items()
    {
        var list = new List<string> { "a", "b", "c" };
        Settings.Move(list, "a", 5);
        Assert.Equal(new[] { "b", "c", "a" }, list.ToArray());
        Settings.Move(list, "a", -3);
        Assert.Equal(new[] { "a", "b", "c" }, list.ToArray());
        Settings.Move(list, "zzz", 0);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Reset_brings_back_the_default_layout_but_keeps_fan_names()
    {
        var s = new Settings { Fahrenheit = true, Smoothing = 3 };
        s.Tiles.RemoveAt(0);
        s.Tiles.Add(new TileConfig { Id = "x", Main = "fc:/x" });
        s.Charts.Clear();
        s.Colors["auto:cpu.temp"] = "red";
        s.Fan("a").Name = "Front"; s.Fan("a").Hidden = true; s.Fan("b").Hidden = true;
        Assert.False(s.IsDefault);
        s.Reset();
        Assert.True(s.IsDefault);
        Assert.Equal(new[] { "cpu", "gpu", "ram", "fans" }, s.Tiles.Select(t => t.Id).ToArray());
        Assert.Equal(2, s.Charts.Count);
        Assert.Empty(s.Colors);
        Assert.False(s.Fahrenheit);
        Assert.Equal("Front", s.Fans.Single().Name);
        Assert.False(s.Fans.Single().Hidden);
    }

    [Fact]
    public void Save_raises_Changed_even_without_a_file()
    {
        var s = new Settings();
        int calls = 0;
        s.Changed += () => calls++;
        s.Save();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void New_ids_are_unique()
    {
        var s = new Settings();
        var ids = Enumerable.Range(0, 200).Select(i => { var id = s.NewId("t"); s.Tiles.Add(new TileConfig { Id = id, Main = "fc:/x" }); return id; }).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
