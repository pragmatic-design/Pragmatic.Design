using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An entity whose members are C# <c>required</c> still generates code that compiles.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Every entity gets a parameterless <c>Create()</c> beside the one that takes the required
///         values, so a caller that cannot see the signature — the mutation invoker, <c>ToEntity</c> —
///         does not have to predict it. Its comment says the required properties "stay at their
///         default, exactly as <c>new</c> left them", and for an ordinary property that is true.
///     </para>
///     <para>
///         For a <c>required</c> member it is not: <c>new T { }</c> without it is <b>CS9035</b>, an
///         error inside a file the author cannot edit. So an entity written the modern way could not
///         be declared at all, and the failure named the generated file. Found declaring the five
///         entities of <c>Pragmatic.Authorization.Management</c>, which are written exactly that way.
///     </para>
/// </remarks>
public class AnEntityWithRequiredMembersTests
{
    private const string Source = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Coupon : IEntity
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public Guid PersistenceId { get => Id; set => Id = value; }

            public required string Code { get; init; }

            public required string? Note { get; init; }
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }
        """;

    /// <summary>The generated create compiles.</summary>
    [Fact]
    public void TheGeneratedCreate_Compiles()
    {
        // The first bucket, not the second: the predicate selects the file under test, and reading the
        // other one would pass while the create does not compile at all.
        var (create, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Source, path => path.Contains("Coupon.Create"));

        create.Should().BeEmpty(
            "a required member the parameterless overload does not set is CS9035 — an error on code "
            + "the author cannot edit: " + TraitCompilationHarness.FormatErrors(create));
    }

    /// <summary>
    ///     And the required value is still a parameter of the create that takes them.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The control. "It compiles" is satisfied by an overload that assigns every required member
    ///     a default and asks the caller for nothing — which would turn a required value into a silent
    ///     empty string at every call site the generator writes.
    /// </remarks>
    [Fact]
    public void TheRequiredValue_IsStillAskedFor()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);

        var create = sources.FirstOrDefault(s => s.Key.Contains("Coupon.Create")).Value;

        create.Should().NotBeNull();
        create!.Should().Contain("string code", "the value the entity cannot do without is a parameter");
        create.Should().Contain("Code = code,");
    }
}
