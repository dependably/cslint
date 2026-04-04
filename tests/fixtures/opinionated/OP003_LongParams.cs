namespace Fixtures.Opinionated;

public class LongParameterListFixture
{
    public void Process(
        string name,
        int age,
        string email,
        string phone,
        string address,
        string city,
        string country)
    {
        Console.WriteLine($"{name}, {age}, {email}");
    }
}
