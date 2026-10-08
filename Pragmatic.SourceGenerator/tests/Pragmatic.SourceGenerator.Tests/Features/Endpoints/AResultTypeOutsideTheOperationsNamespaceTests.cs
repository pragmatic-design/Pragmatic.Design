using System;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Endpoints;

/// <summary>
///     An operation whose result DTO lives in another namespace: everything the generator writes about that
///     type names it <c>global::</c>-qualified, so the generated files compile without the application
///     putting the DTO's namespace in scope for them.
/// </summary>
/// <remarks>
///     The generated code cannot rely on the consumer's <c>global using</c>: a generated file has
///     its own usings, and a child namespace is not in scope from its parent, so a bare name resolves only
///     by the accident of the application's convenience file. Invoicing's <c>Dtos/</c> beside
///     <c>Actions/</c> — the ordinary layout — is the shape that finds it.
/// </remarks>
public class AResultTypeOutsideTheOperationsNamespaceTests
{
    private const string Model = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class RegistryBoundary { }

            [PragmaticDbContext("Registry")]
            public partial class RegistryDbContext { }
        }

        namespace TestApp.Entities
        {
            [Entity]
            [BelongsTo<TestApp.RegistryBoundary>]
            public partial class Customer : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = "";
            }
        }

        namespace TestApp.Dtos
        {
            using TestApp.Entities;

            public sealed class CallerDto
            {
                public string Id { get; init; } = "";
            }

            // The mapped read shape: its generated mapping, projection and extensions are the artifacts
            // the second claim is about.
            [MapFrom<Customer>]
            [GenerateProjection]
            public partial class CustomerDto
            {
                public string Name { get; set; } = "";
            }
        }

        namespace TestApp.Queries
        {
            using TestApp.Dtos;
            using TestApp.Entities;

            [Query<Customer, CustomerDto>(Single = true)]
            public partial class GetCustomerQuery
            {
                [Filter(MapTo = "PersistenceId")]
                public Guid Id { get; init; }
            }
        }

        namespace TestApp.Actions
        {
            // The using an author writes in the operation's own file. Nothing global: the generated files
            // never see it.
            using TestApp.Dtos;

            [DomainAction]
            [BelongsTo<TestApp.RegistryBoundary>]
            [Endpoint(HttpVerb.Get, "api/me")]
            public partial class WhoAmIAction : DomainAction<CallerDto>
            {
                public override Task<Result<CallerDto, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<CallerDto, IError>>(new CallerDto());
            }
        }
        """;

    /// <summary>
    ///     The four artifacts the issue measured, plus everything else the generator writes for this
    ///     operation: the DTO is named qualified, or not at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Only <c>CallerDto</c>, and deliberately. A mapped DTO's name is also a piece of half the
    ///     identifiers written about it — <c>CustomerDtoQueryExtensions</c>, <c>AsCustomerDto</c>,
    ///     <c>GetAsCustomerDtoAsync</c> — and a textual scan cannot tell a name from a type reference.
    ///     For those the oracle is the compiler, in <see cref="TheGeneratedFiles_Compile"/>: it is the
    ///     one reader that knows which spelling has to resolve.
    /// </remarks>
    [Fact]
    public void EveryGeneratedMentionOfTheDto_IsGloballyQualified()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model);

        var unqualified = new List<string>();
        foreach (var (hint, text) in sources.Where(s => IsCode(s.Key)))
        {
            // A file declared inside the DTO's own namespace names it bare and is self-contained — the
            // mapping, the projection and the extensions are written there. Only a file somewhere else
            // has to carry the namespace.
            if (hint.StartsWith("TestApp.Dtos.", StringComparison.Ordinal))
                continue;

            // Remove the qualified form first: whatever still says the name says it bare. A response writer's
            // method is named after the type it writes (Write_TestApp_Dtos_CallerDto): an identifier, not a type
            // reference, so it goes too — the compiler, in TheGeneratedFiles_Compile, is the oracle for that file.
            var rest = text.Replace("global::TestApp.Dtos.CallerDto", string.Empty, StringComparison.Ordinal);
            rest = System.Text.RegularExpressions.Regex.Replace(rest, @"\w+_CallerDto\w*", string.Empty);
            if (rest.Contains("CallerDto", StringComparison.Ordinal))
                unqualified.Add(hint);
        }

        unqualified.Should().BeEmpty(
            "a generated file names the result type unqualified: " + string.Join(", ", unqualified));
    }

    /// <summary>The control: the operation type beside it was always qualified, and still is.</summary>
    [Fact]
    public void TheOperationType_IsGloballyQualifiedToo()
        => TraitCompilationHarness.Generate(Model).Sources
            .Should().ContainKey("_Infra.Endpoints.EndpointContracts.g.cs")
            .WhoseValue.Should().Contain("typeof(global::TestApp.Actions.WhoAmIAction)");

    /// <summary>
    ///     And it compiles. This is the measurement the bug report is about — 16 × <c>CS0246</c>, most of
    ///     them inside generated files.
    /// </summary>
    [Fact]
    public void TheGeneratedFiles_Compile()
    {
        var (mine, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model,
            // ⚠️ `_Infra.Endpoints.Registration.g.cs` is deliberately outside this net: it emits rate-limiter
            // policies that need `System.Threading.RateLimiting`, which this harness's reference closure
            // does not carry. That is a gap in the harness, not in what this test measures, and it fails
            // the same way for a model with no DTO at all.
            static path => TheFilesThisTestOwns(path)
                           || path.EndsWith("TestSource.cs", StringComparison.Ordinal));

        mine.Should().BeEmpty(TraitCompilationHarness.FormatErrors(mine));
    }

    /// <summary>
    ///     The control, and the explanation of the sixteen <c>CS0246</c> the report was written from: take
    ///     the author's own <c>using</c> away and the base type <c>DomainAction&lt;CallerDto&gt;</c> is an
    ///     <b>error symbol</b>. An error symbol is still an <c>INamedTypeSymbol</c>, and its fully-qualified
    ///     display is the bare name with no namespace at all — so the generator writes what it was given,
    ///     everywhere, and one authoring error arrives as a page of failures inside generated files.
    /// </summary>
    /// <remarks>
    ///     This is why a <c>global using</c> made it go away: it fixed the hand-written file, and the
    ///     generated cascade with it. Qualifying the display format could not have — there is nothing to
    ///     qualify a name the compiler never resolved.
    /// </remarks>
    [Fact]
    public void WithoutTheAuthorsUsing_NoGeneratedFileNamesTheTypeAtAll()
    {
        var (sources, _) = TraitCompilationHarness.Generate(WithoutTheAuthorsUsing);

        sources.Where(s => IsCode(s.Key) && s.Value.Contains("CallerDto", StringComparison.Ordinal))
            .Select(s => s.Key)
            .Should().BeEmpty(
                "an unresolved type has no namespace to qualify with, so writing the name back out is "
                + "writing a name that cannot exist — into files the author cannot edit");
    }

    /// <summary>
    ///     And the page of errors that name is written into. One missing <c>using</c> is one
    ///     error in the author's own file; the generated files must not add to it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The cost is not the noise, it is the <b>wrong diagnosis</b>: a <c>CS0400</c> inside a
    ///         generated file sends the reader looking for a generator bug, and such reports land, in good
    ///         faith, against templates that are already correct.
    ///     </para>
    ///     <para>
    ///         The same file net as <see cref="TheGeneratedFiles_Compile" />, so the two say the same
    ///         thing about the same files: with the <c>using</c>, they compile; without it, they stay out
    ///         of the way.
    ///     </para>
    /// </remarks>
    [Fact]
    public void WithoutTheAuthorsUsing_TheGeneratedFilesAddNothingToTheAuthorsOwnError()
    {
        var (mine, theirs) = TraitCompilationHarness.CompileAndSplitErrors(
            WithoutTheAuthorsUsing, TheFilesThisTestOwns);

        mine.Should().BeEmpty(TraitCompilationHarness.FormatErrors(mine));
        theirs.Should().Contain(
            d => d.Id == "CS0246" && d.Location.SourceTree?.FilePath == "TestSource.cs",
            "the one error that is the cause is still reported, in the file that has it");
    }

    /// <summary>
    ///     And the generator says which operation it could not write, so the reader is sent to the file
    ///     that has the error rather than to the generated one that inherited it.
    /// </summary>
    [Fact]
    public void WithoutTheAuthorsUsing_TheGeneratorNamesTheTypeItCouldNotResolve()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(WithoutTheAuthorsUsing);

        var reported = diagnostics.Where(d => d.Id == "PRAG9001").ToList();

        reported.Should().NotBeEmpty("the operation's result type does not resolve");
        reported.Select(d => d.GetMessage()).Should()
            .Contain(m => m.Contains("CallerDto", StringComparison.Ordinal)
                          && m.Contains("WhoAmIAction", StringComparison.Ordinal),
                "the message names the type and the operation it belongs to");
    }

    /// <summary>The control: with the author's using in place nothing is reported.</summary>
    [Fact]
    public void WithTheAuthorsUsing_NothingIsReported()
        => TraitCompilationHarness.Generate(Model).GeneratorDiagnostics
            .Should().NotContain(d => d.Id == "PRAG9001");

    /// <summary>The model with the <c>using</c> the author writes in the operation's own file removed.</summary>
    private static string WithoutTheAuthorsUsing
        => Model.Replace("using TestApp.Dtos;", string.Empty, StringComparison.Ordinal);

    /// <summary>
    ///     The generated files this test speaks for — the same net as <see cref="TheGeneratedFiles_Compile" />.
    /// </summary>
    private static bool TheFilesThisTestOwns(string path)
        => path.Contains("WhoAmIAction", StringComparison.Ordinal)
           || path.Contains("GetCustomerQuery", StringComparison.Ordinal)
           || path.Contains("TestApp.Dtos.", StringComparison.Ordinal)
           || path.Contains("Customer.Projections", StringComparison.Ordinal)
           || path.Contains("_Boundary.", StringComparison.Ordinal)
           || path.Contains("_Infra.Endpoints.EndpointContracts", StringComparison.Ordinal)
           || path.Contains("_Infra.Serialization.Utf8ResponseWriters", StringComparison.Ordinal)
           || path.Contains("_Infra.Actions.", StringComparison.Ordinal);

    private static bool IsCode(string hintName)
        // The manifest is JSON inside a string literal: it carries a `simpleName` by design, and a name
        // inside a string is not something the compiler resolves.
        => !hintName.Contains("PragmaticManifest", StringComparison.Ordinal);
}
