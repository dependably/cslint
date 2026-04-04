// CS010: explicit built-in types where var should be used
namespace Fixtures;

sealed class VarStyleViolations
{
    public void Method()
    {
        int x = 5;
        string s = "hello";
        bool flag = true;
        var result = new System.Collections.Generic.List<int>();
    }
}
