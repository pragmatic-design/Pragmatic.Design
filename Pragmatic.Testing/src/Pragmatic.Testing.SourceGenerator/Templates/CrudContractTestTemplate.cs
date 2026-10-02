using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Templates;

/// <summary>
///     Emits CRUD round-trip contract tests for a boundary (#7, phase 2): a create test posts a body whose
///     fields are filled by the data synthesizer and asserts a 2xx. Field values come from
///     <see cref="Synthesis.TestDataSynthesizer"/>; foreign keys are filled in topological order upstream.
/// </summary>
internal sealed class CrudContractTestTemplate : CSharpTemplate
{
    private const string Identity = "global::Pragmatic.Testing.PragmaticTestIdentity";
    private const string Assert = "global::Pragmatic.Testing.PragmaticHttpAssertions";
    private const string Json = "global::System.Net.Http.Json.JsonContent";
    private const string HOST = "global::Pragmatic.Testing.PragmaticContractHost";
    private const string Fact = "global::Xunit.FactAttribute";
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Request = "global::System.Net.Http.HttpRequestMessage";
    private const string Verb = "global::System.Net.Http.HttpMethod";

    private readonly string _boundary;
    private readonly IReadOnlyList<CrudCreateModel> _creates;

    public CrudContractTestTemplate(string boundary, IReadOnlyList<CrudCreateModel> creates)
    {
        _boundary = boundary;
        _creates = creates;
    }

    protected override string? GeneratorName => "Pragmatic.Testing.SourceGenerator";

    public override Artifact RenderOutput() => new($"_ContractTests.{_boundary}.Crud.g.cs", ToSourceText());

    // Command creates are skipped in the body, so a boundary whose creates are all commands would otherwise
    // emit an empty test class.
    protected override bool Validate() => _creates.Any(c => !c.IsDomainActionCreate);

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Tests.Generated;");
        AppendLine();
        AppendLine("[global::Xunit.CollectionAttribute(\"PragmaticContractTests\")]");
        AppendLine($"public sealed partial class {_boundary}CrudContractTests : global::Pragmatic.Testing.PragmaticContractTestBase");
        AppendLine("{");
        IncreaseIndent();

        // Which host answers these: the boundary is in this class's own name, so the generator states it
        // instead of leaving a multi-service application to route by route prefix.
        AppendLine($"protected override string Boundary => \"{_boundary}\";");
        AppendLine();

