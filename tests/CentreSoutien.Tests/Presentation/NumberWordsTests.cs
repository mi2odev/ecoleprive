using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Tests.Presentation;

public class NumberWordsTests
{
    [Theory]
    [InlineData(0, "zéro")]
    [InlineData(1, "un")]
    [InlineData(17, "dix-sept")]
    [InlineData(21, "vingt et un")]
    [InlineData(71, "soixante et onze")]
    [InlineData(80, "quatre-vingts")]
    [InlineData(81, "quatre-vingt-un")]
    [InlineData(99, "quatre-vingt-dix-neuf")]
    [InlineData(200, "deux cents")]
    [InlineData(250, "deux cent cinquante")]
    [InlineData(1000, "mille")]
    [InlineData(1500, "mille cinq cents")]
    [InlineData(4500, "quatre mille cinq cents")]
    [InlineData(80000, "quatre-vingt mille")]
    [InlineData(200000, "deux cent mille")]
    [InlineData(1_250_000, "un million deux cent cinquante mille")]
    [InlineData(3_000_000, "trois millions")]
    public void Numbers_are_written_in_french(long n, string expected) => Assert.Equal(expected, NumberWords.French(n));

    [Fact]
    public void Amounts_name_the_currency()
    {
        Assert.Equal("Quatre mille cinq cents dinars algériens", NumberWords.Amount(4500));
        Assert.Equal("Un dinar algérien", NumberWords.Amount(1));
        Assert.Equal("Dix dinars algériens et cinquante centimes", NumberWords.Amount(10.5m));
    }
}
