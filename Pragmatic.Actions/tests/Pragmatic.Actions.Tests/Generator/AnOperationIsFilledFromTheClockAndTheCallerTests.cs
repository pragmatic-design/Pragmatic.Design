using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[FromClock]</c> and <c>[FromCurrentUser]</c> on an action or a mutation are written by its
///     invoker, as on a query: after validation and authorization, before the preload and the body.
/// </summary>
/// <remarks>
///     Both attributes were read on a declared <c>[Query]</c> only. On an action or a mutation
///     they compiled and bound nothing — the property stayed <c>default</c>, and a decision stamped with
///     it read 0001-01-01 — so the examples injected <c>IClock</c> to read the time instead.
/// </remarks>
public class AnOperationIsFilledFromTheClockAndTheCallerTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Identity;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        using Pragmatic.Temporal.Clock;

        namespace TestApp.Leave
        {
            [Boundary]
            public partial class LeaveBoundary;

            public partial class Request : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string? Note { get; set; }
                public DateOnly DecidedOn { get; set; }
            }

            [DomainAction(Internal = false)]
            [BelongsTo<LeaveBoundary>]
            public partial class CloseDayAction : DomainAction<bool>
            {
                [FromClock]
                public DateOnly Today { get; private set; }

                [FromCurrentUser]
                public string CallerId { get; private set; } = "";

                public required string Reason { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }

            [Mutation(Mode = MutationMode.Update)]
            [BelongsTo<LeaveBoundary>]
            public partial class DecideRequestMutation : Mutation<Request>
            {
                public required Guid Id { get; init; }

                public string? Note { get; init; }

                [FromClock]
                public DateTimeOffset Now { get; private set; }

                [FromClock]
                public DateOnly DecidedOn { get; private set; }
            }

            [DomainAction(Internal = false)]
            [BelongsTo<LeaveBoundary>]
            public partial class PlanDayAction : DomainAction<bool>
            {
                public DateOnly Today { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
        }
        """;

    [Fact]
    public void AnAction_IsGivenTodayByTheClock()
        => Preparation("CloseDayAction.Invoker", "PrepareActionAsync(")
            .Should().Contain("action.Today = __clock.UtcToday;");

    [Fact]
    public void AMutation_IsGivenNowByTheClock()
        => Preparation("DecideRequestMutation.MutationInvoker", "PrepareMutationAsync(")
            .Should().Contain("mutation.Now = __clock.UtcNow;");

    [Fact]
    public void AnAction_IsGivenTheCallersId()
    {
        var preparation = Preparation("CloseDayAction.Invoker", "PrepareActionAsync(");

        preparation.Should().Contain("action.CallerId = __currentUser.Id;");
        preparation.Should().Contain("UnauthorizedError.Create()",
            "a caller who is not authenticated has no id to bind, and is refused as on a query");
    }

    /// <summary>The control: a property of the same name without the attribute is the caller's to send.</summary>
    [Fact]
    public void APlainPropertyOfTheSameName_IsStillAnInput()
    {
        var result = RunGeneratorWithEntities(Source);

        GetGeneratedSource(result, "PlanDayAction.Invoker")!
            .Should().NotContain("PrepareActionAsync(", "nothing is bound, so there is nothing to prepare");
        BoundaryOverload(result, "PlanDay").Should().Contain("DateOnly today");
    }

    [Fact]
    public void TheBoundaryOverload_DoesNotTakeWhatTheInvokerWrites()
    {
        var overload = BoundaryOverload(RunGeneratorWithEntities(Source), "CloseDay");

        overload.Should().Contain("reason");
        overload.Should().NotContain("DateOnly today");
        overload.Should().NotContain("callerId");
    }

    [Fact]
    public void TheGeneratedOperations_Compile()
    {
        var result = RunGeneratorWithEntities(Source);

        new[] { "PRAG0730", "PRAG0731", "PRAG0734", "PRAG0414" }.Where(id => HasDiagnostic(result, id))
            .Should().BeEmpty("a bound property is no input, so one the entity has no member for is not unmapped");
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();
    }

    /// <summary>
    ///     A bound property named after a member of the entity is written to it, after the invoker has
    ///     written the property: the day a decision is taken, stamped on the row without a line of body.
    /// </summary>
    [Fact]
    public void ABoundPropertyTheEntityHas_IsWrittenToIt()
    {
        var result = RunGeneratorWithEntities(Source);
        var written = GetGeneratedSourcesAsDictionary(result)
            .Where(s => s.Key.Contains("DecideRequestMutation", StringComparison.Ordinal))
            .Select(s => s.Value)
            .Where(s => s.Contains("SetDecidedOn(", StringComparison.Ordinal) || s.Contains("DecidedOn = ", StringComparison.Ordinal))
            .ToList();

        written.Should().NotBeEmpty("the mutation's DecidedOn maps to the entity's");
        written.Should().NotContain(s => s.Contains("SetNow(", StringComparison.Ordinal),
            "Now has no member on the entity, and is the body's to read");
    }

    [Fact]
    public void AClockBindingOfTheWrongType_IsReported()
        => Reports("""
            [FromClock]
            public int Hour { get; private set; }
            """, "PRAG0734").Should().BeTrue();

    [Fact]
    public void AClockBindingTheCallerCanSet_IsReported()
        => Reports("""
            [FromClock]
            public DateOnly Today { get; set; }
            """, "PRAG0734").Should().BeTrue();

    [Fact]
    public void ACurrentUserBindingTheCallerCanSet_IsReported()
        => Reports("""
            [FromCurrentUser]
            public string CallerId { get; init; } = "";
            """, "PRAG0730").Should().BeTrue();

    /// <summary>
    ///     The user entity reaches the action through the pipeline, as it reaches a query: without one in
    ///     the compilation, a member binding is reported rather than left unwritten.
    /// </summary>
    [Fact]
    public void AMemberBindingWithNoUserEntity_IsReported()
        => Reports("""
            [FromCurrentUser("Id")]
            public Guid EmployeeId { get; private set; }
            """, "PRAG0731").Should().BeTrue();

    [Fact]
    public void AMutationsBindingOfTheWrongType_IsReported()
        => HasDiagnostic(RunGeneratorWithEntities("""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Temporal.Clock;

            namespace TestApp.Leave;

            public partial class Request : IEntity
            {
                public Guid PersistenceId { get; set; }
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class DecideRequestMutation : Mutation<Request>
            {
                public required Guid Id { get; init; }

                [FromClock]
                public DateTime Now { get; private set; }
            }
            """), "PRAG0734").Should().BeTrue();

    /// <summary>Whether <paramref name="id" /> is reported for one binding declared on an action.</summary>
    private static bool Reports(string property, string id)
    {
        var result = RunGeneratorWithEntities($$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Identity;
            using Pragmatic.Result;
            using Pragmatic.Temporal.Clock;

            namespace TestApp.Leave;

            [DomainAction]
            public partial class BoundAction : DomainAction<bool>
            {
                {{property}}

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        return HasDiagnostic(result, id);
    }

    /// <summary>The boundary's overload for one operation: the line that declares it.</summary>
    private static string BoundaryOverload(SourceGenRunResult result, string operation)
    {
        var boundary = GetBoundarySource(result);
        boundary.Should().NotBeNull(
            $"the boundary is generated; generated: {string.Join(", ", GetGeneratedSourcesAsDictionary(result).Keys)}");

        var line = boundary!.Split('\n')
            .FirstOrDefault(l => l.Contains($" {operation}(", StringComparison.Ordinal)
                                 && !l.Contains($"{operation}Action action", StringComparison.Ordinal));
        line.Should().NotBeNull($"the boundary declares an overload of {operation} taking its inputs:\n{boundary}");
        return line!;
    }

    /// <summary>The body of the invoker's preparation method.</summary>
    private static string Preparation(string hint, string method)
    {
        var result = RunGeneratorWithEntities(Source);
        var invoker = GetGeneratedSource(result, hint);
        invoker.Should().NotBeNull(
            $"{hint} is generated at all; generated: {string.Join(", ", GetGeneratedSourcesAsDictionary(result).Keys)}");

        var start = invoker!.IndexOf(method, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{hint} prepares the operation:\n{invoker}");
        var end = invoker.IndexOf("\n    }", start, StringComparison.Ordinal);
        return end > start ? invoker[start..end] : invoker[start..];
    }
}
