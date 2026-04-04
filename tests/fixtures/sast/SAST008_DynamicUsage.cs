namespace Fixtures.Sast;

public class DynamicFixture
{
    public dynamic GetValue() => 42;

    public void Process(dynamic input)
    {
        var result = input.ToString();
    }
}
