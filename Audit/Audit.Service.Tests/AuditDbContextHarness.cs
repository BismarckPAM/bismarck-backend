using Audit.Service.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Audit.Service.Tests;

/// <summary>
/// Owns a scoped <see cref="AuditDbContext"/> over a shared in-memory SQLite
/// connection. Each harness is a separate context (as a DI scope would be), so
/// tests exercise real queries rather than a cached identity map.
/// </summary>
internal sealed class AuditDbContextHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public AuditDbContextHarness(SqliteConnection connection)
    {
        _connection = connection;
        Context = new SqliteAuditDbContext(connection);
    }

    public SqliteAuditDbContext Context { get; }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}