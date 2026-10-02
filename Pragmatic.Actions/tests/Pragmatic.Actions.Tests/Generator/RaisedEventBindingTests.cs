using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     An event parameter that binds to nothing (PRAG0433).
/// </summary>
/// <remarks>
///     <para>
///         <c>[Raises&lt;TEvent&gt;]</c> matches the event's constructor parameters to the operation's
///         input properties by name. One that matches nothing was emitted as <c>default</c> — with a
///         comment in the generated file as its only trace — and the event was dispatched anyway. The
///         handler then read <c>Guid.Empty</c>, the write had happened, the event had arrived, and only
///         its contents were wrong.
///     </para>
///     <para>
///         The same shape is already reported for routes: <c>PRAG0504</c>, a <c>{placeholder}</c> with
///         no matching property. A name that does not match is nearly always a typo, and the
///         alternative to saying so is a value that looks deliberate.
///     </para>
/// </remarks>
public class RaisedEventBindingTests : ActionsGeneratorTestBase
{
    private static string Source(string eventParameters, string actionProperties) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authoring;
        using Pragmatic.Events;
        using Pragmatic.Result;

        namespace TestApp.Review;

        public sealed record TermApproved({{eventParameters}}) : IDomainEvent;

        [DomainAction]
        [Raises<TermApproved>]
        public partial class ApproveTermAction : VoidDomainAction
        {
        {{actionProperties}}

            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    [Fact]
    public void AParameterThatMatchesNothing_IsReported()
    {
        var result = RunGeneratorWithEntities(Source(
            "Guid TermId, string Reason",
            "    public required Guid TermId { get; init; }"));

        HasDiagnostic(result, "PRAG0433").Should().BeTrue(
            "Reason has no source, so the handler would read null and nothing would have failed");
    }

    [Fact]
    public void EveryParameterMatching_IsAccepted()
    {
        var result = RunGeneratorWithEntities(Source(
            "Guid TermId, string Reason",
            """
                public required Guid TermId { get; init; }
                public required string Reason { get; init; }
            """));

        HasDiagnostic(result, "PRAG0433").Should().BeFalse();
    }

    /// <summary>
    ///     <c>OccurredAt</c> is filled by the generator, so it is not an unmatched parameter.
    /// </summary>
    [Fact]
    public void TheTimestampParameter_IsNotReported()
    {
        var result = RunGeneratorWithEntities(Source(
            "Guid TermId, DateTimeOffset OccurredAt",
            "    public required Guid TermId { get; init; }"));

        HasDiagnostic(result, "PRAG0433").Should().BeFalse(
            "the generator supplies it, which is why it never matched an input property");
    }

    /// <summary>
    ///     And the value is still emitted, so the author sees the shape rather than a broken file.
    /// </summary>
    [Fact]
    public void TheUnmatchedArgumentIsStillGenerated()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithEntities(Source(
                "Guid TermId, string Reason",
                "    public required Guid TermId { get; init; }")),
            "ApproveTermAction.Invoker");

        generated.Should().Contain("unmatched: Reason",
            "the diagnostic points at it, and the generated file still compiles");
    }
}
