using System.Collections.Generic;
using System.Linq;
using FanControl.Dashboard;
using Xunit;

/// <summary>Pure helpers behind the cards and charts: fan order, smoothing, decimation, units, history growth.</summary>
public sealed class ViewLogicTests
{
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
        FanOrder.Move(s, fans, "a", -1); // already first
        FanOrder.Move(s, fans, "a", +2);
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
        Assert.Equal(7f, r[4]);
        Assert.Same(r, ChartMath.Smooth(r, 1));
    }

    [Fact]
    public void Decimation_keeps_extremes_and_gaps()
    {
        var data = Enumerable.Range(0, 1000).Select(i => (float)(i % 10)).ToArray();
        data[500] = 99; data[600] = -5;
        for (int i = 700; i < 720; i++) data[i] = float.NaN;
        var idx = ChartMath.Decimate(data, 50);
        Assert.True(idx.Count <= 105);
        Assert.Contains(500, idx);
        Assert.Contains(600, idx);
        Assert.Contains(-1, idx);
        var real = idx.Where(i => i >= 0).ToList();
        Assert.Equal(real.OrderBy(i => i), real);
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
            Assert.Equal("13.6 GB", Units.Format(13.62f, "GB"));
            Units.Fahrenheit = true;
            Assert.Equal("212 °F", Units.Format(100f, "°C"));
            Assert.Equal(100, Units.FromDisplay(212), 6);
            Assert.Equal(18, Units.DeltaToDisplay(10), 6);
            Assert.Equal("°F", Units.DisplayUnit("°C"));
        }
        finally { Units.Fahrenheit = false; }
        Assert.Equal("%", Units.ForType("Control"));
        Assert.Equal("rpm", Units.ForType("Rpm"));
        Assert.Equal("", Units.ForType("Something"));
    }

    [Fact]
    public void History_grows_new_series_on_demand()
    {
        var s = new Sampler(null);
        s.Feed(new Dictionary<string, float> { { "cpu.temp", 1 } }, null, null, null, null, null);
        s.Feed(new Dictionary<string, float> { { "cpu.temp", 2 }, { "fc:/x", 30 } }, null, null, null, null, null);
        var h = s.History("fc:/x", 3);
        Assert.True(float.IsNaN(h[0]) && float.IsNaN(h[1]));
        Assert.Equal(30f, h[2]);
        Assert.Equal(30f, s.Get("fc:/x"));
        Assert.Equal(30f, s.PeekValue("/x"));
    }
}
