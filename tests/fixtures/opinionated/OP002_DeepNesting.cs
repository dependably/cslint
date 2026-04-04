namespace Fixtures.Opinionated;

public class DeepNestingFixture
{
    public void ProcessItems(List<string> items)
    {
        foreach (var item in items)
        {
            if (item != null)
            {
                if (item.Length > 0)
                {
                    if (item.StartsWith("A"))
                    {
                        if (item.Length > 3)
                        {
                            for (int i = 0; i < item.Length; i++)
                            {
                                Console.WriteLine(item[i]);
                            }
                        }
                    }
                }
            }
        }
    }
}
