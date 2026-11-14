using FanControl.Dashboard;
using Xunit;

public sealed class ColumnMathTests
{
    // the cards row: gap 16, min column 280
    [Theory]
    [InlineData(4, 1180, 4)]   // wide: one row
    [InlineData(4, 1000, 2)]   // room for 3, but 3 + 1 is avoided
    [InlineData(4, 600, 2)]
    [InlineData(4, 575, 1)]    // 2 * 280 + 16 = 576 does not fit
    [InlineData(4, 576, 2)]
    [InlineData(4, 300, 1)]
    [InlineData(3, 1000, 3)]   // three cards: 3 columns is even
    [InlineData(3, 700, 2)]    // 2 + 1 is fine (no smaller even split exists)
    [InlineData(2, 1180, 2)]   // never more columns than items
    [InlineData(1, 1180, 1)]
    [InlineData(0, 1180, 1)]
    [InlineData(6, 1800, 6)]
    [InlineData(6, 1500, 5)]   // the rule only steps down when one column less divides evenly (6 % 4 != 0)
    public void Card_columns(int items, double width, int expected)
    {
        Assert.Equal(expected, ColumnMath.Columns(items, width, 16, 280, false));
    }

    [Theory]
    [InlineData(2, 1180, 2)]
    [InlineData(2, 895, 1)]    // 2 * 440 + 16 = 896
    [InlineData(2, 896, 2)]
    public void Chart_columns(int items, double width, int expected)
    {
        Assert.Equal(expected, ColumnMath.Columns(items, width, 16, 440, false));
    }

    [Fact]
    public void Forced_single_column_and_degenerate_input()
    {
        Assert.Equal(1, ColumnMath.Columns(2, 2000, 16, 440, true));
        Assert.Equal(1, ColumnMath.Columns(4, double.NaN, 16, 280, false));
        Assert.Equal(1, ColumnMath.Columns(4, -50, 16, 280, false));
        Assert.Equal(4, ColumnMath.Columns(4, 100, 0, 0, false)); // min width 0 is treated as 1 px
    }

    [Fact]
    public void Column_width_shares_the_space_minus_gaps()
    {
        Assert.Equal(278, ColumnMath.ColumnWidth(1160, 16, 4));
        Assert.Equal(1000, ColumnMath.ColumnWidth(1000, 16, 1));
        Assert.Equal(0, ColumnMath.ColumnWidth(10, 16, 2));
    }
}
