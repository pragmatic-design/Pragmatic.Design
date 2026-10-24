using Pragmatic.Result.Database;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     The errors of a broken database rule are the core package's: a persistence engine other than EF Core answers with
///     the same records, without referencing EF Core.
/// </summary>
/// <remarks>
///     They lived in <c>Pragmatic.Result.EFCore</c>, which references EF Core, so the Sql engine's unit of work wrote copies
///     of them with the same code, status and members. This suite references only the core package.
/// </remarks>
public class TheDatabaseRuleErrorsNameNoEngineTests
{
    [Fact]
    public void TheThreeRecords_AreInTheCorePackage()
    {
        typeof(DbConflictError).Assembly.Should().BeSameAs(typeof(Error).Assembly);
        typeof(DbConstraintError).Assembly.Should().BeSameAs(typeof(Error).Assembly);
        typeof(DbInUseError).Assembly.Should().BeSameAs(typeof(Error).Assembly);
    }

    [Fact]
    public void AUniqueViolation_KeepsItsCodeStatusAndReason()
    {
        var conflict = DbConflictError.UniqueViolation("Amenity", constraintName: "IX_Amenities_Name");

        conflict.Code.Should().Be("DB_CONFLICT");
        conflict.StatusCode.Should().Be(409);
        conflict.Description.Should().Be("Duplicate value violates unique constraint IX_Amenities_Name.");
    }

    [Fact]
    public void ARefusedDelete_NamesTheEntityAndWhatUsesIt()
    {
        var inUse = new DbInUseError { EntityType = "Property", UsedBy = "Reservation" };

        inUse.Code.Should().Be("ENTITY_IN_USE");
        inUse.StatusCode.Should().Be(409);
        inUse.Description.Should().Be("The Property is still used by a Reservation and cannot be deleted.");
        new DbConstraintError { ConstraintType = "ForeignKey" }.StatusCode.Should().Be(400);
    }
}
