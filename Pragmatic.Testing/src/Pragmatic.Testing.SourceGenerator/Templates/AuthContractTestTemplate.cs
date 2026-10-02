using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Templates;

/// <summary>
///     Emits the authorization contract tests for a boundary's endpoints (#7, phase 1): for each endpoint that
///     requires a permission, a test asserting a caller WITHOUT it does not get through and one asserting a
///     caller WITH it is not blocked by authorization. Generated against the <c>Pragmatic.Testing</c> harness.
///     <para>
///         The assertions are deliberately wider than "403" / "2xx": Pragmatic's pipeline can reject before the
///         authorization filter runs (400 on body binding, 404 on entity lookup), and an authorized call may
///         still legitimately end in 404 for a random id. See <c>ShouldBeRejected</c> / <c>ShouldNotBeForbidden</c>.
///     </para>
///     <para>
///         The unprivileged caller is built with <c>AsUser(request, "contract-noperm")</c>, which writes an empty
///         permission header so the shared fixture's default grant cannot leak into the call.
///     </para>
/// </summary>
internal sealed class AuthContractTestTemplate : CSharpTemplate
{
    private const string Identity = "global::Pragmatic.Testing.PragmaticTestIdentity";
    private const string Assert = "global::Pragmatic.Testing.PragmaticHttpAssertions";
    private const string Fact = "global::Xunit.FactAttribute";
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Request = "global::System.Net.Http.HttpRequestMessage";
    private const string Verb = "global::System.Net.Http.HttpMethod";
    private const string HOST = "global::Pragmatic.Testing.PragmaticContractHost";

    private readonly string _boundary;
    private readonly IReadOnlyList<EndpointContractModel> _endpoints;

    public AuthContractTestTemplate(string boundary, IReadOnlyList<EndpointContractModel> endpoints)
    {
        _boundary = boundary;
        _endpoints = endpoints;
    }

    protected override string? GeneratorName => "Pragmatic.Testing.SourceGenerator";

    public override Artifact RenderOutput() => new($"_ContractTests.{_boundary}.Auth.g.cs", ToSourceText());

    protected override bool Validate() => _endpoints.Count > 0;

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Tests.Generated;");
        AppendLine();
        AppendLine("[global::Xunit.CollectionAttribute(\"PragmaticContractTests\")]");
        AppendLine($"public sealed partial class {_boundary}AuthContractTests : global::Pragmatic.Testing.PragmaticContractTestBase");
        AppendLine("{");
        IncreaseIndent();

        // Which host answers these: the boundary is in this class's own name, so the generator states it
        // instead of leaving a multi-service application to route by route prefix.
        AppendLine($"protected override string Boundary => \"{_boundary}\";");
        AppendLine();

