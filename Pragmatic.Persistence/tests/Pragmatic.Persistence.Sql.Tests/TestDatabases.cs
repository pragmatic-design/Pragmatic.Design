using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Pragmatic.Persistence.Workload;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The seeded databases the tests run against: SQLite in a file per test run, PostgreSQL, SQL Server and MySQL
///     in Docker when it answers (otherwise those tests are skipped with the reason). One instance per
///     test collection: containers start once.
/// </summary>
public sealed class TestDatabases : IAsyncLifetime
{
    private const string MySqlPassword = "spike";
    private PostgreSqlContainer? _postgres;
    private MsSqlContainer? _sqlServer;
    private IContainer? _mySql;
    private IContainer? _oracle;
    private string? _dockerUnavailable;

    static TestDatabases()
    {
        OracleConfiguration.BindByName = true; // the statements name their parameters (:pN), several times and out of order
    }

    public Seed Seed { get; } = new();

    public string SqlitePath { get; } = Path.Combine(AppContext.BaseDirectory, $"tests-{Guid.NewGuid():N}.sqlite");

    public string? DockerUnavailable => _dockerUnavailable;

    /// <summary>True when Docker answered but a container or its seed failed: the Docker dialects' tests then skip, and <c>DatabasesTests</c> fails with the reason.</summary>
    public bool DockerAnswered { get; private set; }

    /// <summary>The PostgreSQL container's log so far (statements included: the server runs with log_statement=all).</summary>
    public async Task<string> PostgresLogAsync()
    {
        var (stdout, stderr) = await (_postgres ?? throw new InvalidOperationException(_dockerUnavailable)).GetLogsAsync();
        return stdout + stderr;
    }

