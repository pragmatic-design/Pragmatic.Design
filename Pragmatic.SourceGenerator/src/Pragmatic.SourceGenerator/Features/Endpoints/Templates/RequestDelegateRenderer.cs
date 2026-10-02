using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Renders a generated endpoint as a <c>RequestDelegate</c>: the binding, then the handler, then
///     the response.
/// </summary>
/// <remarks>
///     <para>
///         ASP.NET binds a handler <c>Delegate</c> through <c>RequestDelegateFactory</c>, which is
///         reflection-based. Its AOT replacement is the Request Delegate Generator, and one source
///         generator cannot see another's output — so a generated <c>MapPost(route, lambda)</c> is
///         never rewritten and falls back to the runtime factory. Measured on a published Native AOT
///         binary: 500 on the first request, or 200 with an empty body when the reflection fallback is
///         left on. The <c>RequestDelegate</c> overload is passed through untouched and works.
///     </para>
///     <para>
///         The handler lambda itself is left exactly as the templates already write it — same
///         parameters, same body. It becomes a local, and the delegate binds its arguments and calls
///         it. Endpoint filters still run on this shape (verified), and no Pragmatic filter reads the
///         bound arguments, so validation, policy, idempotency and caching are unaffected.
///     </para>
/// </remarks>
internal static class RequestDelegateRenderer
{
    private const string Binder = "global::Pragmatic.Endpoints.Binding.RequestBinder";
    private const string Values = "global::Pragmatic.Endpoints.Binding.RequestValues";
    private const string Failure = "global::Pragmatic.Endpoints.Binding.BindingFailure";

    /// <summary>The lines answering 400 for one parameter, body included.</summary>
    private static void Reject(System.Action<string> line, string parameter, string reason, string indent = "")
    {
        line($"{indent}await {Failure}.WriteAsync(httpContext, \"{StringHelper.CSharpLiteral(parameter)}\", \"{StringHelper.CSharpLiteral(reason)}\").ConfigureAwait(false);");
        line($"{indent}return;");
    }

