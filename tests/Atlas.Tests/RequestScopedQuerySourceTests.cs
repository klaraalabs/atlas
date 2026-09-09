using Atlas.Query;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Atlas.Tests;

public sealed class RequestScopedQuerySourceTests : IAsyncLifetime
{
    private static readonly Guid TenantOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _db.Records.AddRange(
            new TestRecord { Id = 1, TenantId = TenantOne, Category = "shared", Amount = 10, IsRetired = false },
            new TestRecord { Id = 2, TenantId = TenantOne, Category = "private", Amount = 20, IsRetired = false },
            new TestRecord { Id = 3, TenantId = TenantOne, Category = "retired", Amount = 100, IsRetired = true },
            new TestRecord { Id = 4, TenantId = TenantTwo, Category = "shared", Amount = 1_000, IsRetired = false },
            new TestRecord { Id = 5, TenantId = TenantTwo, Category = "private", Amount = 2_000, IsRetired = false });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Normal_query_never_reads_outside_the_supplied_source()
    {
        var executor = CreateExecutor();
        var source = TenantOneSource();

        var result = await executor.ExecuteAsync(source, new AtlasQuery
        {
            Select = ["id", "tenantid", "category", "amount"],
            Limit = 100
        });

        Assert.Equal([1, 2], ReadIds(result));
    }

    [Fact]
    public async Task Client_filter_cannot_select_another_tenant()
    {
        var result = await CreateExecutor().ExecuteAsync(TenantOneSource(), new AtlasQuery
        {
            Select = ["id"],
            Where = new WhereClause
            {
                Op = "eq",
                Field = "tenantid",
                Type = "guid",
                Value = TenantTwo.ToString()
            }
        });

        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task Total_count_uses_the_source_and_policy_filters_before_pagination()
    {
        var result = await CreateExecutor().ExecuteWithCountAsync(TenantOneSource(), new AtlasQuery
        {
            Select = ["id"],
            Limit = 1
        });

        Assert.Single(result.Data);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Aggregates_use_the_supplied_source()
    {
        var result = await CreateExecutor().ExecuteAsync(TenantOneSource(), new AtlasQuery
        {
            Select = ["count", "amount.sum:total", "amount.avg:average"]
        });

        var row = Assert.Single(result.Data);
        Assert.Equal(2, Convert.ToInt32(row["count"]));
        Assert.Equal(30, Convert.ToInt32(row["total"]));
        Assert.Equal(15, Convert.ToInt32(row["average"]));
    }

    [Fact]
    public async Task Group_by_uses_the_supplied_source()
    {
        var result = await CreateExecutor().ExecuteAsync(TenantOneSource(), new AtlasQuery
        {
            Select = ["category", "count", "amount.sum:total"],
            GroupBy = ["category"]
        });

        Assert.Equal(2, result.Data.Count);
        Assert.Contains(result.Data, row =>
            (string)row["category"]! == "shared" &&
            Convert.ToInt32(row["count"]) == 1 &&
            Convert.ToInt32(row["total"]) == 10);
        Assert.DoesNotContain(result.Data, row => Convert.ToInt32(row["total"]) >= 1_000);
    }

    [Fact]
    public async Task DbContext_overload_matches_an_equivalent_DbSet_source()
    {
        var executor = CreateExecutor();
        var query = new AtlasQuery { Select = ["id"], Limit = 100 };

        var contextResult = await executor.ExecuteAsync(_db, query);
        var sourceResult = await executor.ExecuteAsync(_db.Set<TestRecord>(), query);

        Assert.Equal(ReadIds(contextResult), ReadIds(sourceResult));
    }

    private IQueryable<TestRecord> TenantOneSource()
        => _db.Records.Where(record => record.TenantId == TenantOne);

    private static AtlasQueryExecutor<TestRecord> CreateExecutor()
        => Atlas.For<TestRecord>()
            .AllowAllFields()
            .AllowAllOperators()
            .WhereAlways(record => !record.IsRetired)
            .BuildExecutor();

    private static int[] ReadIds(AtlasResult result)
        => result.Data.Select(row => Convert.ToInt32(row["id"])).ToArray();

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestRecord> Records => Set<TestRecord>();
    }

    private sealed class TestRecord
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Category { get; set; } = string.Empty;
        public int Amount { get; set; }
        public bool IsRetired { get; set; }
    }
}
