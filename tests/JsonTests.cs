using System;
using System.Collections.Generic;
using FanControl.Dashboard;
using Xunit;

public sealed class JsonTests
{
    [Fact]
    public void Parses_every_value_kind()
    {
        var o = (Dictionary<string, object>)Json.Parse(" {\"s\":\"a\\\"b\\\\c\\/\\n\\u00e9\\u003c\",\"i\":42,\"n\":-7,\"l\":12345678901,\"d\":1.5,\"e\":2e3,\"t\":true,\"f\":false,\"z\":null,\"a\":[1,\"x\",[],{}],\"o\":{}} ");
        Assert.Equal("a\"b\\c/\né<", o["s"]);
        Assert.Equal(42, o["i"]);
        Assert.Equal(-7, o["n"]);
        Assert.Equal(12345678901L, o["l"]);
        Assert.Equal(1.5, o["d"]);
        Assert.Equal(2000.0, o["e"]);
        Assert.Equal(true, o["t"]);
        Assert.Equal(false, o["f"]);
        Assert.Null(o["z"]);
        var a = (List<object>)o["a"];
        Assert.Equal(4, a.Count);
        Assert.Equal(1, a[0]);
        Assert.Equal("x", a[1]);
        Assert.Empty((List<object>)a[2]);
        Assert.Empty((Dictionary<string, object>)a[3]);
        Assert.Empty((Dictionary<string, object>)o["o"]);
    }

    [Fact]
    public void Accepts_a_byte_order_mark_and_whitespace()
    {
        Assert.Equal(1, Json.Parse("\uFEFF\r\n\t 1 \n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1 2]")]
    [InlineData("{\"a\" 1}")]
    [InlineData("\"open")]
    [InlineData("tru")]
    [InlineData("{} x")]
    [InlineData("\"\\q\"")]
    [InlineData("\"\\u12\"")]
    [InlineData("1.2.3")]
    public void Rejects_invalid_text(string text)
    {
        Assert.Throws<FormatException>(() => Json.Parse(text));
    }

    [Fact]
    public void Deep_nesting_is_rejected_instead_of_overflowing_the_stack()
    {
        Assert.Throws<FormatException>(() => Json.Parse(new string('[', 10000)));
    }

    [Fact]
    public void Writes_in_the_settings_file_format()
    {
        var d = new Dictionary<string, object>
        {
            { "b", true }, { "i", 220 }, { "d", 95.0 }, { "half", 89.5 }, { "e", ChartLayout.Stacked },
            { "list", new List<string> { "0:gpu.vram", "1:cpu.load" } }, { "none", null }, { "s", "<a & 'b'>\"\\\n\u0001" },
        };
        Assert.Equal("{\"b\":true,\"i\":220,\"d\":95,\"half\":89.5,\"e\":1,\"list\":[\"0:gpu.vram\",\"1:cpu.load\"],\"none\":null," +
                     "\"s\":\"\\u003ca \\u0026 \\u0027b\\u0027\\u003e\\\"\\\\\\n\\u0001\"}", Json.Write(d));
    }

    [Fact]
    public void Round_trips()
    {
        var text = "{\"a\":[1,2.25,\"x\",{\"b\":null}],\"c\":false}";
        Assert.Equal(text, Json.Write(Json.Parse(text)));
    }

#if NETFRAMEWORK
    // The settings file used to be written by System.Web.Extensions' JavaScriptSerializer: the new writer must produce the
    // same bytes, and the new reader must read what the old one wrote. (JavaScriptSerializer only exists in .NET Framework.)
    [Fact]
    public void Matches_JavaScriptSerializer_output()
    {
        var js = new System.Web.Script.Serialization.JavaScriptSerializer();
        var d = new Dictionary<string, object>
        {
            { "Collapsed", false }, { "Layout", 2 }, { "CpuLimit", 89.0 }, { "RamWarning", 55.5 },
            { "HiddenSeries", new List<string> { "0:gpu.vram", "odd <&'\"\\\t\u0007é>" } }, { "Nothing", null },
        };
        Assert.Equal(js.Serialize(d), Json.Write(d));
    }

    [Fact]
    public void Settings_written_by_the_old_serializer_load_unchanged()
    {
        // exactly what the first versions wrote (JavaScriptSerializer, original fields only)
        var oldFile = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object>
        {
            { "Collapsed", false }, { "ShowCpu", true }, { "ShowGpu", true }, { "ShowRam", true }, { "ShowFans", false },
            { "ShowCharts", true }, { "Layout", 2 }, { "SingleMode", 1 }, { "RangeMinutes", 30 }, { "ChartHeight", 300 },
            { "ShowLimits", true }, { "CpuLimit", 89.0 }, { "RamWarning", 60.0 }, { "HiddenSeries", new List<string> { "1:ram.load" } },
        });
        var s = Settings.FromJson(oldFile);
        s.Sanitize();
        Assert.False(s.ShowFans); Assert.Equal(ChartLayout.Single, s.Layout); Assert.Equal(1, s.SingleMode);
        Assert.Equal(30, s.RangeMinutes); Assert.Equal(300, s.ChartHeight); Assert.Equal(89, s.CpuLimit); Assert.Equal(60, s.RamWarning);
        Assert.True(s.IsHidden(1, "ram.load"));
        Assert.True(s.RamWarningAuto); // new options get their defaults
        Assert.Equal(new[] { "cpu", "gpu", "ram", "fans" }, s.CardOrder.ToArray());
        // and the start of the new file is still the old format, byte for byte
        Assert.StartsWith(oldFile.Substring(0, oldFile.IndexOf("\"HiddenSeries\"", StringComparison.Ordinal)), s.ToJson().Replace(",\"RamWarningAuto\":true", ""));
    }
#endif
}