    /// <summary>Renders the parameter list of the inner handler, one per line, comma-separated.</summary>
    /// <remarks>
    ///     A parameter's default is applied by the binding below, which always passes every argument;
    ///     it is not repeated in the signature. There it was inert, and it was legal only by accident
    ///     of position — a query endpoint lists its paging last, an action lists its query-string
    ///     values first, and an optional parameter ahead of a required one is CS1737.
    /// </remarks>
    public static void RenderSignature(IReadOnlyList<BoundParameter> parameters, System.Action<string> line)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var comma = i < parameters.Count - 1 ? "," : "";
            var parameter = parameters[i];
            line($"    {parameter.TypeName} {parameter.Name}{comma}");
        }
    }

    /// <summary>
    ///     Renders the statements that produce every argument, in declaration order. A required value
    ///     that is absent or malformed writes a 400 and stops — the handler is never reached.
    /// </summary>
    public static void RenderBinding(IReadOnlyList<BoundParameter> parameters, System.Action<string> line)
    {
        if (parameters.Any(static p => p.Source is BindingSource.Form or BindingSource.FormFile))
            RenderFormPreamble(parameters, line);

        foreach (var parameter in parameters)
        {
            switch (parameter.Source)
            {
                case BindingSource.HttpContext:
                    break;

                case BindingSource.CancellationToken:
                    line($"var {parameter.Name} = httpContext.RequestAborted;");
                    break;

                case BindingSource.Service:
                    line($"var {parameter.Name} = global::Microsoft.Extensions.DependencyInjection." +
                         $"ServiceProviderServiceExtensions.GetRequiredService<{parameter.TypeName}>(httpContext.RequestServices);");
                    break;

                case BindingSource.KeyedService:
                    line($"var {parameter.Name} = global::Microsoft.Extensions.DependencyInjection." +
                         $"ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<{parameter.TypeName}>(" +
                         $"httpContext.RequestServices, {parameter.ServiceKey});");
                    break;

                case BindingSource.FormFile:
                    RenderFormFile(parameter, line);
                    break;

                case BindingSource.Body:
                    RenderBody(parameter, line);
                    break;

                case BindingSource.QueryMany:
                    RenderQueryMany(parameter, line);
                    break;

                default:
                    RenderScalar(parameter, line);
                    break;
            }
        }
    }

    /// <summary>The arguments to pass to the handler, in order.</summary>
    public static string RenderArguments(IReadOnlyList<BoundParameter> parameters)
        => string.Join(", ", parameters.Select(static p => p.Name));

    private static void RenderBody(BoundParameter parameter, System.Action<string> line)
    {
        // Through the seam's JsonTypeInfo, not the reflection-based overload: this is the read that
        // has to work with the fallback disabled.
        var type = TrimNullable(parameter.TypeName);
        var read = $"__read_{parameter.Name}";

        line($"{type}? {read};");
        line("try");
        line("{");
        line($"    {read} = await global::Microsoft.AspNetCore.Http.HttpRequestJsonExtensions.ReadFromJsonAsync(");
        line($"        httpContext.Request,");
        line($"        global::Pragmatic.Endpoints.Binding.RequestJson.TypeInfo<{type}>(httpContext),");
        line("        httpContext.RequestAborted).ConfigureAwait(false);");
        line("}");
        line("catch (global::System.Text.Json.JsonException __jsonEx)");
        line("{");
        line($"    await {Failure}.WriteAsync(httpContext, \"{StringHelper.CSharpLiteral(parameter.Name)}\", __jsonEx.Message).ConfigureAwait(false);");
        line("    return;");
        line("}");
        // ⚠️ A missing or non-JSON Content-Type throws InvalidOperationException, not JsonException, so
        // guarding only the latter turned the commonest client mistake there is into an unhandled 500 —
        // on every write endpoint of every application. The same reasoning as the antiforgery guard
        // below, found much later because a body without a content type still looks like a body.
        line("catch (global::System.InvalidOperationException __mediaEx)");
        line("{");
        line($"    await {Failure}.WriteUnsupportedMediaTypeAsync(httpContext, __mediaEx.Message).ConfigureAwait(false);");
        line("    return;");
        line("}");
        line($"if ({read} is null)");
        line("{");
        Reject(line, parameter.Name, "the request body is required", "    ");
        line("}");
        // Cast rather than assign: for a value-type body `T?` is `Nullable<T>`, and the handler
        // declares `T`. The cast unwraps it and costs nothing for a reference type.
        line($"var {parameter.Name} = ({type}){read};");
    }

    /// <summary>
    ///     Reads the form once, for every parameter that comes from it.
    /// </summary>
    /// <remarks>
    ///     <c>ReadFormAsync</c> throws when antiforgery ran and failed — ASP.NET says exactly that in
    ///     the exception, and its own binder checks the feature first. Ours has to as well, or a
    ///     missing token becomes a 500 instead of the 400 the endpoint declared. Emitted once rather
    ///     than per parameter: the first version guarded only the file path, and a form of plain
    ///     fields went straight past it.
    /// </remarks>
    private static void RenderFormPreamble(IReadOnlyList<BoundParameter> parameters, System.Action<string> line)
    {
        var first = parameters.First(static p => p.Source is BindingSource.Form or BindingSource.FormFile);

        // Antiforgery first, and not by preference: `HasFormContentType` is itself a form access, so
        // it throws before any check of ours could run. The stack said so — FormFeature calls
        // HandleUncheckedAntiforgeryValidationFeature from the property getter.
        line("if (!global::Pragmatic.Endpoints.Binding.AntiforgeryGuard.Validate(httpContext))");
        line("{");
        Reject(line, "antiforgery", "the antiforgery token is missing or invalid", "    ");
        line("}");
        line("if (!httpContext.Request.HasFormContentType)");
        line("{");
        Reject(line, first.Name, "a multipart/form-data or form-urlencoded body is required", "    ");
        line("}");
        // ⚠️ And the read throws a second way: InvalidDataException when the body goes past
        // RequestFormLimits.MultipartBodyLengthLimit, which [HasAttachments] sets from its own ceiling.
        // Uncaught, an upload one byte over that ceiling answered 500 — and the 413 branch the same
        // generator emits after the read could never run, because the read never returned.
        line("global::Microsoft.AspNetCore.Http.IFormCollection __form;");
        line("try");
        line("{");
        line("    __form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted).ConfigureAwait(false);");
        line("}");
        line("catch (global::System.IO.InvalidDataException __overLimit)");
        line("{");
        line($"    await {Failure}.WritePayloadTooLargeAsync(httpContext, __overLimit.Message).ConfigureAwait(false);");
        line("    return;");
        line("}");
    }

    private static void RenderFormFile(BoundParameter parameter, System.Action<string> line)
    {

        // A parameter typed as the collection wants every file, not the one matching its name.
        if (parameter.TypeName.TrimEnd('?').EndsWith("IFormFileCollection"))
        {
            line($"var {parameter.Name} = __form.Files;");
            return;
        }

        var fieldName = parameter.SourceName is null
            ? "null"
            : $"\"{StringHelper.CSharpLiteral(parameter.SourceName)}\"";

        line($"var {parameter.Name} = {Values}.File(__form, {fieldName});");

        if (parameter.IsRequired)
        {
            line($"if ({parameter.Name} is null)");
            line("{");
            Reject(line, parameter.Name, "a file is required", "    ");
            line("}");
        }
    }

    private static void RenderQueryMany(BoundParameter parameter, System.Action<string> line)
    {
        var raw = $"__raw_{parameter.Name}";
        var element = ElementType(parameter.TypeName);
        var call = element is "string" or "global::System.String"
            ? $"{Binder}.TryBindManyStrings({raw}, out var __parsed_{parameter.Name})"
            : $"{Binder}.TryBindMany<{element}>({raw}, out var __parsed_{parameter.Name})";

        line($"var {raw} = {Values}.QueryAll(httpContext, \"{StringHelper.CSharpLiteral(parameter.SourceName ?? parameter.Name)}\");");

        if (parameter.IsRequired)
        {
            line($"if (!{call})");
            line("{");
            Reject(line, parameter.SourceName ?? parameter.Name, "one or more values are missing or malformed", "    ");
            line("}");
            line($"var {parameter.Name} = __parsed_{parameter.Name};");
            return;
        }

        // A query string that never mentions the key is not the same as one that mentions it empty:
        // the first must leave the target property at whatever it already was.
        line($"{element}[]? {parameter.Name} = null;");
        line($"if ({raw}.Length > 0)");
        line("{");
        line($"    if (!{call})");
        line("    {");
        Reject(line, parameter.SourceName ?? parameter.Name, "one or more values are malformed", "        ");
        line("    }");
        line($"    {parameter.Name} = __parsed_{parameter.Name};");
        line("}");
    }

    /// <summary>The element type of an array expression such as <c>int[]</c>.</summary>
    private static string ElementType(string typeName)
    {
        var trimmed = TrimNullable(typeName);
        return trimmed.EndsWith("[]") ? trimmed.Substring(0, trimmed.Length - 2) : trimmed;
    }

    private static void RenderScalar(BoundParameter parameter, System.Action<string> line)
    {
        var raw = $"__raw_{parameter.Name}";
        line($"var {raw} = {ReadRaw(parameter)};");

        var type = TrimNullable(parameter.TypeName);

        if (parameter.IsRequired)
        {
            var call = parameter.Kind switch
            {
                BindKind.String => $"{Binder}.TryBindString({raw}, out var {parameter.Name})",
                BindKind.Enum => $"{Binder}.TryBindEnum<{type}>({raw}, out var {parameter.Name})",
                BindKind.Parsable => $"{Binder}.TryBind<{type}>({raw}, out var {parameter.Name})",
                _ => null,
            };

            if (call is null)
            {
                // A type the classifier could not place: pass the raw value and let the handler's own
                // declaration decide, rather than invent a conversion.
                line($"var {parameter.Name} = {raw};");
                return;
            }

            line($"if (!{call})");
            line("{");
            Reject(line, parameter.SourceName ?? parameter.Name, "the value is missing or malformed", "    ");
            line("}");
            return;
        }

        if (parameter.Kind == BindKind.String)
        {
            line($"var {parameter.Name} = {raw}{(parameter.DefaultValue is { } literal ? $" ?? {literal}" : "")};");
            return;
        }

        if (parameter.Kind is not (BindKind.Enum or BindKind.Parsable))
        {
            line($"{parameter.TypeName} {parameter.Name} = {parameter.DefaultValue ?? "default"};");
            return;
        }

        // Absent and malformed are different: omitting a page size means "use the default", sending
        // "abc" means the caller got it wrong. Written inline rather than through the Optional
        // overloads because those need a struct constraint this parameter may not satisfy.
        var parse = parameter.Kind == BindKind.Enum
            ? $"{Binder}.TryBindEnum<{type}>({raw}, out var __parsed_{parameter.Name})"
            : $"{Binder}.TryBind<{type}>({raw}, out var __parsed_{parameter.Name})";

        // Declared as the handler declares it. A parameter with a real default — paging, say — is not
        // nullable there, and widening it here would hand `int?` to an `int`.
        var declared = parameter.TypeName.EndsWith("?")
            ? $"{type}? {parameter.Name} = {parameter.DefaultValue ?? "null"}"
            : $"{parameter.TypeName} {parameter.Name} = {parameter.DefaultValue ?? "default"}";

        line($"{declared};");
        line($"if (!string.IsNullOrEmpty({raw}))");
        line("{");
        line($"    if (!{parse})");
        line("    {");
        Reject(line, parameter.SourceName ?? parameter.Name, "the value is malformed", "        ");
        line("    }");
        line($"    {parameter.Name} = __parsed_{parameter.Name};");
        line("}");
    }

    private static string ReadRaw(BoundParameter parameter)
    {
        var name = StringHelper.CSharpLiteral(parameter.SourceName ?? parameter.Name);

        return parameter.Source switch
        {
            BindingSource.Route => $"{Values}.Route(httpContext, \"{name}\")",
            BindingSource.Query => $"{Values}.Query(httpContext, \"{name}\")",
            BindingSource.Header => $"{Values}.Header(httpContext, \"{name}\")",
            BindingSource.Form => $"{Values}.Form(__form, \"{name}\")",
            _ => "null",
        };
    }

    /// <summary>The type without its nullable annotation, for a generic argument that cannot carry one.</summary>
    private static string TrimNullable(string typeName)
        => typeName.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;
}
