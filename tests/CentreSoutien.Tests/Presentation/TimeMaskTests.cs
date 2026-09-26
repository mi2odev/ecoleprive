using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Tests.Presentation;

public class TimeMaskTests
{
    [Theory]
    [InlineData("", 0, 0, "1", "1")]
    [InlineData("1", 1, 0, "4", "14:")]      // two digits → ":" added
    [InlineData("14:", 3, 0, "3", "14:3")]
    [InlineData("14:3", 4, 0, "0", "14:30")]
    [InlineData("14:30", 5, 0, "5", "14:30")] // full: extra digits ignored
    [InlineData("", 0, 0, "9", "09:")]        // no hour starts with 9 → 09:
    [InlineData("12:00", 0, 5, "0", "0")]     // whole time selected: typing replaces it
    [InlineData("12:00", 0, 5, "08", "08:")]
    [InlineData("", 0, 0, "1430", "14:30")]   // pasted
    [InlineData("", 0, 0, "9h30", "09:30")]
    [InlineData("12", 2, 0, "x", "12:")]
    public void Colon_is_added_and_non_digits_are_dropped(string current, int start, int length, string input, string expected) =>
        Assert.Equal(expected, Parse.TimeMask(current, start, length, input));

    [Fact]
    public void Partial_times_still_read_as_times()
    {
        Assert.Equal(new TimeSpan(14, 0, 0), Parse.Time("14:"));
        Assert.Equal(new TimeSpan(9, 0, 0), Parse.Time("09:"));
    }
}
