using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Tests;

public class DateParsingTests
{
    [Theory]
    [InlineData("15/03/2010", 2010, 3, 15)]
    [InlineData("15/3/2010", 2010, 3, 15)]
    [InlineData("5/3/2010", 2010, 3, 5)]
    [InlineData("15-03-2010", 2010, 3, 15)]
    [InlineData("15.03.2010", 2010, 3, 15)]
    [InlineData("15032010", 2010, 3, 15)]
    [InlineData(" 15/03/2010 ", 2010, 3, 15)]
    [InlineData("15/03/10", 2010, 3, 15)]
    [InlineData("150310", 2010, 3, 15)]
    [InlineData("15/03/98", 1998, 3, 15)]
    [InlineData("2010-03-15", 2010, 3, 15)]
    [InlineData("29/02/2012", 2012, 2, 29)]
    public void Typed_dates_are_understood(string text, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d), Parse.Date(text, currentYear: 2026));

    [Theory]
    [InlineData("")]
    [InlineData("31/02/2010")]
    [InlineData("29/02/2011")]
    [InlineData("15/13/2010")]
    [InlineData("00/03/2010")]
    [InlineData("15/03")]
    [InlineData("1503201")]
    [InlineData("abc")]
    [InlineData("15/03/201")]
    [InlineData("15/03/1850")]
    public void Invalid_dates_are_rejected(string text) => Assert.Null(Parse.Date(text, currentYear: 2026));

    [Theory]
    [InlineData("1", "1")]
    [InlineData("15", "15")]
    [InlineData("150", "15/0")]
    [InlineData("1503", "15/03")]
    [InlineData("15032010", "15/03/2010")]
    [InlineData("15/03", "15/03")]
    public void Digits_get_their_slashes(string typed, string shown) => Assert.Equal(shown, Parse.DateAsTyped(typed));

    [Fact]
    public void Formatting_round_trips() => Assert.Equal(new DateTime(2010, 3, 5), Parse.Date(Parse.Date(new DateTime(2010, 3, 5))));
}
