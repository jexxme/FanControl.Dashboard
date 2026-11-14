using System.Collections.Generic;
using System.Linq;
using FanControl.Dashboard;
using Xunit;

public sealed class CustomizationTests
{
    [Fact]
    public void New_options_round_trip()
    {
        var s = new Settings { Fahrenheit = true, Compact = true, Smoothing = 10, ShowCpuPower = false, ShowRamModules = false };
        s.Sensors["cpu.temp"] = "/amdcpu/0/temperature/3";
        s.Colors["cpu.temp"] = "pink";
        var water = s.AddCustom("Mix/Water", "Water", "°C");
        water.Limit = 40; water.Color = "teal";
        var pump = s.AddCustom("/lpc/x/fan/4", "Pump", "rpm");
        pump.ShowCard = false;
        s.MoveCard(water.CardId, -4);
        s.Fan("c1").Name = "Front";
        s.Fan("c0").Hidden = true;
        s.Sanitize();

        var r = Settings.FromJson(s.ToJson());
        r.Sanitize();
        Assert.Equal(s.ToJson(), r.ToJson());
        Assert.True(r.Fahrenheit); Assert.True(r.Compact); Assert.Equal(10, r.Smoothing);
        Assert.False(r.ShowCpuPower); Assert.False(r.ShowRamModules); Assert.True(r.ShowGpuDetails);
        Assert.Equal("/amdcpu/0/temperature/3", r.Sensors["cpu.temp"]);
        Assert.Equal("pink", r.Colors["cpu.temp"]);
        Assert.Equal(new[] { water.CardId, "cpu", "gpu", "ram", "fans", pump.CardId }, r.CardOrder.ToArray());
        var w = r.FindCustom(water.Key);
        Assert.Equal("Water", w.Name); Assert.Equal("°C", w.Unit); Assert.Equal(40, w.Limit); Assert.Equal(1, w.Chart); Assert.Equal("teal", w.Color);
        Assert.False(r.FindCustom(pump.Id).ShowCard);
        Assert.Equal(0, r.FindCustom(pump.Id).Chart); // rpm cannot go into a chart
        Assert.True(double.IsNaN(r.FindCustom(pump.Id).Limit));
        Assert.Equal("Front", r.Fans.Single(f => f.ControlId == "c1").Name);
        Assert.True(r.Fans.Single(f => f.ControlId == "c0").Hidden);
    }