        var first = true;
        foreach (var create in _creates)
        {
            // A command create is recorded only so a transition test can arrange its entity; it gets no CRUD
            // contract of its own, because a DomainAction may have business preconditions a synthesized body
            // does not meet and the test would fail for the wrong reason.
            if (create.IsDomainActionCreate)
                continue;

            if (!first)
                AppendLine();
            first = false;

            // Success + tenant-isolation POST a body; where no field could be filled the body is the
            // application's to supply, and the test says so when nobody does. Skipping them instead would
            // lose the isolation proof of every aggregate that carries its children.
            // The validation test POSTs {} and is always valid.
            RenderCreate(create);
            AppendLine();

            RenderValidation(create);

            if (create.IsTenantScoped)
            {
                AppendLine();
                RenderTenantIsolation(create);
            }
        }

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderCreate(CrudCreateModel create)
    {
        AppendLine($"[{Fact}]");
        AppendLine($"public async {Task} Create{create.OperationName}_WithValidBody_IsCreated()");
        AppendLine("{");
        IncreaseIndent();

        RenderBody(create);

        AppendLine($"using var request = new {Request}({Verb}.Post, \"{create.Route}\");");
        var permission = create.Permission is null ? "" : $", permissions: [\"{create.Permission}\"]";
        AppendLine($"{Identity}.AsUser(request, \"contract-{create.Permission ?? "noperm"}\"{permission});");
        AppendLine($"{HOST}.Prepare(request, \"{create.OperationName}\", Boundary);");
        // The synthesised body is built from the shape; a create whose validity needs more than the
        // shape gets its body from the application instead.
        AppendLine($"request.Content = {Json}.Create({HOST}.Body(\"{create.OperationName}\", body));");
        AppendLine("var response = await Client.SendAsync(request);");
        AppendLine($"{Assert}.ShouldBeSuccess(response);");

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     The body the request posts: the fields the synthesiser filled, or nothing at all when a
    ///     required member is one it cannot invent.
    /// </summary>
    /// <remarks>
    ///     <c>null!</c> rather than half an object: a body missing a required member is refused by the
    ///     application for the wrong reason, and the test would read as a broken contract instead of as a
    ///     missing body. <c>PragmaticContractHost.Body</c> throws naming the create, which is how the
    ///     consumer learns to write it.
    /// </remarks>
    private void RenderBody(CrudCreateModel create)
    {
        if (!create.CanSynthesizeBody)
        {
            AppendLine("object? body = null;");
            return;
        }

        AppendLine("var body = new");
        AppendLine("{");
        IncreaseIndent();
        foreach (var field in create.Fields)
            AppendLine($"{field.Name} = {field.ValueExpression},");
        DecreaseIndent();
        AppendLine("};");
    }

    /// <summary>An empty body omits the required fields, so a create must refuse it.</summary>
    /// <remarks>
    ///     The refusal is the contract, not its status code. Which of 400 and 422 comes back depends on
    ///     whether the missing value stops the object being constructed at all, and two creates in the
    ///     Showcase — both with a required field — answered differently to the same empty body. A test
    ///     generated for every create cannot predict that, and pinning either one would make half of
    ///     them assert a contract the API does not have.
    /// </remarks>
    private void RenderValidation(CrudCreateModel create)
    {
        AppendLine($"[{Fact}]");
        AppendLine($"public async {Task} Create{create.OperationName}_WithMissingRequiredFields_IsRejected()");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"using var request = new {Request}({Verb}.Post, \"{create.Route}\");");
        var permission = create.Permission is null ? "" : $", permissions: [\"{create.Permission}\"]";
        AppendLine($"{Identity}.AsUser(request, \"contract-{create.Permission ?? "noperm"}\"{permission});");
        AppendLine($"request.Content = {Json}.Create(new {{ }});");
        AppendLine("var response = await Client.SendAsync(request);");
        AppendLine($"{Assert}.ShouldBeRejected(response);");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>Tenant isolation: an entity created for one tenant must not be visible to another.</summary>
    private void RenderTenantIsolation(CrudCreateModel create)
    {
        var permission = create.Permission is null ? "" : $", permissions: [\"{create.Permission}\"]";

        AppendLine($"[{Fact}]");
        AppendLine($"public async {Task} Create{create.OperationName}_IsNotVisibleToAnotherTenant()");
        AppendLine("{");
        IncreaseIndent();

        RenderBody(create);

        AppendLine($"using var createRequest = new {Request}({Verb}.Post, \"{create.Route}\");");
        AppendLine($"{Identity}.AsUser(createRequest, \"contract-{create.Permission ?? "noperm"}\", \"tenant-a\", null{permission});");
        AppendLine($"{HOST}.Prepare(createRequest, \"{create.OperationName}\", Boundary);");
        AppendLine($"createRequest.Content = {Json}.Create({HOST}.Body(\"{create.OperationName}\", body));");
        AppendLine("var createResponse = await Client.SendAsync(createRequest);");
        AppendLine($"{Assert}.ShouldBeSuccess(createResponse);");

        AppendLine("var location = createResponse.Headers.Location?.ToString();");
        AppendLine("if (location is null) return; // cannot correlate the created resource without a Location header");

        // The reader may read: isolation is the 404 a caller who could read gets. With the create's
        // permission alone the read is refused before the lookup, and a 403 proves nothing about tenants.
        var readerPermissions = new[] { create.Permission, create.ReadPermission }
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .Select(p => $"\"{p}\"")
            .ToList();
        var readerPermission = readerPermissions.Count == 0 ? "" : $", permissions: [{string.Join(", ", readerPermissions)}]";

        AppendLine($"using var readRequest = new {Request}({Verb}.Get, location);");
        AppendLine($"{Identity}.AsUser(readRequest, \"contract-{create.Permission ?? "noperm"}\", \"tenant-b\", null{readerPermission});");
        AppendLine($"{HOST}.Prepare(readRequest, \"{create.OperationName}\", Boundary);");
        AppendLine("var readResponse = await Client.SendAsync(readRequest);");
        AppendLine($"{Assert}.ShouldBeNotFound(readResponse);");

        DecreaseIndent();
        AppendLine("}");
    }
}
