// Pragmatic.SourceGenerator - Composition - Pragmatic Invoke Endpoint Template

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates <c>MapPragmaticInvoke()</c> extension method that maps
///     <c>POST /_pragmatic/invoke</c> with a dispatch switch on ActionType
///     for all public local actions in this host.
/// </summary>
internal sealed class PragmaticInvokeEndpointTemplate : CSharpTemplate
{
    private readonly ImmutableArray<DiscoveredActionInfo> _localActions;
    private readonly string _rootNamespace;

    public PragmaticInvokeEndpointTemplate(
        ImmutableArray<DiscoveredActionInfo> localActions,
        string rootNamespace)
    {
        _localActions = localActions;
        _rootNamespace = rootNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput()
    {
        return new Artifact("_Infra.Remote.InvokeEndpoint.g.cs", ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsing("System.Text.Json");
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.AspNetCore.Http");
        AddUsing("Microsoft.AspNetCore.Routing");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Composition.Remote");

        AppendNamespace(_rootNamespace);
        AppendLine();

        XmlSummary("Generated dispatch endpoint for remote boundary invocations.");

        Class("PragmaticInvokeEndpoint", RenderClassBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderClassBody()
    {
        // Static dispatch table — O(1) action type lookup instead of O(n) switch
        AppendLine("private static readonly global::System.Collections.Generic.Dictionary<string, global::System.Func<JsonElement, global::System.IServiceProvider, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<IResult>>> __dispatchers = new()");
        AppendLine("{");
        IncreaseIndent();
        foreach (var action in _localActions.OrderBy(a => a.ActionType))
        {
            var actionFqn = $"global::{action.ActionType}";
            var escapedType = action.ActionType;
            if (action.IsVoid)
            {
                AppendLine($"[\"{escapedType}\"] = (payload, sp, ct) =>");
                AppendLine("{");
                IncreaseIndent();
                AppendLine($"var __action = payload.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<{actionFqn}>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options));");
                AppendLine("if (__action is null)");
                AppendLine($"    return InvalidPayload(\"{escapedType}\");");
                AppendLine("return PragmaticInvokeDispatcher.InvokeVoidActionAsync(");
                AppendLine("    __action,");
                AppendLine($"    sp.GetRequiredService<global::Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<{actionFqn}>>(),");
                AppendLine("    ct);");
                DecreaseIndent();
                AppendLine("},");
            }
            else
            {
                var returnType = action.ReturnType ?? "object";
                var invokerType =
                    $"global::Pragmatic.Actions.Invoker.IDomainActionInvoker<{actionFqn}, {returnType}>";

                AppendLine($"[\"{escapedType}\"] = (payload, sp, ct) =>");
                AppendLine("{");
                IncreaseIndent();
                AppendLine($"var __action = payload.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<{actionFqn}>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options));");
                AppendLine("if (__action is null)");
                AppendLine($"    return InvalidPayload(\"{escapedType}\");");

                if (FileResponseType.Is(action.ReturnType))
                {
                    // The success value is the file itself. Serializing it into the envelope would emit
                    // the record's shape and leave the bytes unread, so the body IS the file and the
                    // status code carries the outcome. Same helper as the local endpoint path, so
                    // Content-Type / Content-Disposition / ETag are identical across topologies.
                    //
                    // includeRpcMetadata: the caller is another Pragmatic host rebuilding the
                    // FileResponse, and Content-Disposition alone loses the file name of an inline file
                    // (there is no such header) together with the inline flag itself.
                    //
                    // The HttpContext argument stays null: it drives only the manual If-None-Match /
                    // If-Modified-Since checks, which belong to the host that faces the end user, not to
                    // this RPC hop. Range processing does NOT depend on it — the result reads the range
                    // off the HttpContext it is executed against, which is this request, carrying the
                    // Range that PragmaticRemoteRangeHandler propagated.
                    AppendLine($"return PragmaticInvokeDispatcher.InvokeFileActionAsync<{actionFqn}, {returnType}>(");
                    AppendLine("    __action,");
                    AppendLine($"    sp.GetRequiredService<{invokerType}>(),");
                    AppendLine(
                        "    static __file => global::Pragmatic.Endpoints.Extensions.FileResponseExtensions.ToResult(__file, null, includeRpcMetadata: true),");
                    AppendLine("    ct);");
                }
                else
                {
                    AppendLine($"return PragmaticInvokeDispatcher.InvokeActionAsync<{actionFqn}, {returnType}>(");
                    AppendLine("    __action,");
                    AppendLine($"    sp.GetRequiredService<{invokerType}>(),");
                    AppendLine("    ct);");
                }

                DecreaseIndent();
                AppendLine("},");
            }
        }
        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        // Shared 400 response for payloads that fail to deserialize into the target action type.
        AppendLine("private static global::System.Threading.Tasks.Task<IResult> InvalidPayload(string actionType)");
        IncreaseIndent();
        AppendLine("=> global::System.Threading.Tasks.Task.FromResult<IResult>(Results.BadRequest(new PragmaticInvokeResponse(false, null, new Microsoft.AspNetCore.Mvc.ProblemDetails");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("Status = 400,");
        AppendLine("Title = \"Invalid payload\",");
        AppendLine("Detail = $\"Payload for action type '{actionType}' could not be deserialized.\"");
        DecreaseIndent();
        AppendLine("})));");
        DecreaseIndent();
        AppendLine();

        XmlSummary("Maps <c>POST /_pragmatic/invoke</c> endpoint for remote action dispatch.");
        XmlParam("endpoints", "The endpoint route builder.");
        XmlReturns("The endpoint route builder for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IEndpointRouteBuilder", "endpoints")
        };

        Method("MapPragmaticInvoke", RenderMapBody, "IEndpointRouteBuilder", parameters,
            AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    private void RenderMapBody()
    {
        // A RequestDelegate, not a handler Delegate: ASP.NET binds the latter through a
        // reflection-based factory that does not survive an AOT publish.
        AppendLine("var handler = async (");
        AppendLine("    PragmaticInvokeRequest request,");
        AppendLine("    IServiceProvider sp,");
        AppendLine("    CancellationToken ct) =>");
        AppendLine("{");
        IncreaseIndent();

        Comment($"Static dispatch table for {_localActions.Length} actions — O(1) lookup via dictionary.");
        AppendLine("if (!__dispatchers.TryGetValue(request.ActionType, out var __dispatch))");
        Block(() =>
        {
            AppendLine("return Results.NotFound(new PragmaticInvokeResponse(false, null, new Microsoft.AspNetCore.Mvc.ProblemDetails");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("Status = 404,");
            AppendLine("Title = \"Action not found\",");
            AppendLine("Detail = $\"Action type '{request.ActionType}' is not registered on this host.\"");
            DecreaseIndent();
            AppendLine("}));");
        });
        AppendLine();
        AppendLine("return await __dispatch(request.Payload, sp, ct);");

        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        AppendLine("var __invoke = endpoints.MapPost(\"/_pragmatic/invoke\", (global::Microsoft.AspNetCore.Http.RequestDelegate)(async httpContext =>");
        Block(() =>
        {
            Comment("Resolved here rather than through Pragmatic.Endpoints: a host that exposes remote");
            Comment("boundaries does not necessarily reference the Endpoints package.");
            AppendLine("var __jsonOptions = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions");
            AppendLine("    .GetRequiredService<global::Microsoft.Extensions.Options.IOptions<global::Microsoft.AspNetCore.Http.Json.JsonOptions>>(");
            AppendLine("        httpContext.RequestServices).Value.SerializerOptions;");
            AppendLine("var __requestInfo = (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<PragmaticInvokeRequest>)");
            AppendLine("    __jsonOptions.GetTypeInfo(typeof(PragmaticInvokeRequest));");
            AppendLine("PragmaticInvokeRequest? __request;");
            AppendLine("try");
            AppendLine("{");
            AppendLine("    __request = await global::Microsoft.AspNetCore.Http.HttpRequestJsonExtensions.ReadFromJsonAsync(");
            AppendLine("        httpContext.Request,");
            AppendLine("        __requestInfo,");
            AppendLine("        httpContext.RequestAborted).ConfigureAwait(false);");
            AppendLine("}");
            AppendLine("catch (global::System.Text.Json.JsonException)");
            AppendLine("{");
            AppendLine("    httpContext.Response.StatusCode = 400;");
            AppendLine("    return;");
            AppendLine("}");
            AppendLine("if (__request is null)");
            AppendLine("{");
            AppendLine("    httpContext.Response.StatusCode = 400;");
            AppendLine("    return;");
            AppendLine("}");
            AppendLine();
            AppendLine("var __result = await handler(__request, httpContext.RequestServices, httpContext.RequestAborted).ConfigureAwait(false);");
            AppendLine("await __result.ExecuteAsync(httpContext).ConfigureAwait(false);");
        });
        AppendLine(")).ExcludeFromDescription();");
        AppendLine();
        Comment("Enforce the trust boundary. Fail-closed by default (requires an");
        Comment("authenticated principal); a trusted-network deployment opts out explicitly via");
        Comment("Pragmatic:RemoteBoundaries:InvokeEndpoint:AllowAnonymous.");
        AppendLine("global::Pragmatic.Composition.Remote.RemoteInvokeEndpointAuth.Apply(__invoke, endpoints.ServiceProvider);");
        AppendLine();
        AppendLine("return endpoints;");
    }
}
