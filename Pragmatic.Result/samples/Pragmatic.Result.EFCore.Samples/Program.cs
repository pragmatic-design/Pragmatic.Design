using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Result.EntityFrameworkCore;
using Pragmatic.Result.EntityFrameworkCore.Samples;

Console.WriteLine("Pragmatic.Result.EntityFrameworkCore Samples");
Console.WriteLine("=============================================");
Console.WriteLine();

// SQLite (in-memory) — a real relational store: enforces the unique index on Email, so a duplicate
// insert becomes a typed error from SaveChangesAsResultAsync. The connection is kept open for the
// lifetime of the in-memory database.
await using var connection = new SqliteConnection("DataSource=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<SampleDbContext>()
    .UseSqlite(connection)
    .Options;

await using var context = new SampleDbContext(options);
await context.Database.EnsureCreatedAsync();

// Seed some data
context.Users.AddRange(
    new User { Id = 1, Name = "Alice", Email = "alice@example.com" },
    new User { Id = 2, Name = "Bob", Email = "bob@example.com" },
    new User { Id = 3, Name = "Charlie", Email = "charlie@example.com" }
);
await context.SaveChangesAsync();

// =====================================================
// FindAsResultAsync - Find by primary key
// =====================================================
Console.WriteLine("1. FindAsResultAsync - Find by primary key");
Console.WriteLine("-------------------------------------------");

// Success case
var findResult = await context.Users.FindAsResultAsync(1, "User");
if (findResult.IsSuccess) Console.WriteLine($"  Found: {findResult.Value.Name} ({findResult.Value.Email})");

// Failure case - returns NotFoundError
var notFoundResult = await context.Users.FindAsResultAsync(999, "User");
if (notFoundResult.IsFailure)
{
    Console.WriteLine($"  Not found: {notFoundResult.Error.EntityType} with ID {notFoundResult.Error.EntityId}");
    Console.WriteLine($"  Error code: {notFoundResult.Error.Code}");
}

Console.WriteLine();

// =====================================================
// FirstOrDefaultAsResultAsync - Query with predicate
// =====================================================
Console.WriteLine("2. FirstOrDefaultAsResultAsync - Query with predicate");
Console.WriteLine("------------------------------------------------------");

// With LINQ Where clause
var queryResult = await context.Users
    .Where(u => u.Name.StartsWith("B"))
    .FirstOrDefaultAsResultAsync("User");

if (queryResult.IsSuccess) Console.WriteLine($"  Found user starting with 'B': {queryResult.Value.Name}");

// With predicate
var predicateResult = await context.Users
    .FirstOrDefaultAsResultAsync(u => u.Email == "charlie@example.com", "User");

if (predicateResult.IsSuccess) Console.WriteLine($"  Found by email: {predicateResult.Value.Name}");

// Not found case
var notFoundQuery = await context.Users
    .FirstOrDefaultAsResultAsync(u => u.Name == "NonExistent", "User");

if (notFoundQuery.IsFailure) Console.WriteLine($"  Query returned NotFoundError: {notFoundQuery.Error.Code}");
Console.WriteLine();

// =====================================================
// SingleOrDefaultAsResultAsync - Exactly one result
// =====================================================
Console.WriteLine("3. SingleOrDefaultAsResultAsync - Exactly one result");
Console.WriteLine("-----------------------------------------------------");

var singleResult = await context.Users
    .Where(u => u.Id == 2)
    .SingleOrDefaultAsResultAsync("User");

if (singleResult.IsSuccess) Console.WriteLine($"  Single user with Id=2: {singleResult.Value.Name}");
Console.WriteLine();

// =====================================================
// SaveChangesAsResultAsync - typed errors, no try/catch
// =====================================================
Console.WriteLine("4. SaveChangesAsResultAsync - typed DB errors");
Console.WriteLine("----------------------------------------------");

// Success: insert a brand-new user, no try/catch needed.
context.Users.Add(new User { Id = 4, Name = "Dana", Email = "dana@example.com" });
var okSave = await context.SaveChangesAsResultAsync();
Console.WriteLine("  Insert Dana → " + okSave.Match(
    () => "Success",
    conflict => $"Conflict: {conflict.Code}",
    constraint => $"Constraint: {constraint.Code}"));

// Failure: duplicate Email violates the unique index → typed error, not a thrown DbUpdateException.
context.Users.Add(new User { Id = 5, Name = "Eve", Email = "alice@example.com" });
var dupSave = await context.SaveChangesAsResultAsync();
Console.WriteLine("  Insert Eve (duplicate email) → " + dupSave.Match(
    () => "Success (unexpected)",
    conflict => $"DbConflictError [{conflict.Code}]: {conflict.Reason}",
    constraint => $"DbConstraintError [{constraint.Code}]: {constraint.Details}"));

// The failed unit-of-work leaves the context dirty; drop the offending tracked entity before moving on.
context.ChangeTracker.Clear();
Console.WriteLine();

// =====================================================
// Pattern matching with Result
// =====================================================
Console.WriteLine("5. Pattern matching with Match");
Console.WriteLine("-------------------------------");

var matchResult = await context.Users.FindAsResultAsync(1, "User");
var output = matchResult.Match(
    user => $"  Found user: {user.Name}",
    error => $"  Error: {error.Code} - {error.EntityType} not found"
);
Console.WriteLine(output);

var matchNotFound = await context.Users.FindAsResultAsync(999, "User");
output = matchNotFound.Match(
    user => $"  Found user: {user.Name}",
    error => $"  Error: {error.Code} - {error.EntityType} not found"
);
Console.WriteLine(output);
Console.WriteLine();

// =====================================================
// Railway-Oriented Programming with Bind
// =====================================================
Console.WriteLine("6. Railway-Oriented Programming with Map");
Console.WriteLine("-----------------------------------------");

var chainResult = await context.Users.FindAsResultAsync(1, "User");

// Chain operations: Find user -> Get email domain
var domainResult = chainResult
    .Map(u => u.Email)
    .Map(email => email.Split('@')[1]);

if (domainResult.IsSuccess) Console.WriteLine($"  User's email domain: {domainResult.Value}");
Console.WriteLine();

Console.WriteLine("Sample completed successfully!");

namespace Pragmatic.Result.EntityFrameworkCore.Samples
{
    // =====================================================
    // Sample entities
    // =====================================================

    public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Unique index on Email — a duplicate insert is what SaveChangesAsResultAsync classifies
            // into a typed DbConflictError/DbConstraintError.
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Explicit Ids in the sample, so don't auto-generate the key.
            modelBuilder.Entity<User>()
                .Property(u => u.Id)
                .ValueGeneratedNever();
        }
    }

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
