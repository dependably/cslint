using Microsoft.EntityFrameworkCore;

namespace Fixtures.Sast;

public class SqlInjectionFixture
{
    private readonly DbContext _db;

    public SqlInjectionFixture(DbContext db) => _db = db;

    public IQueryable<object> GetUser(string userId)
    {
        return _db.Set<object>().FromSqlRaw($"SELECT * FROM Users WHERE Id = {userId}");
    }

    public IQueryable<object> GetUserSafe(string userId)
    {
        return _db.Set<object>().FromSqlRaw("SELECT * FROM Users WHERE Id = {0}", userId);
    }
}