    [Fact]
    public void Sanitize_repairs_bad_input()
    {
        var s = Settings.FromJson(@"{
            ""CardOrder"": [""fans"", ""bogus"", ""fans"", ""custom:dead""],
            ""Sensors"": { ""cpu.temp"": """", ""not.a.slot"": ""/x"", ""gpu.load"": ""/gpu/load"" },
            ""Colors"": { ""cpu.temp"": ""chartreuse"", ""gpu.temp"": ""red"" },
            ""Smoothing"": 7,
            ""Custom"": [ { ""Id"": ""a1"", ""SensorId"": ""/s"", ""Unit"": ""rpm"", ""Chart"": 1, ""Color"": ""nope"" }, { ""Id"": ""a1"", ""SensorId"": ""/dup"" }, { ""Id"": """", ""SensorId"": ""/x"" } ],
            ""Fans"": [ { ""ControlId"": ""c1"" }, { ""ControlId"": ""c1"", ""Hidden"": true }, { ""Name"": ""no id"" } ]
        }");
        s.Sanitize();
        Assert.Equal(new[] { "fans", "cpu", "gpu", "ram", "custom:a1" }, s.CardOrder.ToArray());
        Assert.Equal(new[] { "gpu.load" }, s.Sensors.Keys.ToArray());
        Assert.Equal(new[] { "gpu.temp" }, s.Colors.Keys.ToArray());
        Assert.Equal(0, s.Smoothing);
        var c = s.Custom.Single();
        Assert.Equal("Sensor", c.Name); Assert.Equal(0, c.Chart); Assert.Null(c.Color);
        Assert.Single(s.Fans);
    }

    [Fact]
    public void Removing_a_custom_sensor_cleans_up_after_it()
    {
        var s = new Settings();
        var c = s.AddCustom("/x", "X", "%");
        s.Colors[c.Key] = "red";
        s.ToggleHidden(1, c.Key);
        s.RemoveCustom(c.CardId);
        Assert.Empty(s.Custom);
        Assert.DoesNotContain(c.CardId, s.CardOrder);
        Assert.False(s.Colors.ContainsKey(c.Key));
        Assert.Empty(s.HiddenSeries);
    }

    [Fact]
    public void Custom_sensors_are_limited()
    {
        var s = new Settings();
        for (int i = 0; i < Settings.MaxCustom; i++) Assert.NotNull(s.AddCustom("/x" + i, "X", "°C"));
        Assert.Null(s.AddCustom("/too-many", "X", "°C"));
        Assert.Equal(Settings.MaxCustom, s.Custom.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Reset_keeps_what_the_user_created()
    {
        var s = new Settings { Fahrenheit = true, Smoothing = 3 };
        var c = s.AddCustom("/x", "Water", "°C");
        c.ShowCard = false; c.Color = "red";
        s.MoveCard(c.CardId, -2);
        s.Sensors["cpu.temp"] = "/y";
        s.Fan("a").Name = "Front"; s.Fan("a").Hidden = true;
        s.Fan("b").Hidden = true;
        Assert.False(s.IsDefault);
        s.Reset();
        Assert.False(s.Fahrenheit); Assert.Equal(0, s.Smoothing);
        Assert.Empty(s.Sensors);
        Assert.Equal(new[] { "cpu", "gpu", "ram", "fans", c.CardId }, s.CardOrder.ToArray());
        Assert.True(c.ShowCard); Assert.Null(c.Color); Assert.Equal("Water", c.Name);
        Assert.Equal("Front", s.Fans.Single().Name);
        Assert.False(s.Fans.Single().Hidden);
        Assert.True(s.IsDefault);
    }

    [Fact]
    public void Card_visibility_and_moves()
    {
        var s = new Settings();
        s.SetCardVisible("gpu", false);
        Assert.False(s.ShowGpu); Assert.False(s.IsCardVisible("gpu"));
        s.MoveCard("cpu", -1); // already first: no change
        s.MoveCard("fans", -1);
        Assert.Equal(new[] { "cpu", "gpu", "fans", "ram" }, s.CardOrder.ToArray());
        s.MoveCard("ram", +1); // already last
        Assert.Equal("ram", s.CardOrder.Last());
    }

    static List<FanInfo> Fans(params string[] ids) { return ids.Select(i => new FanInfo { ControlId = i, Name = "Fan " + i }).ToList(); }

    [Fact]
    public void Fan_order_names_and_new_fans()
    {
        var fans = Fans("a", "b", "c", "d");
        var prefs = new List<FanPref> { new FanPref { ControlId = "c", Name = "  Pump  " }, new FanPref { ControlId = "gone" }, new FanPref { ControlId = "a", Hidden = true } };
        var rows = FanOrder.Apply(fans, prefs);
        Assert.Equal(new[] { "c", "a", "b", "d" }, rows.Select(r => r.Fan.ControlId).ToArray());
        Assert.Equal("Pump", rows[0].Name);
        Assert.Equal("Fan a", rows[1].Name);
        Assert.True(rows[1].Hidden);
        Assert.False(rows[2].Hidden);
    }

    [Fact]
    public void Moving_a_fan_writes_the_whole_order()
    {
        var s = new Settings();
        var fans = Fans("a", "b", "c");
        FanOrder.Move(s, fans, "c", -1);
        Assert.Equal(new[] { "a", "c", "b" }, FanOrder.Apply(fans, s.Fans).Select(r => r.Fan.ControlId).ToArray());
        FanOrder.Move(s, fans, "a", -1); // first already
        FanOrder.Move(s, fans, "a", +2); // only one step at a time is used by the UI, but larger moves work too
        Assert.Equal(new[] { "c", "b", "a" }, FanOrder.Apply(fans, s.Fans).Select(r => r.Fan.ControlId).ToArray());
    }

    [Fact]
    public void Smoothing_is_a_trailing_average_that_keeps_gaps()
    {
        var r = ChartMath.Smooth(new[] { 1f, 3f, 5f, float.NaN, 7f }, 2);
        Assert.Equal(1f, r[0]);
        Assert.Equal(2f, r[1]);
        Assert.Equal(4f, r[2]);
        Assert.True(float.IsNaN(r[3]));
        Assert.Equal(7f, r[4]); // the window holds only NaN and 7
        Assert.Same(r, ChartMath.Smooth(r, 1));
    }

    [Fact]
    public void Decimation_keeps_extremes_and_gaps()
    {
        var data = Enumerable.Range(0, 1000).Select(i => (float)(i % 10)).ToArray();
        data[500] = 99; data[600] = -5;
        for (int i = 700; i < 720; i++) data[i] = float.NaN;
        var idx = ChartMath.Decimate(data, 50);
        Assert.True(idx.Count <= 100 + 5);
        Assert.Contains(500, idx);
        Assert.Contains(600, idx);
        Assert.Contains(-1, idx);
        var real = idx.Where(i => i >= 0).ToList();
        Assert.Equal(real.OrderBy(i => i), real); // in time order
        // short series are drawn completely
        Assert.Equal(new[] { 0, -1, 2 }, ChartMath.Decimate(new[] { 1f, float.NaN, 2f }, 50).ToArray());
    }

    [Fact]
    public void Units_and_fahrenheit()
    {
        try
        {
            Units.Fahrenheit = false;
            Assert.Equal("71 °C", Units.Format(71.4f, "°C"));
            Assert.Equal("–", Units.Format(float.NaN, "%"));
            Assert.Equal("1200 rpm", Units.Format(1200.2f, "rpm"));
            Assert.Equal("1.05 V", Units.Format(1.049f, "V"));
            Assert.Equal("5.5 W", Units.Format(5.49f, "W"));
            Units.Fahrenheit = true;
            Assert.Equal("212 °F", Units.Format(100f, "°C"));
            Assert.Equal(100, Units.FromDisplay(212), 6);
            Assert.Equal(18, Units.DeltaToDisplay(10), 6);
            Assert.Equal("°F", Units.DisplayUnit("°C"));
            Assert.Equal("%", Units.DisplayUnit("%"));
        }
        finally { Units.Fahrenheit = false; }
        Assert.Equal("°C", Units.ForType("Temperature"));
        Assert.Equal("%", Units.ForType("UsagePercent"));
        Assert.Equal("%", Units.ForType("Control"));
        Assert.Equal("rpm", Units.ForType("Rpm"));
        Assert.Equal("", Units.ForType("Something"));
    }

    [Fact]
    public void History_grows_new_series_on_demand()
    {
        var s = new Sampler(null);
        s.Feed(new Dictionary<string, float> { { "cpu.temp", 1 } }, null, null, null, null, null);
        s.Feed(new Dictionary<string, float> { { "cpu.temp", 2 }, { "custom:ab12", 30 } }, null, null, null, null, null);
        var h = s.History("custom:ab12", 3);
        Assert.True(float.IsNaN(h[0]) && float.IsNaN(h[1]));
        Assert.Equal(30f, h[2]);
        Assert.Equal(30f, s.Get("custom:ab12"));
    }
}
