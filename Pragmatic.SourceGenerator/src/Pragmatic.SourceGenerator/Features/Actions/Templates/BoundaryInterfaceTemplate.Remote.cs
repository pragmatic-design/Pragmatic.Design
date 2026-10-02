using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Renders the remote (HTTP) implementation class and AddRemote() DI method.
///     Self-contained: uses only HttpClient + System.Text.Json (no Composition.Host dependency).
/// </summary>
internal sealed partial class BoundaryInterfaceTemplate
{
    private void RenderRemoteImplementation()
    {
        var implementedInterface = _boundary.InterfaceName;
        var remoteName = StripBoundarySuffix(_boundary.TypeName) + "RemoteActions";

        XmlSummary(
            $"Remote HTTP implementation of <see cref=\"{_boundary.InterfaceName}\"/>. " +
            "Invokes actions via POST /_pragmatic/invoke on the remote host.");

        // Remote class implements root + all sub-boundary interfaces (methods are flat)
        var interfaces = new List<string> { implementedInterface };
        if (_hasSubBoundaries)
        {
            foreach (var sub in _boundary.SubBoundaries)
                interfaces.Add(sub.InterfaceName);
        }

        Class(remoteName, () => RenderRemoteImplBody(remoteName),
            interfaces: interfaces,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderRemoteImplBody(string remoteName)
    {
        AddUsing("System.Net.Http");
        AddUsing("System.Net.Http.Json");
        AddUsing("System.Text.Json");

        // Fields
        Field("_httpClient", "global::System.Net.Http.HttpClient", AccessModifier.Private, isReadOnly: true);
        Field("_json", "global::System.Text.Json.JsonSerializerOptions", AccessModifier.Private, isReadOnly: true);
        AppendLine();

        // Constructor
        var moduleName = StripBoundarySuffix(_boundary.TypeName);
        var clientName = $"Pragmatic.Remote.{moduleName}";
        Constructor(remoteName, () =>
        {
            AppendLine($"_httpClient = factory.CreateClient(\"{clientName}\");");
            Comment("The shared seam, so a remote call uses the same contexts and converters as the");
            Comment("rest of the app — and so it works with the reflection fallback disabled.");
            AppendLine("_json = (json ?? global::Pragmatic.Serialization.PragmaticJsonOptions.Default).Build();");
        }, new List<MethodParameter>
        {
            new("global::System.Net.Http.IHttpClientFactory", "factory"),
            new("global::Pragmatic.Serialization.PragmaticJsonOptions", "json", nullable: true)
                { DefaultValue = "null" }
        }, AccessModifier.Public);

        // Sub-boundary accessors — remote class implements all sub-interfaces, return this
        if (_hasSubBoundaries)
        {
            foreach (var sub in _boundary.SubBoundaries)
            {
                XmlInheritDoc();
                AppendLine($"public {sub.InterfaceName} {sub.PropertyName} => this;");
                AppendLine();
            }
        }

        // Methods — root public members + sub-boundary members (all flat via HTTP)
        foreach (var member in _publicMembers)
            RenderRemoteMethod(member);

        if (_hasSubBoundaries)
        {
            foreach (var sub in _boundary.SubBoundaries)
            {
                foreach (var member in sub.PublicMembers)
                    RenderRemoteMethod(member);
            }
        }
    }

    private void RenderRemoteMethod(BoundaryMemberModel member)
    {
        var methodName = DeriveMethodName(member);
        var returnType = GetMethodReturnType(member);
        var paramName = member.IsMutation ? "mutation" : "action";
        var actionFqn = StripGlobalPrefix(member.FullTypeName);

        // A FileResponse cannot survive the JSON envelope — it is streamed, not serialized.
        var isFileResponse = !member.IsVoid && FileResponseType.Is(member.ReturnTypeName);

        // Primary overload — serializes and sends via HTTP
        XmlInheritDoc();
        Method(methodName, () =>
        {
            if (isFileResponse)
            {
                RenderRemoteFileBody(paramName, actionFqn, member.FullTypeName);
                return;
            }

            AppendLine($"var payload = global::System.Text.Json.JsonSerializer.SerializeToElement({paramName}, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<{member.FullTypeName}>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options));");
            AppendLine($"var requestBody = new global::Pragmatic.Serialization.PragmaticInvokeEnvelope(\"{actionFqn}\", payload);");
            AppendLine($"var response = await _httpClient.PostAsJsonAsync(\"/_pragmatic/invoke\", requestBody, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<global::Pragmatic.Serialization.PragmaticInvokeEnvelope>(_json), ct).ConfigureAwait(false);");
            AppendLine($"var json = await response.Content.ReadFromJsonAsync(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<global::System.Text.Json.JsonElement>(_json), ct).ConfigureAwait(false);");
            AppendLine();
            AppendLine("var isSuccess = json.TryGetProperty(\"isSuccess\", out var successProp) && successProp.GetBoolean();");

            if (member.IsVoid)
            {
                AppendLine("if (!isSuccess)");
                AppendLine("{");
                IncreaseIndent();
                RenderRemoteErrorExtraction();
                AppendLine("return global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Failure(error);");
                DecreaseIndent();
                AppendLine("}");
                AppendLine("return global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Success();");
            }
            else
            {
                var resultType = member.ReturnTypeName ?? "object";
                AppendLine("if (!isSuccess)");
                AppendLine("{");
                IncreaseIndent();
                RenderRemoteErrorExtraction();
                AppendLine($"return global::Pragmatic.Result.Result<{resultType}, global::Pragmatic.Result.IError>.Failure(error);");
                DecreaseIndent();
                AppendLine("}");
                AppendLine("if (!json.TryGetProperty(\"value\", out var valueProp) || valueProp.ValueKind == global::System.Text.Json.JsonValueKind.Null)");
                AppendLine("{");
                IncreaseIndent();
                RenderRemoteDeserializationError("remote response did not contain a non-null 'value' element");
                AppendLine($"return global::Pragmatic.Result.Result<{resultType}, global::Pragmatic.Result.IError>.Failure(error);");
                DecreaseIndent();
                AppendLine("}");
                // The JsonValueKind.Null guard above already rejects a null payload; deserializing a
                // non-null token yields a non-null value (or throws). No second null-check here — it would
                // be invalid for a non-nullable value-type result (e.g. Guid).
                AppendLine($"var value = valueProp.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<{resultType}>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options));");
                AppendLine($"return global::Pragmatic.Result.Result<{resultType}, global::Pragmatic.Result.IError>.Success(value!);");
            }
        },
        returnType,
        new List<MethodParameter>
        {
            new(member.FullTypeName, paramName),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        },
        AccessModifier.Public,
        new MethodModifiers { IsAsync = true });

        // Unwrapped overload
        if (member.HasInputProperties)
        {
            XmlInheritDoc();
            var unwrappedParams = BuildUnwrappedMethodParams(member);
            var varName = member.IsMutation ? "__mutation" : "__action";

            Method(methodName, () =>
            {
                AppendLine($"var {varName} = new {member.FullTypeName}");
                AppendLine("{");
                IncreaseIndent();
                foreach (var prop in member.InputProperties)
                    AppendLine($"{prop.Name} = {TemplateHelpers.ToCamelCase(prop.Name)},");
                DecreaseIndent();
                AppendLine("};");
                AppendLine($"return {methodName}({varName}, ct);");
            }, returnType, unwrappedParams, AccessModifier.Public);
        }
    }

    /// <summary>
    ///     Renders the body of a remote action whose success value is a <c>FileResponse</c>: the remote
    ///     host streams the bytes back instead of the JSON envelope, so the body is read as a stream.
    /// </summary>
    private void RenderRemoteFileBody(string paramName, string actionFqn, string actionTypeName)
    {
        var fileResult =
            $"global::Pragmatic.Result.Result<{FileResponseType.FullyQualifiedName}, global::Pragmatic.Result.IError>";

        AppendLine($"var payload = global::System.Text.Json.JsonSerializer.SerializeToElement({paramName}, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<{actionTypeName}>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options));");
        AppendLine($"var requestBody = new global::Pragmatic.Serialization.PragmaticInvokeEnvelope(\"{actionFqn}\", payload);");
        AppendLine(
            "using var request = new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Post, \"/_pragmatic/invoke\")");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("Content = global::System.Net.Http.Json.JsonContent.Create(requestBody),");
        DecreaseIndent();
        AppendLine("};");
        AppendLine();
        AppendLine("// ResponseHeadersRead: the default completion option buffers the whole file into memory");
        AppendLine("// before returning, which defeats the streaming and does not survive large downloads.");
        AppendLine(
            "var response = await _httpClient.SendAsync(request, global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);");
        AppendLine();

        // Failure: the file path signals the outcome with the status code, because the body is the file.
        // Success spans the whole 2xx range, not just 200: a propagated Range makes the remote answer
        // 206, whose body is the requested slice and whose Content-Range the FileResponse carries back
        // (FileResponse.PartialContent). A 416 lands here instead, as the failure it is.
        AppendLine("if (!response.IsSuccessStatusCode)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var json = default(global::System.Text.Json.JsonElement);");
        AppendLine("try");
        AppendLine("{");
        IncreaseIndent();
        AppendLine(
            $"json = await response.Content.ReadFromJsonAsync(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<global::System.Text.Json.JsonElement>(_json), ct).ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine("}");
        AppendLine(
            "catch (global::System.Exception ex) when (ex is global::System.Text.Json.JsonException or global::System.NotSupportedException)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("// A proxy or gateway answered instead of the boundary: keep the status, drop the body.");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        AppendLine("var errorCode = \"REMOTE_ERROR\";");
        AppendLine("var errorTitle = \"Remote invocation failed\";");
        AppendLine("var errorStatus = (int)response.StatusCode;");
        AppendLine("if (json.ValueKind == global::System.Text.Json.JsonValueKind.Object &&");
        AppendLine("    json.TryGetProperty(\"error\", out var errorProp) &&");
        AppendLine("    errorProp.ValueKind == global::System.Text.Json.JsonValueKind.Object)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (errorProp.TryGetProperty(\"title\", out var t)) errorTitle = t.GetString() ?? errorTitle;");
        AppendLine("if (errorProp.TryGetProperty(\"status\", out var s)) errorStatus = s.GetInt32();");
        AppendLine("if (errorProp.TryGetProperty(\"code\", out var c)) errorCode = c.GetString() ?? errorCode;");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        AppendLine("response.Dispose();");
        AppendLine("var error = new " + StripBoundarySuffix(_boundary.TypeName) +
                   "RemoteError(errorCode, errorStatus, errorTitle);");
        AppendLine($"return {fileResult}.Failure(error);");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();

        // Success: ownership of the response moves into the returned stream.
        AppendLine("// The response is NOT disposed here: its content stream is what the caller reads, and");
        AppendLine("// it dies with the response. Ownership moves into the FileResponse — disposing that");
        AppendLine("// (or its Content) releases the response, and nothing else may.");
        AppendLine(
            "var file = await global::Pragmatic.Endpoints.Responses.RemoteFileResponse.ReadAsync(response, ct: ct).ConfigureAwait(false);");
        AppendLine($"return {fileResult}.Success(file);");
    }

    /// <summary>
    ///     Renders inline error extraction from the JSON response — no Composition.Host dependency.
    /// </summary>
    private void RenderRemoteErrorExtraction()
    {
        // Extract error info from PragmaticInvokeResponse JSON shape
        AppendLine("var errorCode = \"REMOTE_ERROR\";");
        AppendLine("var errorTitle = \"Remote invocation failed\";");
        AppendLine("var errorStatus = 500;");
        AppendLine("if (json.TryGetProperty(\"error\", out var errorProp))");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (errorProp.TryGetProperty(\"title\", out var t)) errorTitle = t.GetString() ?? errorTitle;");
        AppendLine("if (errorProp.TryGetProperty(\"status\", out var s)) errorStatus = s.GetInt32();");
        AppendLine("if (errorProp.TryGetProperty(\"code\", out var c)) errorCode = c.GetString() ?? errorCode;");
        DecreaseIndent();
        AppendLine("}");
        // Use an inline error record — avoids dependency on RemoteError from Composition.Host
        AppendLine("var error = new " + StripBoundarySuffix(_boundary.TypeName) + "RemoteError(errorCode, errorStatus, errorTitle);");
    }

    /// <summary>
    ///     Renders an inline error for an unsafe/failed deserialization of a successful remote response.
    /// </summary>
    private void RenderRemoteDeserializationError(string reason)
    {
        AppendLine(
            "var error = new " + StripBoundarySuffix(_boundary.TypeName) +
            $"RemoteError(\"REMOTE_DESERIALIZATION_ERROR\", 502, \"Remote invocation succeeded but {reason}.\");");
    }

    // ── AddRemote DI ──

    private void RenderAddRemoteMethod()
    {
        var remoteName = StripBoundarySuffix(_boundary.TypeName) + "RemoteActions";

        Method("AddRemote", () =>
        {
            AppendLine($"services.AddScoped<{remoteName}>();");

            if (!_boundary.IsInternal)
                AppendLine($"services.AddScoped<{_boundary.InterfaceName}>(sp => sp.GetRequiredService<{remoteName}>());");

            // The internal interface, and a group's internal twin, are deliberately absent and cannot
            // be here: they declare the preloaded shapes, which take a tracked entity, and a tracked
            // entity does not cross a process. Whoever injects one is code of this module, and it runs
            // in the process that hosts the module -- which is why this host does not register that
            // module's message handlers, jobs, sagas or event handlers either.
            Comment(
                $"{_boundary.InternalInterfaceName} is local-only and is NOT registered here: it declares the");
            Comment("preloaded shapes, and a tracked entity does not cross a process. Whoever injects it is");
            Comment("code of this module, and runs in the host that owns the module.");

            AppendLine("return services;");
        },
        "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
        new List<MethodParameter>
        {
            new("global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
        },
        AccessModifier.Private,
        new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     Renders a lightweight inline error record — avoids dependency on Composition.Host.
    /// </summary>
    private void RenderRemoteErrorRecord()
    {
        var errorName = StripBoundarySuffix(_boundary.TypeName) + "RemoteError";

        XmlSummary("Lightweight error from remote boundary invocation.");
        AppendLine($"internal sealed record {errorName}(string Code, int StatusCode, string Title) : global::Pragmatic.Result.IError;");
    }
}