        var first = true;
        foreach (var endpoint in _endpoints)
        {
            if (!endpoint.RequiresPermission)
                continue;

            if (!first)
                AppendLine();
            first = false;

            var isGet = string.Equals(endpoint.HttpMethod, "Get", System.StringComparison.OrdinalIgnoreCase);
            var hasRouteParam = endpoint.Route.Contains("{");

            // The "rejected" contract only holds where denial is observable: mutations and GET-by-id return 4xx.
            // A GET list/search (no route param) data-scopes — it returns 200 with filtered results, not a
            // rejection — so asserting 4xx there would be wrong; skip it (visibility is covered by scope tests).
            if (!isGet || hasRouteParam)
            {
                RenderWithoutPermission(endpoint);
                AppendLine();
            }

            RenderWithPermission(endpoint);

            // A GET-by-id with an unknown id must be 404 — and a route parameter alone does not make one.
            // A sub-collection (`/api/invoices/{id}/payments`) answers 200 with an empty list, which is
            // the right answer and not a missing 404.
            if (isGet && hasRouteParam && !endpoint.AnswersWithACollection)
            {
                AppendLine();
                RenderNotFound(endpoint);
            }
        }

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderWithoutPermission(EndpointContractModel endpoint)
    {
        AppendLine($"[{Fact}]");
        AppendLine($"public async {Task} {endpoint.ActionName}_WithoutRequiredPermission_IsRejected()");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"using var request = new {Request}({Verb}.{endpoint.HttpMethod}, {RandomizedRoute(endpoint.Route)});");
        AppendLine($"{Identity}.AsUser(request, \"contract-noperm\");");
        AppendLine($"{HOST}.Prepare(request, \"{endpoint.ActionName}\", Boundary);");
        AppendLine("var response = await Client.SendAsync(request);");
        // The invariant is that an unprivileged caller does not succeed. Pragmatic may reject at 403, or earlier
        // (400 body binding, 404 entity lookup) before the authorization filter; any 4xx satisfies the contract.
        AppendLine($"{Assert}.ShouldBeRejected(response);");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     The privileged half of the pair — and it sends the unprivileged request too, because the
    ///     pair is the measurement.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Asserting "not forbidden" on the privileged call alone passes when something refuses every
    ///     request before it reaches a route: the unprivileged one is a 400, which counts as rejected,
    ///     and the privileged one is the same 400, which counts as not forbidden. Fifty-four generated
    ///     tests reported success on exactly that. Two identical statuses now fail.
    /// </remarks>
    private void RenderWithPermission(EndpointContractModel endpoint)
    {
        AppendLine($"[{Fact}]");
        AppendLine($"public async {Task} {endpoint.ActionName}_WithRequiredPermission_IsReachable()");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"using var denied = new {Request}({Verb}.{endpoint.HttpMethod}, {RandomizedRoute(endpoint.Route)});");
        AppendLine($"{Identity}.AsUser(denied, \"contract-noperm\");");
        AppendLine($"{HOST}.Prepare(denied, \"{endpoint.ActionName}\", Boundary);");
        AppendLine("var deniedResponse = await Client.SendAsync(denied);");
        AppendLine();
        AppendLine($"using var request = new {Request}({Verb}.{endpoint.HttpMethod}, {RandomizedRoute(endpoint.Route)});");
        AppendLine($"{Identity}.AsUser(request, \"contract-{endpoint.Permission}\", permissions: [\"{endpoint.Permission}\"]);");
        AppendLine($"{HOST}.Prepare(request, \"{endpoint.ActionName}\", Boundary);");
        AppendLine("var response = await Client.SendAsync(request);");
        // Authorization passed AND it made a difference: the same request without the permission was
        // answered differently. Identical statuses mean nothing was measured.
        AppendLine($"{Assert}.ShouldBeAuthorizedUnlike(response, deniedResponse);");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderNotFound(EndpointContractModel endpoint)
    {
        AppendLine($"[{Fact}]");
        AppendLine($"public async {Task} {endpoint.ActionName}_WithUnknownId_IsNotFound()");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"using var request = new {Request}({Verb}.Get, {RandomizedRoute(endpoint.Route)});");
        var permission = endpoint.Permission is null ? "" : $", permissions: [\"{endpoint.Permission}\"]";
        AppendLine($"{Identity}.AsUser(request, \"contract-{endpoint.Permission ?? "noperm"}\"{permission});");
        AppendLine($"{HOST}.Prepare(request, \"{endpoint.ActionName}\", Boundary);");
        AppendLine("var response = await Client.SendAsync(request);");
        AppendLine($"{Assert}.ShouldBeNotFound(response);");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Turns a route template into a string expression with each {param} replaced by a random id. Uses
    ///     concatenation, not interpolation: <c>global::</c> as the first token inside an interpolation hole does
    ///     not compile (CS0103), so <c>"/api/x/" + global::System.Guid.NewGuid() + ""</c> is emitted instead.
    /// </summary>
    private static string RandomizedRoute(string route)
    {
        var replaced = System.Text.RegularExpressions.Regex.Replace(
            route, "\\{[^}]+\\}", "\" + global::System.Guid.NewGuid() + \"");
        return $"\"{replaced}\"";
    }
}
