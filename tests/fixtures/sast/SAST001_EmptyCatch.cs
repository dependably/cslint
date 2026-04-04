namespace Fixtures.Sast;

public class EmptyCatchFixture
{
    public void BadEmpty()
    {
        try
        {
            int x = int.Parse("bad");
        }
        catch (Exception)
        {
        }
    }

    public void BadDiscard()
    {
        try
        {
            int x = int.Parse("bad");
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    public void Good()
    {
        try
        {
            int x = int.Parse("bad");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            throw;
        }
    }
}
