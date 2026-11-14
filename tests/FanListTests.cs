using System.Collections.Generic;
using FanControl.Dashboard;
using Xunit;

public sealed class FanListTests
{
    static Dictionary<string, object> Parse(string s) { return (Dictionary<string, object>)Json.Parse(s); }

    [Fact]
    public void Reads_enabled_controls_with_their_paired_sensor_and_curve()
    {
        var cfg = Parse(@"{ ""FanControl"": { ""Controls"": [
            { ""Enable"": true,  ""NickName"": ""CPU cooler"", ""Identifier"": ""/lpc/x/control/0"", ""PairedFanSensor"": { ""Identifier"": ""/lpc/x/fan/0"" }, ""SelectedFanCurve"": { ""Name"": ""CPU"" } },
            { ""Enable"": false, ""NickName"": ""Unused"",     ""Identifier"": ""/lpc/x/control/1"" },
            { ""Enable"": true,  ""NickName"": ""GPU"",        ""Identifier"": ""ADLX/gpu/control"", ""PairedFanSensor"": null, ""SelectedFanCurve"": null }
        ] } }");
        var fans = FanList.Parse(cfg);
        Assert.Equal(2, fans.Count);
        Assert.Equal("CPU cooler", fans[0].Name);
        Assert.Equal("/lpc/x/control/0", fans[0].ControlId);
        Assert.Equal("/lpc/x/fan/0", fans[0].RpmId);
        Assert.Equal("CPU", fans[0].Curve);
        Assert.Equal("GPU", fans[1].Name);
        Assert.Null(fans[1].RpmId);
        Assert.Null(fans[1].Curve);
    }

    [Fact]
    public void Unexpected_shapes_give_an_empty_list()
    {
        Assert.Empty(FanList.Parse(null));
        Assert.Empty(FanList.Parse(Parse("{}")));
        Assert.Empty(FanList.Parse(Parse(@"{ ""FanControl"": { ""Controls"": [ 1, ""x"", { ""Enable"": ""yes"" } ] } }")));
    }
}
