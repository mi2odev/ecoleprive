namespace CentreSoutien.Domain.Calculations;

public static class Levels
{
    /// <summary>Sort key for Algerian levels: 1AP…5AP, 1AM…4AM, 1AS…3AS, then anything else.</summary>
    public static int Order(string level)
    {
        var cycle = level.EndsWith("AP") ? 0 : level.EndsWith("AM") ? 10 : level.EndsWith("AS") ? 20 : 30;
        return cycle + (level.Length > 0 && char.IsDigit(level[0]) ? level[0] - '0' : 9);
    }
}
