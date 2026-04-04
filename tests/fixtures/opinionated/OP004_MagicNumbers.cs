namespace Fixtures.Opinionated;

public class MagicNumberFixture
{
    public int GetTimeout() => 86400;

    public double ConvertToFahrenheit(double celsius) => celsius * 1.8 + 32;

    public int GetMaxRetries() => 7;

    private const int MaxRetries = 7;
    public int GetMaxRetriesCorrect() => MaxRetries;
}
