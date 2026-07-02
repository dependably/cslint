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

    // Sync lambda offloaded to the thread pool runs synchronously — blocking is fine here.
    public async Task ProcessWithSyncLambdaAsync()
    {
        await Task.Run(() => Thread.Sleep(500));
    }
}
