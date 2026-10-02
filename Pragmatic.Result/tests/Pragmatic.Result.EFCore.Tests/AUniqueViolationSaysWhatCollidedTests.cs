using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Result.EntityFrameworkCore;
using Pragmatic.Result.EntityFrameworkCore.PostgreSQL;
using Pragmatic.Result.EntityFrameworkCore.Tests;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.EFCore.Tests;

/// <summary>
///     A duplicate insert answers with something the caller can act on.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ PostgreSQL does not fill <c>ColumnName</c> for SQLSTATE <c>23505</c>; it fills
///         <c>ConstraintName</c> and <c>TableName</c>. The parser asked for the one that is never there
///         and the classifier discarded the one that is, three lines above a foreign-key branch that
///         reads it correctly. So every duplicate answered "Duplicate value violates unique
///         constraint." and named nothing — on this repository's own default provider.
///     </para>
///     <para>
///         A constraint name is not a field name, and this is the half that can be answered without a
///         model: <b>nothing is discarded</b>. Turning it into the properties it covers needs the
///         model, and happens where the model is — <c>RuleViolationClassifier</c>.
///     </para>
/// </remarks>
public class AUniqueViolationSaysWhatCollidedTests
{
    private const string Constraint = "IX_Employees_WorkEmail";

    [Fact]
    public void TheConstraintNameSurvivesTheClassification()
    {
        var error = Classify(UniqueViolation());

        error.Should().BeOfType<DbConflictError>()
            .Which.ConstraintName.Should().Be(Constraint);
    }

    /// <summary>
    ///     And it reaches the caller as something rather than nothing, until the model can do better.
    /// </summary>
    [Fact]
    public void TheReasonNamesWhatWasViolated()
        => ((DbConflictError)Classify(UniqueViolation())).Reason.Should().Contain(Constraint);

    /// <summary>
    ///     The control: a violation that names no constraint still answers, and says only what it knows.
    /// </summary>
    /// <remarks>
    ///     Without it, "the constraint is named" is satisfied by a classifier that invents one — and a
    ///     provider that reports neither column nor constraint (or a heuristic parse of a message) is
    ///     the ordinary case on SQLite.
    /// </remarks>
    [Fact]
    public void WithNeitherColumnNorConstraint_TheAnswerIsStillAConflict()
    {
        var error = Classify(new PostgresException("duplicate key", "ERROR", "ERROR", "23505"));

        var conflict = error.Should().BeOfType<DbConflictError>().Which;
        conflict.ConstraintName.Should().BeNull();
        conflict.FieldName.Should().BeNull();
        conflict.Reason.Should().Be("Duplicate value violates unique constraint.");
    }

    /// <summary>
    ///     The control on the other side: the foreign-key branch is untouched. It already read the
    ///     constraint name, and naming what uses a row depends on it reaching <c>RuleViolationClassifier</c>.
    /// </summary>
    [Fact]
    public void TheForeignKeyBranchStillCarriesItsConstraint()
    {
        var pg = new PostgresException("violates foreign key", "ERROR", "ERROR", "23503",
            tableName: "LeaveRequests", constraintName: "FK_LeaveRequests_Employees_EmployeeId");

        Classify(pg).Should().BeOfType<DbConstraintError>()
            .Which.ConstraintName.Should().Be("FK_LeaveRequests_Employees_EmployeeId");
    }

    /// <summary>
    ///     What Npgsql builds from a real 23505: the constraint and the table named, and the column
    ///     left empty — which is why the constraint name has to survive.
    /// </summary>
    private static PostgresException UniqueViolation()
        => new("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505",
            tableName: "Employees", constraintName: Constraint);

    /// <summary>
    ///     Classifies through the application's registry, as a host does.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The application service provider is not decoration: without it the context resolves
    ///     <c>DbExceptionParserRegistry.Default</c> — the heuristic parser, which reads the message
    ///     text and never sees a constraint name. Omit it and the <b>control</b> fails too, which says
    ///     the harness is wrong rather than the classifier.
    /// </remarks>
    private static IError Classify(Exception inner)
    {
        var services = new ServiceCollection().AddPostgreSqlResultErrorHandling().BuildServiceProvider();

        using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseApplicationServiceProvider(services)
            .Options);

        return db.ClassifyRuleViolation(new DbUpdateException("save failed", inner));
    }
}
