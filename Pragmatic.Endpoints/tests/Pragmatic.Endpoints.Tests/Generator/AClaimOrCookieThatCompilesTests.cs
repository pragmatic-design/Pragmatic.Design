using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A <c>[FromClaim]</c> or <c>[FromCookie]</c> value on the <c>required … init</c> shape these classes
///     are written in has to compile, on every handler template.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Claims and cookies read, checked and assigned <em>after</em> the operation is built —
///         required values too — make a <c>required</c> property CS9035 at the <c>new</c>, and an
///         <c>init</c> one CS8852 at the assignment, both inside a generated file. Form fields, headers
///         and query values share the shape and the rule.
///     </para>
///     <para>
///         The reads and their 401/400 happen before the operation runs and ahead of its construction;
///         the values go into the initializer.
///     </para>
/// </remarks>
public class AClaimOrCookieThatCompilesTests : EndpointsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Claims;
        """;

    /// <summary>The values, on every shape: required, optional, typed with a declared default.</summary>
    private const string Values = """

            [FromClaim("sub")]
            public required string UserId { get; init; }

            [FromClaim("tenant", IsRequired = false)]
            public string? Tenant { get; init; }

            [FromClaim("level", IsRequired = false)]
            public int Level { get; init; } = 3;

            [FromCookie("session")]
            public required Guid Session { get; init; }

            [FromCookie("theme", IsRequired = false)]
            public string? Theme { get; init; }
        """;

    private const string DomainAction = Usings + """

        [DomainAction]
        [Endpoint(HttpVerb.Get, "api/whoami")]
        public partial class WhoAmIAction : DomainAction<string>
        {
        """ + Values + """

            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success(UserId));
        }
        """;

    private const string Mutation = Usings + """

        public class Profile : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; private set; } = "";

            internal void SetName(string value) => Name = value;
        }

        [Mutation(Mode = MutationMode.Update)]
        [Endpoint(HttpVerb.Put, "api/profiles/{id}")]
        public partial class RenameProfileMutation : Mutation<Profile>
        {
            public required Guid Id { get; init; }

            public required string Name { get; init; }
        """ + Values + """

        }
        """;

    private const string Endpoint = Usings + """

        public record WhoAmI(string UserId, int Level);

        [Endpoint(HttpVerb.Get, "api/whoami/raw")]
        public partial class WhoAmIEndpoint : Endpoint<WhoAmI>
        {
        """ + Values + """

            public override Task<Result<WhoAmI>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<WhoAmI>>(new WhoAmI(UserId, Level));
        }
        """;

    private const string Query = """
        using System;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp.Claims;

        [Entity]
        public partial class Note : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Text { get; private set; } = "";
        }

        public sealed class NoteDto
        {
            public Guid Id { get; init; }
        }

        [Query<Note, NoteDto>]
        [Endpoint(HttpVerb.Get, "api/my-notes")]
        public partial class MyNotesQuery
        {
        """ + Values + """

        }
        """;

    /// <summary>
    ///     The errors this defect produces, and no other: the test references are a deliberate subset of
    ///     what a consumer compiles against, so the whole compilation is never clean here.
    /// </summary>
    private static string Errors(SourceGenRunResult result)
        => string.Join("\n", GetCompilationErrors(result)
            .Where(e => e.Id is "CS9035" or "CS8852" or "CS1912" or "CS0019" or "CS0266" or "CS0029" or "CS0165" or "CS0103")
            .Select(e => e.ToString()));

    [Fact]
    public void ADomainAction_Compiles_WithEveryValueInTheInitializer()
    {
        var result = RunGenerator(DomainAction);

        Errors(result).Should().BeEmpty();

        var handler = GetGeneratedSource(result, "WhoAmIAction.Endpoint")!;
        handler.Should().Contain("UserId = userIdClaim", "a required claim is set where a required property can be set");
        handler.Should().Contain("Session = sessionCookieParsed", "a required typed cookie arrives as its own type");
        handler.Should().Contain("Tenant = tenantClaim ?? default!", "an absent optional claim leaves the default");
        handler.Should().Contain("Theme = themeCookie ?? default!");
        handler.Should().Contain(": 3", "an absent typed claim leaves the declared 3, not 0");
        handler.Should().NotContain("action.UserId =", "and nothing is assigned after construction");
    }

    [Fact]
    public void AMutation_Compiles()
    {
        Errors(RunGenerator(Mutation)).Should().BeEmpty();
    }

    [Fact]
    public void AnEndpoint_Compiles()
    {
        Errors(RunGenerator(Endpoint)).Should().BeEmpty();
    }

    /// <summary>
    ///     A query builds its instance in an object initializer of its own, apart from the other three
    ///     templates: the claims and cookies have to reach that one too.
    /// </summary>
    [Fact]
    public void AQuery_Compiles_WithEveryValueInTheInitializer()
    {
        var result = RunGeneratorWithPersistence(Query);

        Errors(result).Should().BeEmpty();

        var handler = GetGeneratedSourcesAsDictionary(result)
            .First(kv => kv.Key.Contains("MyNotesQuery.Endpoint")).Value;
        handler.Should().Contain("UserId = userIdClaim");
        handler.Should().Contain("Session = sessionCookieParsed");
        handler.Should().Contain("Tenant = tenantClaim ?? default!");
        handler.Should().Contain("Missing required claim: sub\", statusCode: 401");

        // A query binds its scalar properties from the query string by default; a claim or a cookie
        // is not one of them, or the caller could name the value and the claim would compete with it.
        handler.Should().NotContain("RequestValues.Query(httpContext, \"userId\")");
        handler.Should().NotContain("RequestValues.Query(httpContext, \"session\")");
    }

    /// <summary>
    ///     The control: the reads still refuse before anything runs — 401 for a missing required claim,
    ///     400 for a missing required cookie.
    /// </summary>
    [Fact]
    public void TheRefusals_AreStillThere()
    {
        var handler = GetGeneratedSource(RunGenerator(DomainAction), "WhoAmIAction.Endpoint")!;

        handler.Should().Contain("Missing required claim: sub\", statusCode: 401");
        handler.Should().Contain("Missing required cookie: session\", statusCode: 400");
        handler.Should().Contain("Invalid required cookie: session\", statusCode: 400");

        handler.IndexOf("Missing required claim: sub", StringComparison.Ordinal)
            .Should().BeLessThan(handler.IndexOf("new global::TestApp.Claims.WhoAmIAction", StringComparison.Ordinal),
                "the refusal comes before the operation is built");
    }

    /// <summary>A non-constant default on an optional init claim is PRAG0536, as for a header.</summary>
    [Fact]
    public void AnOptionalInitClaimWithANonConstantDefault_ReportsPRAG0536()
    {
        var result = RunGenerator(DomainAction.Replace(
            "public int Level { get; init; } = 3;",
            "public int Level { get; init; } = Environment.ProcessorCount;"));

        Errors(result).Should().BeEmpty("nothing that cannot compile is emitted");
        GetDiagnosticsById(result, "PRAG0536").Should().ContainSingle()
            .Which.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    /// <summary>The control: an optional claim on a <c>set</c> property is still assigned after construction.</summary>
    [Fact]
    public void AnOptionalSetClaim_IsStillAssignedAfterConstruction()
    {
        var result = RunGenerator(DomainAction.Replace(
            "public string? Tenant { get; init; }",
            "public string? Tenant { get; set; }"));

        Errors(result).Should().BeEmpty();
        GetGeneratedSource(result, "WhoAmIAction.Endpoint")!
            .Should().Contain("if (tenantClaim is not null) action.Tenant = tenantClaim;");
    }
}
