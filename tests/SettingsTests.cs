using System;
using System.IO;
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
        Assert.True(s.ShowCpu && s.ShowGpu && s.ShowRam && s.ShowFans && s.ShowCharts);
        Assert.False(s.Collapsed);
        Assert.Equal(ChartLayout.SideBySide, s.Layout);
        Assert.Equal(10, s.RangeMinutes);
        Assert.Equal(220, s.ChartHeight);
        Assert.Equal(Settings.DefaultCpuLimit, s.CpuLimit);
        Assert.Equal(Settings.DefaultRamWarning, s.RamWarning);
        Assert.True(s.RamWarningAuto);
        Assert.Empty(s.HiddenSeries);
        Assert.Equal(FilePath, s.FilePath);
        Assert.True(s.IsDefault);
        Assert.False(File.Exists(FilePath)); // loading never writes
    }

    [Fact]
    public void Save_then_load_round_trips_every_option()
    {
        var s = Settings.Load(FilePath, null);
        s.Collapsed = true; s.ShowGpu = false; s.ShowFans = false; s.Layout = ChartLayout.Single; s.SingleMode = 1;
        s.RangeMinutes = 30; s.ChartHeight = 300; s.ShowLimits = false; s.CpuLimit = 89; s.RamWarning = 60; s.RamWarningAuto = false;
        s.ToggleHidden(0, "gpu.vram");
        s.Save();

        var r = Settings.Load(FilePath, null);
        Assert.True(r.Collapsed);
        Assert.False(r.ShowGpu); Assert.False(r.ShowFans); Assert.True(r.ShowCpu);
        Assert.Equal(ChartLayout.Single, r.Layout);
        Assert.Equal(1, r.SingleMode);
        Assert.Equal(30, r.RangeMinutes);
        Assert.Equal(300, r.ChartHeight);
        Assert.False(r.ShowLimits);
        Assert.Equal(89, r.CpuLimit);
        Assert.Equal(60, r.RamWarning);
        Assert.False(r.RamWarningAuto);
        Assert.True(r.IsHidden(0, "gpu.vram"));
        Assert.False(r.IsHidden(1, "gpu.vram"));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Save_overwrites_an_existing_file()
    {
        var s = Settings.Load(FilePath, null);
        s.Save();
        s.RangeMinutes = 2;
        s.Save();
        Assert.Equal(2, Settings.Load(FilePath, null).RangeMinutes);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_logs()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        string logged = null;
        var s = Settings.Load(FilePath, m => logged = m);
        Assert.True(s.IsDefault);
        Assert.NotNull(logged);
        Assert.Contains("defaults", logged);
    }

    [Fact]
    public void Out_of_range_values_are_repaired_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"RangeMinutes\":7,\"ChartHeight\":9000,\"Layout\":9,\"SingleMode\":5,\"CpuLimit\":400,\"RamWarning\":1,\"HiddenSeries\":null}");
        var s = Settings.Load(FilePath, null);
        Assert.Equal(10, s.RangeMinutes);
        Assert.Equal(220, s.ChartHeight);
        Assert.Equal(ChartLayout.SideBySide, s.Layout);
        Assert.Equal(0, s.SingleMode);
        Assert.Equal(Settings.CpuLimitMax, s.CpuLimit);      // clamped, not reset
        Assert.Equal(Settings.RamWarningMin, s.RamWarning);
        Assert.NotNull(s.HiddenSeries);
    }

    [Fact]
    public void Older_files_without_new_fields_keep_their_values()
    {
        Directory.CreateDirectory(_dir);
        // written by the first version: no RamWarningAuto field
        File.WriteAllText(FilePath, "{\"Collapsed\":false,\"ShowCpu\":true,\"ShowGpu\":true,\"ShowRam\":true,\"ShowFans\":true,\"ShowCharts\":true,\"Layout\":1,\"SingleMode\":0,\"RangeMinutes\":5,\"ChartHeight\":160,\"ShowLimits\":true,\"CpuLimit\":89,\"RamWarning\":55,\"HiddenSeries\":[\"1:ram.load\"]}");
        var s = Settings.Load(FilePath, null);
        Assert.Equal(ChartLayout.Stacked, s.Layout);
        Assert.Equal(5, s.RangeMinutes);
        Assert.Equal(160, s.ChartHeight);
        Assert.Equal(89, s.CpuLimit);
        Assert.True(s.RamWarningAuto);
        Assert.True(s.IsHidden(1, "ram.load"));
    }

    [Fact]
    public void Reset_restores_defaults_but_keeps_the_file_path()
    {
        var s = Settings.Load(FilePath, null);
        s.ShowCpu = false; s.CpuLimit = 70; s.ToggleHidden(1, "cpu.load"); s.RamWarningAuto = false;
        Assert.False(s.IsDefault);
        s.Reset();
        Assert.True(s.IsDefault);
        Assert.Equal(FilePath, s.FilePath);
    }

    [Fact]
    public void ToggleHidden_adds_and_removes()
    {
        var s = new Settings();
        s.ToggleHidden(0, "cpu.temp");
        Assert.True(s.IsHidden(0, "cpu.temp"));
        s.ToggleHidden(0, "cpu.temp");
        Assert.False(s.IsHidden(0, "cpu.temp"));
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

    [Theory]
    [InlineData(true, 55f, 60, 55)]        // auto and reported: module value
    [InlineData(true, float.NaN, 60, 60)]  // auto but not reported: manual value
    [InlineData(false, 55f, 60, 60)]       // manual
    [InlineData(true, 125f, 60, 60)]       // implausible module value is ignored
    public void Effective_RAM_warning(bool auto, float moduleLimit, double manual, double expected)
    {
        var s = new Settings { RamWarningAuto = auto, RamWarning = manual };
        Assert.Equal(expected, s.EffectiveRamWarning(moduleLimit));
    }

    [Theory]
    [InlineData(20, Settings.CpuLimitMin)]
    [InlineData(200, Settings.CpuLimitMax)]
    [InlineData(88.6, 89)]
    [InlineData(double.NaN, Settings.DefaultCpuLimit)]
    public void Cpu_limit_is_clamped_and_rounded(double input, double expected)
    {
        Assert.Equal(expected, Settings.ClampCpuLimit(input));
    }
}
