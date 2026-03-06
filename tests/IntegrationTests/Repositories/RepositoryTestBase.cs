using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IntegrationTests.Repositories;

/// <summary>
/// Base class for repository integration tests using SQLite in-memory.
/// SQLite does not support all PostgreSQL features (e.g., specific PG functions, array params),
/// but covers CRUD, filtering, ordering, paging, includes, transactions, and constraints.
/// For production-fidelity tests, replace with a Testcontainers/PostgreSQL fixture.
/// </summary>
public abstract class RepositoryTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    protected readonly AppDbContext Ctx;

    protected RepositoryTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Ctx = new AppDbContext(options);
        Ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Ctx.Dispose();
        _connection.Dispose();
    }
}
