using System.Security.Cryptography;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Cryptography.EFCore.Tests;

/// <summary>
///     Persists protected data through EF and reads it back, including after the subject is erased.
/// </summary>
/// <remarks>
///     Also exercises the "co-locate the key table in your own context" path, by applying the module's
///     configuration to a consumer's <see cref="DbContext" /> rather than using
///     <see cref="CryptographyDbContext" /> directly.
/// </remarks>
public sealed class ProtectedValuePersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AesGcmSecretEncryptor _master;
    private readonly AppDbContext _db;
    private readonly EfCoreSubjectKeyStore _store;
    private readonly SubjectDataProtector _protector;

    public ProtectedValuePersistenceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _master = new AesGcmSecretEncryptor(
            new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32))));

        // The store needs the module's context type; it maps to the same tables in the same database.
        var keyDb = new CryptographyDbContext(
            new DbContextOptionsBuilder<CryptographyDbContext>().UseSqlite(_connection).Options);

        _store = new EfCoreSubjectKeyStore(keyDb, _master, TimeProvider.System);
        _protector = new SubjectDataProtector(_store, _store);
    }

    public void Dispose()
    {
        _db.Dispose();
        _master.Dispose();
        _connection.Dispose();
    }

    private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task ProtectedValue_RoundTripsThroughTheDatabase()
    {
        var packed = await _protector.ProtectAsync("subject-a", Bytes("ada@example.com"));
        _db.Customers.Add(new Customer { Id = 1, SubjectRef = "subject-a", Email = new ProtectedValue(packed) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _db.Customers.SingleAsync(c => c.Id == 1);
        var read = await _protector.TryReadAsync(loaded.Email.Packed);

        read.Outcome.Should().Be(DecryptOutcome.Success);
        read.Plain.Should().Equal(Bytes("ada@example.com"));
    }

    [Fact]
    public async Task StoredColumn_HoldsCiphertext_NotThePlaintext()
    {
        var packed = await _protector.ProtectAsync("subject-a", Bytes("ada@example.com"));
        _db.Customers.Add(new Customer { Id = 1, SubjectRef = "subject-a", Email = new ProtectedValue(packed) });
        await _db.SaveChangesAsync();

        // Read the raw column, bypassing the entity entirely.
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Email FROM Customers WHERE Id = 1";
        var raw = (byte[])(await cmd.ExecuteScalarAsync())!;

        System.Text.Encoding.UTF8.GetString(raw).Should().NotContain("ada@example.com");
        raw[0].Should().Be(0x01, "the stored bytes are the versioned ciphertext");
    }

    [Fact]
    public async Task ReadingARow_DoesNotRequireKnowingWhoseItIs()
    {
        // The key id travels in the value's header, so a reader holding only the row can resolve it.
        var packed = await _protector.ProtectAsync("subject-a", Bytes("secret"));
        _db.Customers.Add(new Customer { Id = 1, SubjectRef = "subject-a", Email = new ProtectedValue(packed) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var email = await _db.Customers.Where(c => c.Id == 1).Select(c => c.Email).SingleAsync();

        email.KeyId.Should().NotBeNullOrEmpty();
        (await _protector.TryReadAsync(email.Packed)).Outcome.Should().Be(DecryptOutcome.Success);
    }

    [Fact]
    public async Task AfterErasure_TheRowSurvivesButItsDataIsUnreadable()
    {
        // What crypto-shredding looks like from the database: the row is still there, the bytes are
        // still there, and nobody can read them — including in any backup taken before the erasure.
        var packed = await _protector.ProtectAsync("subject-a", Bytes("ada@example.com"));
        _db.Customers.Add(new Customer { Id = 1, SubjectRef = "subject-a", Email = new ProtectedValue(packed) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _store.DestroyAsync("subject-a");

        var loaded = await _db.Customers.SingleAsync(c => c.Id == 1);
        loaded.Email.IsEmpty.Should().BeFalse("the ciphertext is untouched — only the key is gone");

        var read = await _protector.TryReadAsync(loaded.Email.Packed);
        read.Outcome.Should().Be(DecryptOutcome.KeyDestroyed);
    }

    [Fact]
    public async Task EmptyProtectedValue_RoundTrips()
    {
        _db.Customers.Add(new Customer { Id = 1, SubjectRef = "subject-a", Email = ProtectedValue.None });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _db.Customers.SingleAsync(c => c.Id == 1);

        loaded.Email.IsEmpty.Should().BeTrue();
        loaded.Email.KeyId.Should().BeNull();
    }

    private sealed class Customer
    {
        public int Id { get; set; }
        public required string SubjectRef { get; set; }
        public ProtectedValue Email { get; set; }
    }

    private sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The consumer keeps the key table alongside its own data instead of a separate context.
            CryptographyDbContext.ApplyCryptographyConfigurations(modelBuilder);

            modelBuilder.Entity<Customer>(b =>
            {
                b.ToTable("Customers");
                b.HasKey(c => c.Id);
                b.Property(c => c.SubjectRef).HasMaxLength(128).IsRequired();
                b.Property(c => c.Email).HasConversion<ProtectedValueConverter>();
            });
        }
    }
}
