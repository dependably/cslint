namespace Fixtures.Sast;

public class ConsoleOutputFixture
{
    public void ProcessData(string input)
    {
        Console.WriteLine($"Processing: {input}");
        Debug.WriteLine("debug output");
        var result = input.ToUpper();
        Console.WriteLine(result);
    }
}
