using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     A <c>[FromClock]</c> or <c>[FromCurrentUser]</c> property of a mutation, with Mapping writing the
///     body: written to the entity when the entity has a member of its name, and otherwise no input at all.
/// </summary>
/// <remarks>
///     Once the invoker wrote these properties, a mutation carrying one stopped compiling in
///     Time off: Mapping read <c>Now</c> as a DTO property the entity lacked (<c>PRAG0303</c>, and
///     <c>PRAG0414</c> from the mutation's own auto-map) — the rule for an input applied to what is not one.
/// </remarks>
public class ABoundMutationPropertyIsNoInputToMapTests
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;
        using Pragmatic.Temporal.Clock;

        namespace TestApp
        {
            [Boundary]
            public partial class LeaveBoundary { }

            [Entity]
            [BelongsTo<LeaveBoundary>]
            public partial class Request : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public DateOnly DecidedOn { get; set; }
                public DateTimeOffset? DecidedAt { get; private set; }

                public void Stamp(DateTimeOffset at) => DecidedAt = at;
            }

            [PragmaticDbContext("Leave")]
            public partial class LeaveDbContext { }

            [Mutation(Mode = MutationMode.Update)]
            public partial class DecideRequestMutation : Mutation<Request>
            {
                public required Guid Id { get; init; }

                [FromClock]
                public DateTimeOffset Now { get; private set; }

                [FromClock]
                public DateOnly DecidedOn { get; private set; }

                public override Task<Result<Request, IError>> ApplyAsync(Request entity, CancellationToken ct = default)
                {
                    entity.Stamp(Now);
                    return Task.FromResult(Result<Request, IError>.Success(entity));
                }
            }
        }
        """;

    [Fact]
    public void APropertyTheEntityLacks_IsNotReportedUnmapped()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Source);

        diagnostics.Where(d => d.Id is "PRAG0303" or "PRAG0414")
            .Select(d => d.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void APropertyTheEntityHas_IsWrittenToIt()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);

        var written = sources.Where(s => s.Key.Contains("DecideRequestMutation")).Select(s => s.Value).ToList();
        written.Should().Contain(s => s.Contains("DecidedOn"), "the entity has DecidedOn, and the mapping writes it");
        written.Should().NotContain(s => s.Contains("SetNow(") || s.Contains(".Now = this.Now"),
            "the entity has no Now: the body reads it");
    }

    /// <summary>The mutation, its mapping and its invoker compile.</summary>
    [Fact]
    public void ItCompiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Source, static path => path.Contains("DecideRequestMutation") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }
}
