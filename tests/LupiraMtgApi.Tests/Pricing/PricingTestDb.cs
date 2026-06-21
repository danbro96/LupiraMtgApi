using LupiraMtgApi.Pricing.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LupiraMtgApi.Tests.Pricing;

/// <summary>
/// A throwaway <see cref="PricingDbContext"/> backed by a single in-memory SQLite connection. The
/// connection is held open for the fixture's lifetime (an in-memory SQLite DB vanishes when its last
/// connection closes), and <see cref="Create"/> hands out fresh contexts sharing it — mirroring the
/// production pattern where ingest re-queries latest on each call. SQLite ignores the `prices` schema,
/// which is fine: these tests exercise service logic, not Postgres-specific DDL.
/// </summary>
public sealed class PricingTestDb : IDisposable
{
    private readonly SqliteConnection _conn;

    public PricingTestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        using var db = Create();
        db.Database.EnsureCreated();
    }

    public PricingDbContext Create() =>
        new(new DbContextOptionsBuilder<PricingDbContext>().UseSqlite(_conn).Options);

    public void Dispose() => _conn.Dispose();
}
