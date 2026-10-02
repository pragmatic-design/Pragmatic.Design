using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Result;
using Pragmatic.Result.EntityFrameworkCore;
using Pragmatic.Result.EntityFrameworkCore.PostgreSQL;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     The violated unique index is answered as the <b>properties</b> it covers.
/// </summary>
/// <remarks>
///     <para>
///         The provider names an index; a client wants a field. `IX_Employees_WorkEmail` is no more
///         actionable than nothing, and this is the layer that has the model — the same trade the
///         foreign-key branch makes, which is why both live in one class.
///     </para>
///     <para>
///         ⚠️ The composite case is the one worth pinning: naming only the first property of a
///         two-property index is a <em>wrong</em> answer, not a partial one. The collision is the
///         combination, and a form told to mark one of the two marks the wrong thing half the time.
///     </para>
/// </remarks>
public class ADuplicateNamesThePropertiesTests
{
    [Fact]
    public void ASingleColumnIndex_IsAnsweredAsItsProperty()
    {
        var error = Classify("IX_People_Email");

        var conflict = error.Should().BeOfType<DbConflictError>().Which;
        conflict.FieldName.Should().Be("Email");
        conflict.Reason.Should().Be("Duplicate value for Email.");
    }

    [Fact]
    public void ACompositeIndex_NamesEveryPropertyItCovers()
    {
        var error = Classify("IX_People_TenantId_Code");

        var conflict = error.Should().BeOfType<DbConflictError>().Which;
        conflict.FieldName.Should().Be("TenantId, Code");
        conflict.Reason.Should().Be("Duplicate value for TenantId, Code.");
    }

    /// <summary>
    ///     The control: a name the model does not recognise is left as the database gave it.
    /// </summary>
    /// <remarks>
    ///     Without it, "the field is named" is satisfied by a lookup that answers something for every
    ///     input — and the caller would be told a property that had nothing to do with the collision.
    ///     The constraint still travels, which is more than there was before.
    /// </remarks>
    [Fact]
    public void AnIndexTheModelDoesNotKnow_IsLeftAsTheDatabaseNamedIt()
    {
        var conflict = Classify("IX_SomethingElse_Whatever").Should().BeOfType<DbConflictError>().Which;

        conflict.FieldName.Should().BeNull();
        conflict.ConstraintName.Should().Be("IX_SomethingElse_Whatever");
    }

    /// <summary>
    ///     The control the other way: a non-unique index of the same shape is not an answer to a
    ///     duplicate.
    /// </summary>
    [Fact]
    public void ANonUniqueIndex_IsNotOffered()
    {
        Classify("IX_People_City").Should().BeOfType<DbConflictError>()
            .Which.FieldName.Should().BeNull();
    }

    private static IError Classify(string constraintName)
    {
        var services = new ServiceCollection().AddPostgreSqlResultErrorHandling().BuildServiceProvider();

        using var db = new PeopleContext(new DbContextOptionsBuilder<PeopleContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseApplicationServiceProvider(services)
            .Options);

        var pg = new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", "23505",
            tableName: "People", constraintName: constraintName);

        return RuleViolationClassifier.Classify(db, new DbUpdateException("save failed", pg));
    }

    private sealed class Person
    {
        public int Id { get; set; }

        public string Email { get; set; } = "";

        public string TenantId { get; set; } = "";

        public string Code { get; set; } = "";

        public string City { get; set; } = "";
    }

    private sealed class PeopleContext(DbContextOptions<PeopleContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Person>(person =>
            {
                person.ToTable("People");
                person.HasKey(p => p.Id);
                person.HasIndex(p => p.Email).IsUnique();
                person.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
                // Not unique: a duplicate cannot have violated it, and offering it would be an answer
                // about the wrong column.
                person.HasIndex(p => p.City);
            });
        }
    }
}
