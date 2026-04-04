namespace Fixtures.Opinionated;

public class MissingCancellationTokenFixture
{
    public async Task FetchDataAsync()
    {
        await Task.Delay(100);
    }

    public async Task<string> LoadAsync(string key)
    {
        await Task.Delay(50);
        return key;
    }

    public async Task SaveAsync(string data, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
    }
}
