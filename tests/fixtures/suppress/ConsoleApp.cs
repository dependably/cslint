public class ConsoleApp
{
    // Would normally trip SAST002, but the local .editorconfig sets it to severity=none.
    public void Run()
    {
        System.Console.WriteLine("hello");
    }
}
