using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     PRAG0463 — an <c>[Invariant]</c> the generated invoker cannot call is reported as an <b>error</b>,
///     because a rule that cannot fire is not a weaker check: it is the absence of one.
/// </summary>
/// <remarks>
///     <para>
///         An <c>[Invariant]</c> written <c>private</c> — which is what a rule nobody calls from
///         outside the entity looks like — cannot be called by the generated check. Dropped without a
///         diagnostic, it lets a request that breaks the rule answer <b>201 Created</b>, and <c>grep</c>
///         for the method name across the generated files finds zero hits: the rule is in the source,
///         reads as enforced, and is checked on no path at all. Making the method <c>internal</c> is the
///         whole remedy, and the diagnostic is what says so.
///     </para>
///     <para>
///         ⚠️ <b>The accessibility is one of five silent exits, not the only one.</b> A shape filter
///         that runs <em>before</em> the attribute is read makes a method carrying <c>[Invariant]</c>
///         that is <c>static</c>, takes a parameter, returns something other than <c>bool</c>, or shares
///         a name with a rule already collected indistinguishable from a method nobody annotated. Each
///         case has its own test below, and each says which condition it failed — a diagnostic that only
///         said "cannot be called" would send the author looking at the accessibility of a method whose
///         real problem is its return type.
///     </para>
///     <para>
///         <b>Error and not warning</b>: an entity whose invariant cannot fire has a guarantee written
///         in its source that nothing keeps. An author who wants the method private wants it not to be
///         an invariant.
///     </para>
/// </remarks>
public class AnInvariantTheInvokerCannotCallTests
{
    /// <summary>
    ///     An aggregate with one rule, whose shape the caller chooses. Everything else is the smallest
    ///     thing that makes a mutation over an entity compile.
    /// </summary>
    private static string Model(string rule) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class CasesBoundary { }

            [Entity]
            [BelongsTo<CasesBoundary>]
            public partial class Case : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Subject { get; set; } = "";

                {{rule}}
            }

            [Mutation(Mode = MutationMode.Create)]
            public partial class OpenCaseMutation : Mutation<Case>
            {
                public required string Subject { get; init; }
            }
        }
        """;

    private static (string[] Ids, string[] Messages) Diagnostics(string rule)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model(rule));
        return (
            [.. diagnostics.Select(d => d.Id)],
            [.. diagnostics.Select(d => d.GetMessage())]);
    }

    private static string InvokerFor(string rule)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model(rule));
        // The hint is `…MutationInvoker`, read off the generated list rather than guessed: the first
        // version of this looked for "OpenCaseMutation.Invoker" and found nothing, which would have
        // passed for the wrong reason had the assertion been the other way round.
        var match = sources.FirstOrDefault(s => s.Key.Contains("OpenCaseMutation.MutationInvoker"));
        match.Value.Should().NotBeNull(
            $"the invoker is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }

    /// <summary>The setpoint: the shape the example actually had.</summary>
    [Fact]
    public void APrivateInvariant_IsReported_NamingTheAccessibility()
    {
        var (ids, messages) = Diagnostics("""
            [Invariant(nameof(SubjectIsSubstantial))]
                private bool SubjectIsSubstantial() => Subject.Trim().Length >= 10;
            """);

        ids.Should().Contain("PRAG0463",
            "a rule the invoker cannot call is checked on no path, and nothing said so");
        messages.Should().Contain(m => m.Contains("SubjectIsSubstantial") && m.Contains("private"),
            "the message names the method and which of the five conditions it failed");
    }

    /// <summary>
    ///     Control — the same entity with the method <c>internal</c> reports nothing, <b>and</b> the
    ///     invoker calls it. Both halves: a diagnostic that stopped firing because the rule stopped being
    ///     collected would pass the first assertion alone.
    /// </summary>
    [Fact]
    public void AnInternalInvariant_ReportsNothing_AndTheInvokerCallsIt()
    {
        const string rule = """
            [Invariant(nameof(SubjectIsSubstantial))]
                internal bool SubjectIsSubstantial() => Subject.Trim().Length >= 10;
            """;

        var (ids, _) = Diagnostics(rule);
        ids.Should().NotContain("PRAG0463");

        InvokerFor(rule).Should().Contain("SubjectIsSubstantial()",
            "which is the other half: the rule is enforced, not merely un-reported");
    }

    /// <summary>A static method has no entity to ask, and would be dropped by the same silent exit.</summary>
    [Fact]
    public void AStaticInvariant_IsReported()
    {
        var (ids, messages) = Diagnostics("""
            [Invariant(nameof(AlwaysTrue))]
                internal static bool AlwaysTrue() => true;
            """);

        ids.Should().Contain("PRAG0463");
        messages.Should().Contain(m => m.Contains("static"));
    }

    /// <summary>A rule with a parameter: the invoker has nothing to pass.</summary>
    [Fact]
    public void AParameterisedInvariant_IsReported()
    {
        var (ids, messages) = Diagnostics("""
            [Invariant(nameof(LongerThan))]
                internal bool LongerThan(int minimum) => Subject.Length >= minimum;
            """);

        ids.Should().Contain("PRAG0463");
        messages.Should().Contain(m => m.Contains("parameter"));
    }

    /// <summary>A rule that does not answer yes or no.</summary>
    [Fact]
    public void ANonBooleanInvariant_IsReported()
    {
        var (ids, messages) = Diagnostics("""
            [Invariant(nameof(SubjectLength))]
                internal int SubjectLength() => Subject.Length;
            """);

        ids.Should().Contain("PRAG0463");
        messages.Should().Contain(m => m.Contains("bool"));
    }

    /// <summary>
    ///     Control — an entity with no <c>[Invariant]</c> at all is silent. The diagnostic is about a
    ///     method that asked to be a rule, not about every parameterless method that is not one.
    /// </summary>
    [Fact]
    public void AMethodWithoutTheAttribute_IsNotReported()
    {
        var (ids, _) = Diagnostics("private bool JustAHelper() => Subject.Length > 0;");

        ids.Should().NotContain("PRAG0463");
    }
}
