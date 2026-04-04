namespace Fixtures.Opinionated;

public class BooleanFlagFixture
{
    public IEnumerable<string> GetItems(bool includeDeleted)
    {
        return includeDeleted
            ? new[] { "active", "deleted" }
            : new[] { "active" };
    }

    public void Export(bool includeHeaders, bool compress)
    {
        Console.WriteLine($"Headers: {includeHeaders}, Compress: {compress}");
    }
}
