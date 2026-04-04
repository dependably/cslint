// CS033: field only set in constructor, should be readonly
namespace Fixtures;

public class ReadonlyViolation
{
    private int _count;
    private string _name;

    public ReadonlyViolation(int count, string name)
    {
        _count = count;
        _name = name;
    }

    public int Count => _count;
    public string Name => _name;
}
