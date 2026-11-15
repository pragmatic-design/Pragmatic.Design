using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     An <c>[Invariant]</c> with an <c>ErrorCode</c>, a <c>Member</c> and <c>Parameters</c> (#739): the
///     refusal carries them, and a name the refusal cannot report is <c>PRAG0471</c>.
/// </summary>
/// <remarks>
///     What the refusal answers over HTTP is proved in the Showcase (<c>InvariantTests</c>); these pin the
///     two things only the generator decides — which names are read off the row, and which are refused.
/// </remarks>
public class AnInvariantThatDeclaresItsRefusalTests
{
    private static string Model(string rule) => $$"""
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;

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
                private int Secret { get; set; }

                {{rule}}
            }

            [Mutation(Mode = MutationMode.Create)]
            public partial class OpenCaseMutation : Mutation<Case>
            {
                public required string Subject { get; init; }
            }
        }
        """;

    private static (string Invoker, string[] Ids, string[] Messages) Generate(string rule)
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model(rule));
        var invoker = sources.FirstOrDefault(s => s.Key.Contains("OpenCaseMutation.MutationInvoker")).Value;
        invoker.Should().NotBeNull($"the invoker is generated; generated: {string.Join(", ", sources.Keys)}");
        return (invoker, [.. diagnostics.Select(d => d.Id)], [.. diagnostics.Select(d => d.GetMessage())]);
    }

    [Fact]
    public void ARuleThatDeclaresItsRefusal_IsRefusedWithTheCodeTheMemberAndTheValues()
    {
        var (invoker, ids, _) = Generate("""
            [Invariant("The subject {Subject} is too short", ErrorCode = "CASE_SUBJECT_SHORT",
                    Member = nameof(Subject), Parameters = [nameof(Subject)])]
                internal bool SubjectIsSubstantial() => Subject.Length >= 10;
            """);

        ids.Should().NotContain("PRAG0471");
        invoker.Should().Contain("DeclaredCode = \"CASE_SUBJECT_SHORT\"");
        invoker.Should().Contain("Member = \"Subject\"");
        invoker.Should().Contain("[\"Subject\"] = (object?)entity.Subject",
            "the value is read off the row that broke the rule");
    }

    /// <summary>Control — a rule that declares none of them renders as it did before they existed.</summary>
    [Fact]
    public void ARuleThatDeclaresNothing_IsRefusedAsBefore()
    {
        var (invoker, _, _) = Generate("""
            [Invariant("Too short")]
                internal bool SubjectIsSubstantial() => Subject.Length >= 10;
            """);

        invoker.Should().Contain(
            "new global::Pragmatic.Actions.Mutation.InvariantViolationError(\"SubjectIsSubstantial\", \"Too short\");");
    }

    [Fact]
    public void AParameterTheInvokerCannotRead_IsReported_AndNotGenerated()
    {
        var (invoker, ids, messages) = Generate("""
            [Invariant("Secret {Secret}", Parameters = [nameof(Secret)])]
                internal bool SubjectIsSubstantial() => Subject.Length >= 10;
            """);

        ids.Should().Contain("PRAG0471");
        messages.Should().Contain(m => m.Contains("Secret") && m.Contains("getter at least internal"));
        invoker.Should().NotContain("entity.Secret", "a generated read of a private member would not compile");
    }

    [Fact]
    public void AMemberTheEntityDoesNotHave_IsReported()
    {
        var (_, ids, messages) = Generate("""
            [Invariant("Too short", Member = "Subjet")]
                internal bool SubjectIsSubstantial() => Subject.Length >= 10;
            """);

        ids.Should().Contain("PRAG0471");
        messages.Should().Contain(m => m.Contains("Subjet") && m.Contains("no property or field"));
    }
}
