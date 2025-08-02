using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using RadioWash.Api.Infrastructure.Data;

namespace RadioWash.Api.Test.Helpers;

/// <summary>
/// Factory for creating test database contexts with SQLite in-memory database.
/// SQLite in-memory supports transactions and is closer to real SQL behavior than EF InMemory provider.
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a new RadioWashDbContext with SQLite in-memory database.
    /// The connection must be kept open for the database to persist.
    /// </summary>
    /// <returns>A tuple containing the DbContext and the connection that must be disposed</returns>
    public static (RadioWashDbContext context, SqliteConnection connection) CreateInMemoryContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new RadioWashDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}