using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IntegrationTests.Repositories;

/// <summary>
/// Base class for repository integration tests using SQLite in-memory.
/// SQLite does not support all PostgreSQL features (e.g., specific PG functions, array params),
/// but covers CRUD, filtering, ordering, paging, includes, transactions, and constraints.
/// For production-fidelity tests, replace with a Testcontainers/PostgreSQL fixture.
///
/// NOTE: this schema is generated from the EF model via EnsureCreated(), so any column that
/// exists in Postgres only via a raw-SQL migration (not mapped on the entity, e.g. User.senha
/// or the User.addr_* fields) will be missing here. Repositories that select those columns via
/// raw SQL (see AuthRepository, UserRepository) must ALTER TABLE them into the test's Ctx in
/// their own test constructor — see AuthRepositoryTests for the pattern. Remember to do this
/// whenever a new raw-SQL-only column is added to a table these tests touch.
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
