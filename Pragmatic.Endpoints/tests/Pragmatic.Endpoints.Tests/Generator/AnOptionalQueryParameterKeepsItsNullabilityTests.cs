using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A verb with no body puts the operation's values in the query string, and the handler it
///     generates has to declare them the way the action did.
/// </summary>
/// <remarks>
///     <para>
///         An optional <c>string?</c> declared as a non-nullable <c>string</c> on the generated handler,
///         while the value handed to it comes from <c>RequestValues.Query</c>, which returns
///         <c>string?</c>, makes the generated file warn about its own code — <c>CS8604</c> — which a
///         consumer building with <c>--warnaserror</c> cannot do anything about, because the file is
///         not theirs to change.
///     </para>
///     <para>
///         ⚠️ <b>The nullability is not in the type name.</b> <c>ToDisplayString(FullyQualifiedFormat)</c>
///         renders <c>string?</c> as <c>string</c> — for a reference type the <c>?</c> is an annotation,
///         not part of the type — so a template that passes the model's <c>TypeName</c> through loses
///         it. The model knows: <c>IsNullable</c> sits beside it, and the neighbouring
///         <c>QueryParameters</c> path re-applies the annotation, and so does this one.
///     </para>
///     <para>
///         The control is the reason the rule is not "make every optional parameter nullable". A
///         property with a real default — paging — is optional and <b>not</b> nullable, and widening it
///         would hand an <c>int?</c> to an <c>int</c>: the binding a few lines below chooses its branch
///         by reading exactly that.
///     </para>
///     <para>
///         ⚠️ Asserted on the generated text rather than on the compilation. This harness does not
///         reference <c>Pragmatic.Endpoints.AspNetCore</c>, <c>System.Threading.RateLimiting</c> or
///         <c>System.Private.Uri</c>, so a generated endpoint does not compile here at all and
///         <c>CS8604</c> cannot be read out of it. The end-to-end evidence is a consumer application
///         built from the package.
///     </para>
/// </remarks>
public class AnOptionalQueryParameterKeepsItsNullabilityTests : EndpointsGeneratorTestBase
{
    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Work;
        """;

    private static string Source(string properties) => $$"""
        {{Preamble}}

        [DomainAction]
        [Endpoint(HttpVerb.Get, "api/work/suggestions")]
        public partial class SuggestAction : DomainAction<Guid>
        {
            public required Guid TemplateId { get; init; }
        {{properties}}

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    /// <summary>The generated endpoint for an action carrying the given extra property.</summary>
    private static string EndpointFor(string properties)
        => GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(Source(properties)))
            .First(kv => kv.Key.Contains("SuggestAction.Endpoint"))
            .Value;

    /// <summary>
    ///     A nullable optional value is declared nullable on the handler.
    /// </summary>
    /// <remarks>
    ///     The binding assertion is half of it: it says the value flowing into that parameter really is
    ///     the raw query value, which is what makes the declared type wrong rather than merely narrow.
    /// </remarks>
    [Fact]
    public void ANullableOptionalQueryProperty_IsDeclaredNullableOnTheHandler()
    {
        var endpoint = EndpointFor("    public string? Term { get; init; }");

        endpoint.Should().Contain("string? term",
            "the handler declares what the action declared, and what is bound into it is nullable");

        endpoint.Should().Contain("var term = __raw_term;",
            "and the value it receives is the raw query value, which may be absent");
    }

    /// <summary>
    ///     The control: an optional value that is not nullable stays non-nullable.
    /// </summary>
    [Fact]
    public void AnOptionalQueryPropertyWithADefault_StaysNonNullable()
    {
        var endpoint = EndpointFor("    public int Page { get; init; } = 1;");

        endpoint.Should().Contain("int page",
            "a property with a real default is optional without being nullable");
        endpoint.Should().NotContain("int? page",
            "widening it would hand an int? to an int, which is why the two are told apart");
    }
}
