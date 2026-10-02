using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     An entity whose name camel-cases to a C# keyword — <c>Case</c>, <c>Event</c>, <c>Lock</c>,
///     <c>Object</c> — is preloaded by a name the compiler accepts.
/// </summary>
/// <remarks>
///     <para>
///         <c>[LoadEntity&lt;Case&gt;]</c> emitted <c>var case = await …</c> and
///         <c>this._case = case;</c>, which are not C#: measured on Casework's <c>Case</c>,
///         28 compile errors in two generated files the author cannot edit. The setter's <b>parameter</b>
///         was escaped — <c>SetLoadedEntities(Case @case)</c> — because <c>CSharpTemplate</c> escapes the
///         parameter names it renders; the local and the assignment were composed as strings and were not.
///         One site out of four, which is how it stayed invisible: no application had an entity named
///         after a keyword.
///     </para>
///     <para>
///         ⚠️ The escape belongs on the identifier and <b>not</b> on the stem the derived names are built
///         from: <c>__@caseKey</c> is as broken as <c>case</c>, so the batch and optional paths compose
///         their locals from the unescaped stem and only the identifier itself carries the <c>@</c>.
///     </para>
/// </remarks>
public class AnEntityNamedLikeAKeywordTests
{
    private static string Model(string idType = "Guid") => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class IntakeBoundary { }

            [Entity]
            [BelongsTo<IntakeBoundary>]
            public partial class Case : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Subject { get; set; } = "";
            }

            [PragmaticDbContext("Intake")]
            public partial class IntakeDbContext { }

            [DomainAction]
            [LoadEntity<Case>(nameof(Id))]
            public partial class ReadTheSubjectAction : DomainAction<string>
            {
                public required {{idType}} Id { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<string, IError>>(_case!.Subject);
            }
        }
        """;

    /// <summary>
    ///     Both shapes: the load by a key that must be there, and the optional one a nullable key gives —
    ///     which composes a second local (<c>__…Key</c>) out of the same name and is the reason the
    ///     escape cannot live on the stem.
    /// </summary>
    [Theory]
    [InlineData("Guid")]
    [InlineData("Guid?")]
    public void TheLoadedEntity_Compiles(string idType)
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model(idType),
            static path => path.Contains("ReadTheSubjectAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>
    ///     And the identifier is the escaped one, at every site: the local, the hand-over, and the field
    ///     assignment inside the setter.
    /// </summary>
    /// <remarks>
    ///     Asserted on the text as well as on the compilation because a compile check alone would keep
    ///     passing if the generator renamed the local to something unrelated — the name a reader of the
    ///     generated file sees is the entity's, escaped, and not a mangled one.
    /// </remarks>
    [Fact]
    public void TheLocalAndTheHandover_AreTheEscapedIdentifier()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model());

        var invoker = sources.First(s => s.Key.Contains("ReadTheSubjectAction.Invoker")).Value;
        invoker.Should().Contain("var @case = await", "the local a keyword-named entity is read into")
            .And.Contain("action.SetLoadedEntities(@case)", "and the name it is handed over by");

        var load = sources.First(s => s.Key.Contains("ReadTheSubjectAction.LoadEntity")).Value;
        load.Should().Contain("this._case = @case;", "the assignment inside the setter, whose parameter was already escaped");
    }
}
