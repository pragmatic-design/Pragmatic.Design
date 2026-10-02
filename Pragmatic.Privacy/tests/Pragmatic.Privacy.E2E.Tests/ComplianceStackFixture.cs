using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Audit.EFCore;
using Pragmatic.Cryptography;
using Pragmatic.Cryptography.EFCore;
using Testcontainers.PostgreSql;

namespace Pragmatic.Privacy.E2E.Tests;

/// <summary>
///     The compliance stack — per-subject keys and the audit trail — over a real PostgreSQL database,
///     wired through dependency injection exactly as a consumer would wire it.
/// </summary>
/// <remarks>
///     <para>
///         Everything here is resolved from the container through public interfaces. Reaching for the
///         concrete types would have meant opening the modules' internals to this project — and would
///         have quietly skipped the registration extensions, which are the part a consumer actually
///         touches and which nothing else exercises.
///     </para>
///     <para>
///         PostgreSQL rather than SQLite because a real provider has already caught one defect in this
///         work: a <c>DateTimeOffset</c> comparison SQLite refuses to translate, which would have made
///         every time filter on the trail fail in production.
///     </para>
/// </remarks>
internal sealed class ComplianceStackFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("compliance_e2e")
        .WithUsername("pragmatic")
        .WithPassword("Pragmatic@Test!")
        .Build();

    private ServiceProvider? _services;
    private AesGcmSecretEncryptor? _master;

    /// <summary>Null when Docker is unavailable, so the tests skip rather than fail for the wrong reason.</summary>
    public string? ConnectionString { get; private set; }

    public CryptographyDbContext KeyDb { get; private set; } = null!;
    public AuditDbContext AuditDb { get; private set; } = null!;
    public ISubjectKeyStore Keys { get; private set; } = null!;
    public ISubjectDataProtector Protector { get; private set; } = null!;
    public IAuditTrail Trail { get; private set; } = null!;
    public IAuditTrailReader TrailReader { get; private set; } = null!;
    public AuditSealingService Sealing { get; private set; } = null!;
    public AuditRetentionService Retention { get; private set; } = null!;
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero));
    public IAuditSegmentNaming Naming { get; } = new HourlyAuditSegmentNaming();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch
        {
            ConnectionString = null;
            return;
        }

        // The master key never comes from the database holding the wrapped keys — that is the whole
        // point of wrapping them.
        _master = new AesGcmSecretEncryptor(
            new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32))));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ISecretEncryptor>(_master);
        services.AddSingleton(Naming);
        services.AddDbContext<CryptographyDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddDbContext<AuditDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddCryptographySubjectKeys();
        services.AddAuditTrail();

        _services = services.BuildServiceProvider();

        KeyDb = _services.GetRequiredService<CryptographyDbContext>();
        AuditDb = _services.GetRequiredService<AuditDbContext>();

        // Two contexts over one database cannot both call EnsureCreated: the first creates the
        // database, the second finds it already there and returns without creating its own tables —
        // leaving a half-built schema and no error. So the first one creates, and the second's schema
        // is scripted in explicitly.
        //
        // This is not a test workaround, it is the shape of the problem a consumer meets: Cryptography
        // and Audit each ship a context, and putting them in one database means using migrations rather
        // than EnsureCreated, or co-locating both through the modules' ApplyXxxConfigurations methods.
        await KeyDb.Database.EnsureCreatedAsync();
        await AuditDb.Database.ExecuteSqlRawAsync(AuditDb.Database.GenerateCreateScript());

        Keys = _services.GetRequiredService<ISubjectKeyStore>();
        Protector = _services.GetRequiredService<ISubjectDataProtector>();
        Trail = _services.GetRequiredService<IAuditTrail>();
        TrailReader = _services.GetRequiredService<IAuditTrailReader>();
        Sealing = _services.GetRequiredService<AuditSealingService>();
        Retention = _services.GetRequiredService<AuditRetentionService>();
    }

    public async Task DisposeAsync()
    {
        _services?.Dispose();
        _master?.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>A clock the test drives: sealing and retention are defined in elapsed time.</summary>
    internal sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