    public async Task InitializeAsync()
    {
        await SeedAsync(SqlDialect.Sqlite);
        try
        {
            // PostgreSQL 17 with pgvector (the vector type and its HNSW index); every statement in the container's log: what the origin comment is for.
            _postgres = new PostgreSqlBuilder().WithImage("pgvector/pgvector:pg17").WithCommand("-c", "log_statement=all").Build();
            await _postgres.StartAsync();
            DockerAnswered = true; // from here a failure is a container's or a seed's, not Docker's absence: DatabasesTests reports it
            // SQL Server 2025: the VECTOR type and VECTOR_DISTANCE; with the Full-Text component (docker/mssql-fts: the official image lacks it), built
            // once from the official image and kept under its tag (the layers cached: a later build is a check).
            var sqlServerImage = new ImageFromDockerfileBuilder().WithDockerfileDirectory(CommonDirectoryPath.GetProjectDirectory(), "docker/mssql-fts").WithName("pragmatic-spike/mssql-fts:2025").WithCleanUp(false).Build();
            await sqlServerImage.CreateAsync();
            _sqlServer = new MsSqlBuilder().WithImage(sqlServerImage).Build();
            await _sqlServer.StartAsync();
            using (var master = new SqlConnection(_sqlServer.GetConnectionString()))
            {
                // The tables in an application database: master refuses full-text search (and is no place for them).
                master.Open();
                using var create = master.CreateCommand();
                create.CommandText = "CREATE DATABASE [spike]";
                create.ExecuteNonQuery();
            }

            // MySQL 8.4 (the LTS), through the generic container: no module package. Ready when the server listens on its port (its log says so twice: the bootstrap, then the server).
            _mySql = new ContainerBuilder().WithImage("mysql:8.4").WithEnvironment("MYSQL_ROOT_PASSWORD", MySqlPassword).WithEnvironment("MYSQL_DATABASE", "spike").WithPortBinding(3306, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready for connections.*port: 3306")).Build();
            await _mySql.StartAsync();
            // Oracle Database 23ai Free (gvenzl's image, the fast-start variant, not slim: Oracle Text, which CONTAINS needs, is in it): SYSTEM in the FREEPDB1 pluggable database.
            // The tables in an application user's schema (USERS tablespace: a VECTOR column is refused in SYSTEM's), with DBA for the tenant test's schemas.
            _oracle = new ContainerBuilder().WithImage("gvenzl/oracle-free:23-faststart").WithEnvironment("ORACLE_PASSWORD", MySqlPassword)
                .WithEnvironment("APP_USER", "spike").WithEnvironment("APP_USER_PASSWORD", MySqlPassword).WithPortBinding(1521, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("DATABASE IS READY TO USE!")).Build();
            await _oracle.StartAsync();
            using (var system = new OracleConnection($"User Id=system;Password={MySqlPassword};Data Source={_oracle.Hostname}:{_oracle.GetMappedPublicPort(1521)}/FREEPDB1"))
            {
                system.Open();
                using var grant = system.CreateCommand();
                grant.CommandText = "GRANT DBA TO spike";
                grant.ExecuteNonQuery();
                // Flashback Query (AsOf) reads the undo: the image's 11 MB undo tablespace is reused under the suite's load before it grows (ORA-08176), so the retention is guaranteed.
                using var undo = system.CreateCommand();
                undo.CommandText = "ALTER TABLESPACE UNDOTBS1 RETENTION GUARANTEE";
                undo.ExecuteNonQuery();
            }
            await SeedAsync(SqlDialect.PostgreSql);
            await SeedAsync(SqlDialect.SqlServer);
            await SeedAsync(SqlDialect.MySql);
            await SeedAsync(SqlDialect.Oracle);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A container that failed before PostgreSQL's start completed: its logs say Docker answered (the failure is the server's, reported
            // by DatabasesTests with the last lines), no logs say Docker is not there (the Docker tests skip).
            var log = await TailAsync(_postgres);
            DockerAnswered |= log is not null;
            _dockerUnavailable = "Docker containers did not start: " + e.GetType().Name + ": " + e.Message.Split('\n')[0] + (log is null ? "" : " — PostgreSQL log: " + log);
            _postgres = null;
            _sqlServer = null;
            _mySql = null;
            _oracle = null;
        }
    }

    /// <summary>The container's last log lines; null when there are none to read (never created: Docker did not answer).</summary>
    private static async Task<string?> TailAsync(IContainer? container)
    {
        if (container is null)
            return null;
        try
        {
            var (stdout, stderr) = await container.GetLogsAsync(timestampsEnabled: false);
            return string.Join(" | ", (stdout + stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(6));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null; // no logs: the container was never created, Docker did not answer
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        if (_sqlServer is not null)
            await _sqlServer.DisposeAsync();
        if (_mySql is not null)
            await _mySql.DisposeAsync();
        if (_oracle is not null)
            await _oracle.DisposeAsync();
        SqliteConnection.ClearAllPools(); // pooled connections keep the file open
        if (File.Exists(SqlitePath))
            File.Delete(SqlitePath);
    }

    public string ConnectionString(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Sqlite => $"Data Source={SqlitePath}",
        SqlDialect.PostgreSql => _postgres?.GetConnectionString() ?? throw new InvalidOperationException(_dockerUnavailable),
        SqlDialect.SqlServer => _sqlServer is null ? throw new InvalidOperationException(_dockerUnavailable) : new SqlConnectionStringBuilder(_sqlServer.GetConnectionString()) { InitialCatalog = "spike" }.ConnectionString,
        SqlDialect.MySql => _mySql is null ? throw new InvalidOperationException(_dockerUnavailable) : $"Server={_mySql.Hostname};Port={_mySql.GetMappedPublicPort(3306)};User ID=root;Password={MySqlPassword};Database=spike",
        SqlDialect.Oracle => _oracle is null ? throw new InvalidOperationException(_dockerUnavailable) : $"User Id=spike;Password={MySqlPassword};Data Source={_oracle.Hostname}:{_oracle.GetMappedPublicPort(1521)}/FREEPDB1",
        _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
    };

    public DbConnection Open(SqlDialect dialect)
    {
        var connection = Connect(dialect);
        connection.Open();
        if (dialect == SqlDialect.MySql)
            SqlMySql.Prepare(connection); // ANSI_QUOTES and UTC on the session, as the library's statements expect
        if (dialect == SqlDialect.Oracle)
            SqlOracle.Prepare(connection); // binary sorting and comparison, UTC
        return connection;
    }

    /// <summary>A closed connection to the dialect's database (what a pool opens itself).</summary>
    public DbConnection Connect(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Sqlite => new SqliteConnection(ConnectionString(dialect)),
        SqlDialect.PostgreSql => new NpgsqlConnection(ConnectionString(dialect)),
        SqlDialect.SqlServer => new SqlConnection(ConnectionString(dialect)),
        SqlDialect.MySql => new MySqlConnection(ConnectionString(dialect)),
        SqlDialect.Oracle => new OracleConnection(ConnectionString(dialect)),
        _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
    };

    public SqlSession Session(SqlDialect dialect) => new(Open(dialect), dialect);

    /// <summary>EF Core on the same database, mapped as the schema is: the oracle.</summary>
    /// <summary>EF Core on the same database; <paramref name="log" /> receives its command log (what it ran, for a failing comparison).</summary>
    public WorkloadDbContext Ef(SqlDialect dialect, Action<string>? log = null)
    {
        var builder = new DbContextOptionsBuilder<WorkloadDbContext>();
        if (log is not null)
            builder.LogTo(log, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted]);
        switch (dialect)
        {
            case SqlDialect.Sqlite:
                builder.UseSqlite(ConnectionString(dialect));
                break;
            case SqlDialect.PostgreSql:
                builder.UseNpgsql(ConnectionString(dialect));
                break;
            case SqlDialect.SqlServer:
                builder.UseSqlServer(ConnectionString(dialect));
                break;
            case SqlDialect.MySql:
                builder.UseMySQL(ConnectionString(dialect));
                break;
            case SqlDialect.Oracle:
                builder.UseOracle(ConnectionString(dialect), o => o.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23));
                break;
        }

        return new WorkloadDbContext(builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options, dialect);
    }

    private async Task SeedAsync(SqlDialect dialect)
    {
        using var connection = Open(dialect);
        if (connection is NpgsqlConnection npgsql)
        {
            // pgvector's type before anything else, and Npgsql's type catalog reloaded: a type created after the first connection is otherwise unknown to every connection of the data source.
            using (var extension = npgsql.CreateCommand())
            {
                extension.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
                await extension.ExecuteNonQueryAsync();
            }

            await npgsql.ReloadTypesAsync();
        }

        await Seeder.SeedAsync(connection, dialect, Seed);
        await ExtrasSchema.CreateAsync(connection, dialect); // the test-only entities' tables, empty
    }
}

[CollectionDefinition("databases")]
public sealed class DatabasesCollection : ICollectionFixture<TestDatabases>;

/// <summary>EF Core's view of the workload for the oracle, mapped to the same tables and columns the schema creates.</summary>
public sealed class WorkloadDbContext(DbContextOptions<WorkloadDbContext> options, SqlDialect dialect) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<OrderTag> OrderTags => Set<OrderTag>();

