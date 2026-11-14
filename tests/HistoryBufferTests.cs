using System.Collections.Generic;
using System.Linq;
using FanControl.Dashboard;
using Xunit;

public sealed class HistoryBufferTests
{
    static HistoryBuffer Buffer(int capacity) { return new HistoryBuffer(new[] { "a", "b" }, capacity); }
    static Dictionary<string, float> A(float v) { return new Dictionary<string, float> { { "a", v } }; }

    [Fact]
    public void Empty_buffer_reads_all_NaN()
    {
        var h = Buffer(5);
        Assert.Equal(0, h.Count);
        Assert.All(h.Read("a", 5), v => Assert.True(float.IsNaN(v)));
    }

    [Fact]
    public void Read_returns_oldest_first_and_pads_the_front_with_NaN()
    {
        var h = Buffer(5);
        h.Push(A(1)); h.Push(A(2)); h.Push(A(3));
        var r = h.Read("a", 5);
        Assert.True(float.IsNaN(r[0])); Assert.True(float.IsNaN(r[1]));
        Assert.Equal(new[] { 1f, 2f, 3f }, r.Skip(2));
        Assert.Equal(3, h.Count);
    }

    [Fact]
    public void Wraps_around_and_keeps_the_newest_samples()
    {
        var h = Buffer(4);
        for (int i = 1; i <= 10; i++) h.Push(A(i));
        Assert.Equal(new[] { 7f, 8f, 9f, 10f }, h.Read("a", 4));
        Assert.Equal(new[] { 9f, 10f }, h.Read("a", 2));
        Assert.Equal(4, h.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(99, 4)]
    public void Point_count_is_clamped_to_1_and_the_capacity(int asked, int expected)
    {
        var h = Buffer(4);
        h.Push(A(1));
        Assert.Equal(expected, h.Read("a", asked).Length);
    }

    [Fact]
    public void Missing_values_and_unknown_keys_read_as_NaN()
    {
        var h = Buffer(3);
        h.Push(A(1));                // "b" not in this sample
        h.Push(null);                // nothing at all
        Assert.True(float.IsNaN(h.Read("b", 1)[0]));
        Assert.True(float.IsNaN(h.Read("a", 1)[0]));
        Assert.Equal(1f, h.Read("a", 2)[0]);
        Assert.All(h.Read("nope", 3), v => Assert.True(float.IsNaN(v)));
        Assert.All(h.Read(null, 3), v => Assert.True(float.IsNaN(v)));
    }

    [Fact]
    public void Sampler_history_uses_the_buffer_through_Feed()
    {
        var s = new Sampler(null);
        for (int i = 0; i < Sampler.Capacity + 5; i++)
            s.Feed(new Dictionary<string, float> { { "cpu.temp", i } }, null, null, null, null, null);
        var r = s.History("cpu.temp", 3);
        Assert.Equal(new float[] { Sampler.Capacity + 2, Sampler.Capacity + 3, Sampler.Capacity + 4 }, r);
        Assert.Equal(Sampler.Capacity + 4, s.Get("cpu.temp"));
        Assert.True(float.IsNaN(s.Get("gpu.temp")));
        Assert.True(s.Discovered);
    }

    [Fact]
    public void Saved_history_comes_back_after_a_restart_with_a_gap()
    {
        var h = new HistoryBuffer(new[] { "a" }, 10);
        h.Ensure("fc:/x"); // a sensor added at runtime
        h.Push(A(1)); h.Push(A(2)); h.Push(new Dictionary<string, float> { { "a", 3 }, { "fc:/x", 7 } });
        var saved = new System.DateTime(2026, 11, 14, 12, 0, 0, System.DateTimeKind.Utc);
        var ms = new System.IO.MemoryStream();
        h.Save(ms, saved);

        var r = new HistoryBuffer(new[] { "a" }, 10);
        ms.Position = 0;
        Assert.True(r.Load(ms, saved.AddSeconds(2)));      // two seconds without the dashboard
        var a = r.Read("a", 5);
        Assert.Equal(new[] { 1f, 2f, 3f }, a.Take(3));
        Assert.True(float.IsNaN(a[3]) && float.IsNaN(a[4]));
        Assert.Equal(7f, r.Read("fc:/x", 5)[2]);             // series that were added at runtime come back too
    }

    [Fact]
    public void Old_or_damaged_history_is_ignored()
    {
        var h = new HistoryBuffer(new[] { "a" }, 10);
        h.Push(A(1));
        var saved = new System.DateTime(2026, 11, 14, 12, 0, 0, System.DateTimeKind.Utc);
        var ms = new System.IO.MemoryStream();
        h.Save(ms, saved);
        var bytes = ms.ToArray();

        var r = new HistoryBuffer(new[] { "a" }, 10);
        Assert.False(r.Load(new System.IO.MemoryStream(bytes), saved.AddSeconds(10)));   // older than the buffer
        Assert.False(r.Load(new System.IO.MemoryStream(bytes, 0, bytes.Length - 2), saved.AddSeconds(1))); // cut off
        Assert.False(r.Load(new System.IO.MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), saved));    // not ours
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Sampler_saves_and_restores_its_history_file()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fcdash-history-" + System.Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var s = new Sampler(null) { HistoryPath = path };
            s.Feed(new Dictionary<string, float> { { "cpu.temp", 50 } }, null, null, null, null, null);
            s.SaveHistory();
            var t = new Sampler(null) { HistoryPath = path };
            t.LoadHistory();
            Assert.Contains(50f, t.History("cpu.temp", 5));
        }
        finally { try { System.IO.File.Delete(path); } catch { } }
    }
}
