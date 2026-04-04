// CS011: block bodies where expression bodies are preferred
namespace Fixtures;

public class ExpressionBodyViolations
{
    private int _value;

    public int Value
    {
        get { return _value; }
    }

    public string GetName()
    {
        return "name";
    }

    public int Double(int x)
    {
        return x * 2;
    }
}
