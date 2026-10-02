using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Internationalization.Tests.UserCulture;

/// <summary>
///     The database the generated resolver reads. The resolver asks DI for a plain
///     <see cref="DbContext" />, so this is what gets registered under that contract.
/// </summary>
public sealed class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    /// <summary>The users.</summary>
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<AppUser>().HasKey(u => u.Id);
}
