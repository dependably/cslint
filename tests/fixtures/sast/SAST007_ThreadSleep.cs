namespace Fixtures.Sast;

public class ThreadSleepFixture
{
    public async Task ProcessAsync()
    {
        Thread.Sleep(500);
        await Task.Yield();
    }

    public async Task ProcessCorrectlyAsync()
    {
        await Task.Delay(500);
    }
}