    public DbSet<Reminder> Reminders => Set<Reminder>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (dialect != SqlDialect.Oracle)
            return;
        // Oracle, as the schema stores it: guids as their text, a DateTimeOffset as the UTC instant in a TIMESTAMP.
        configurationBuilder.Properties<Guid>().HaveConversion<string>().HaveMaxLength(36);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcInstantConverter>();
    }

    private sealed class UtcInstantConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTime>(d => d.UtcDateTime, d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The tracked entities (IChangeTracking): their tracking state is not a column.
        modelBuilder.Entity<Event>().Ignore(e => e.IsNew);
        modelBuilder.Entity<Ticket>().ToTable("Tickets").HasKey(t => t.Id);
        modelBuilder.Entity<Ticket>().Ignore(t => t.IsNew);
        modelBuilder.Entity<Invoice>().ToTable("Invoices").HasKey(i => i.Id);
        modelBuilder.Entity<Invoice>().Ignore(i => i.IsNew);
        modelBuilder.Entity<Invoice>().Property(i => i.Version).IsConcurrencyToken();
        modelBuilder.Entity<Invoice>().HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
        modelBuilder.Entity<Invoice>().OwnsOne(i => i.Active, active =>
        {
            active.Property(a => a.From).HasColumnName("Active_From");
            active.Property(a => a.To).HasColumnName("Active_To");
        });
        modelBuilder.Entity<InvoiceLine>().ToTable("InvoiceLines").HasKey(l => l.Id);
        modelBuilder.Entity<InvoiceLine>().Ignore(l => l.IsNew);
        // The test-only hierarchy (Extras.cs): TPH on "Kind" with the type names, the parent kind as its name, each derived type's Parent on the root's ParentId.
        modelBuilder.Entity<Assignment>().ToTable("Assignments").HasKey(a => a.Id);
        modelBuilder.Entity<Assignment>().HasDiscriminator<string>("Kind").HasValue<CustomerAssignment>("CustomerAssignment").HasValue<OrderAssignment>("OrderAssignment").HasValue<SiteAssignment>("SiteAssignment");
        modelBuilder.Entity<Assignment>().Property(a => a.ParentType).HasConversion<string>();
        modelBuilder.Entity<CustomerAssignment>().HasOne(a => a.Parent).WithMany().HasForeignKey(a => a.ParentId);
        modelBuilder.Entity<OrderAssignment>().HasOne(a => a.Parent).WithMany().HasForeignKey(a => a.ParentId);
        modelBuilder.Entity<Reminder>().ToTable("Reminders").HasKey(r => r.Id);
        modelBuilder.Entity<Reminder>().HasOne(r => r.Assignment).WithMany().HasForeignKey(r => r.AssignmentId);
        modelBuilder.Entity<Customer>().ToTable("Customers").HasKey(c => c.Id);
        modelBuilder.Entity<Order>().ToTable("Orders").HasKey(o => o.Id);
        modelBuilder.Entity<Order>().HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
        modelBuilder.Entity<Order>().Property(o => o.Status).HasConversion<int>();
        modelBuilder.Entity<OrderLine>().ToTable("OrderLines").HasKey(l => l.Id);
        modelBuilder.Entity<Event>().ToTable("Events").HasKey(e => e.Id);
        modelBuilder.Entity<Event>().Property(e => e.Version).IsConcurrencyToken();
        modelBuilder.Entity<Note>().ToTable("Notes").HasKey(n => n.Id);
        modelBuilder.Entity<Note>().Property(n => n.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<OrderTag>().ToTable("OrderTags").HasKey(t => new { t.OrderId, t.Tag });
        modelBuilder.Entity<OrderTag>().Property(t => t.Version).IsConcurrencyToken();
        modelBuilder.Entity<OrderLine>().HasOne(l => l.Order).WithMany(o => o.Lines).HasForeignKey(l => l.OrderId);
        if (dialect == SqlDialect.Sqlite)
        {
            modelBuilder.Entity<Note>().Property(n => n.OrderId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<OrderTag>().Property(t => t.OrderId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Order>().Property(o => o.Total).HasConversion<double>();
            modelBuilder.Entity<OrderLine>().Property(l => l.UnitPrice).HasConversion<double>();
            modelBuilder.Entity<Order>().Property(o => o.CreatedAt).HasConversion<string>();
            modelBuilder.Entity<Customer>().Property(c => c.CreatedAt).HasConversion<string>();
            modelBuilder.Entity<Customer>().Property(c => c.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Order>().Property(o => o.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Order>().Property(o => o.CustomerId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<OrderLine>().Property(l => l.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<OrderLine>().Property(l => l.OrderId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Event>().Property(e => e.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Event>().Property(e => e.OrderId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Event>().Property(e => e.At).HasConversion<string>();
            modelBuilder.Entity<Assignment>().Property(a => a.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Assignment>().Property(a => a.ParentId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Reminder>().Property(r => r.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Reminder>().Property(r => r.AssignmentId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Ticket>().Property(t => t.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Ticket>().Property(t => t.ProjectId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Ticket>().Property(t => t.Amount).HasConversion<double>();
            modelBuilder.Entity<Ticket>().Property(t => t.CreatedAt).HasConversion<string>();
            modelBuilder.Entity<Invoice>().Property(i => i.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<Invoice>().Property(i => i.Total).HasConversion<double>();
            modelBuilder.Entity<InvoiceLine>().Property(l => l.Id).HasConversion(g => g.ToString(), s => Guid.Parse(s));
            modelBuilder.Entity<InvoiceLine>().Property(l => l.InvoiceId).HasConversion(g => g.ToString(), s => Guid.Parse(s));
        }
        else
        {
            modelBuilder.Entity<Order>().Property(o => o.Total).HasPrecision(18, 2);
            modelBuilder.Entity<OrderLine>().Property(l => l.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<Ticket>().Property(t => t.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<Invoice>().Property(i => i.Total).HasPrecision(18, 2);
        }

        if (dialect == SqlDialect.MySql)
        {
            // A DateTimeOffset is a UTC DATETIME(6) there (the session's time zone is UTC): EF reads and writes it as the library does.
            var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTime>(d => d.UtcDateTime, d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)));
            modelBuilder.Entity<Order>().Property(o => o.CreatedAt).HasConversion(utc).HasColumnType("datetime(6)");
            modelBuilder.Entity<Customer>().Property(c => c.CreatedAt).HasConversion(utc).HasColumnType("datetime(6)");
            modelBuilder.Entity<Event>().Property(e => e.At).HasConversion(utc).HasColumnType("datetime(6)");
            modelBuilder.Entity<Ticket>().Property(t => t.CreatedAt).HasConversion(utc).HasColumnType("datetime(6)");
        }
    }
}
