using Microsoft.EntityFrameworkCore;
using System.Data.SqlClient;

namespace Fixtures.Sast;

public class SqlInjectionFixture
{
    private readonly DbContext _db;

    public SqlInjectionFixture(DbContext db) => _db = db;

    // SAST003: method invocation with interpolated string (existing coverage)
    public IQueryable<object> GetUser(string userId)
    {
        return _db.Set<object>().FromSqlRaw($"SELECT * FROM Users WHERE Id = {userId}");
    }

    // Negative: constant SQL string — no interpolation, should not flag
    public IQueryable<object> GetUserSafe(string userId)
    {
        return _db.Set<object>().FromSqlRaw("SELECT * FROM Users WHERE Id = {0}", userId);
    }

    // SAST003: constructor call with interpolated string
    public SqlCommand BuildCommand(string userId)
    {
        return new SqlCommand($"SELECT * FROM Users WHERE Id = {userId}");
    }

    // SAST003: CommandText assignment with interpolated string
    public void SetCommandText(SqlCommand cmd, string userId)
    {
        cmd.CommandText = $"DELETE FROM Users WHERE Id = {userId}";
    }
}
