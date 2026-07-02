using System.Collections.Generic;

namespace Fixtures.Sast;

public class DynamicFixture
{
    // SAST008: return type annotation
    public dynamic GetValue() => 42;

    // SAST008: parameter type
    public void Process(dynamic input)
    {
        var result = input.ToString();
    }

    // SAST008: explicit cast to dynamic
    public void CastExample(object obj)
    {
        var x = (dynamic)obj;
    }

    // SAST008: 'as dynamic' expression
    public void AsExample(object obj)
    {
        var x = obj as dynamic;
    }

    // SAST008: generic type argument
    public void GenericExample()
    {
        var list = new List<dynamic>();
    }

    // SAST008: array element type
    public void ArrayExample()
    {
        dynamic[] arr = new dynamic[3];
    }

    // SAST008: foreach iteration variable type
    public void ForeachExample(IEnumerable<object> items)
    {
        foreach (dynamic d in items)
        {
            _ = d.ToString();
        }
    }
}
