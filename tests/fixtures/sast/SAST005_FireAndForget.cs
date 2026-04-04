namespace Fixtures.Sast;

public class FireAndForgetFixture
{
    private readonly Repository _repo;

    public FireAndForgetFixture(Repository repo) => _repo = repo;

    public void SaveWithoutAwait()
    {
        _repo.SaveAsync();
    }

    public void DiscardTask()
    {
        _ = _repo.SaveAsync();
    }

    public async Task SaveCorrectly()
    {
        await _repo.SaveAsync();
    }
}

public class Repository
{
    public Task SaveAsync() => Task.CompletedTask;
}
