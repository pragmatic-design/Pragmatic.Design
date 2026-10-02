using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

// File validation checks and form parameter handling
internal sealed partial class DomainActionHandlerTemplate
{
    /// <summary>
    ///     Generates file validation checks for [MaxFileSize] and [AllowedContentTypes]
    ///     before the action is invoked. Returns HTTP 413 or 415 on violation.
    /// </summary>
    private void RenderFileValidation()
    {
        var allFileParams = _model.FormParameters.Where(p => p.IsFile).ToList();
        if (allFileParams.Count == 0)
            return;

        AppendLine("// File validation (HTTP-boundary)");

        // Explicit per-property validation (compile-time [MaxFileSize]/[AllowedContentTypes]).
        foreach (var param in allFileParams.Where(p => p.MaxFileSize.HasValue || !p.AllowedContentTypes.IsDefaultOrEmpty))
        {
            var varName = ToCamelCase(param.PropertyName);
            var isCollection = param.TypeName.Contains("IFormFileCollection");

            if (isCollection)
            {
                AppendLine($"foreach (var file in {varName})");
                Block(() => RenderFileChecks(param, "file", isCollection: true));
            }
            else
            {
                RenderFileChecks(param, varName, isCollection: false);
            }
        }

        // Apply the global upload defaults (MaxUploadFileSize / DefaultAllowedContentTypes)
        // to unattributed file parameters, just like standalone endpoints — a Domain Action upload
        // must not escape the app-wide limits simply because it has no explicit file attributes.
        var unattributed = allFileParams
            .Where(p => !p.MaxFileSize.HasValue && p.AllowedContentTypes.IsDefaultOrEmpty)
            .ToList();

        if (unattributed.Count > 0)
        {
            AppendLine("// Global file upload defaults from PragmaticEndpointsOptions");
            AppendLine("var __fileOpts = httpContext.RequestServices.GetService<global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions>();");

            foreach (var param in unattributed)
            {
                var varName = ToCamelCase(param.PropertyName);
                var isCollection = param.TypeName.Contains("IFormFileCollection");

                if (isCollection)
                {
                    AppendLine($"if (__fileOpts is not null) foreach (var __file in {varName})");
                    AppendLine("{");
                    IncreaseIndent();
                    RenderGlobalFileCheck("__file", param.PropertyName);
                    DecreaseIndent();
                    AppendLine("}");
                }
                else
                {
                    AppendLine("if (__fileOpts is not null)");
                    AppendLine("{");
                    IncreaseIndent();
                    RenderGlobalFileCheck(varName, param.PropertyName);
                    DecreaseIndent();
                    AppendLine("}");
                }
            }
        }

        AppendLine();
    }

    private void RenderGlobalFileCheck(string varName, string propertyName)
    {
        AppendLine($"if (__fileOpts.MaxUploadFileSize.HasValue && {varName}.Length > __fileOpts.MaxUploadFileSize.Value)");
        Block(() =>
        {
            AppendLine("return Microsoft.AspNetCore.Http.Results.Problem(");
            AppendLine($"    $\"File '{propertyName}' exceeds the maximum allowed size of {{__fileOpts.MaxUploadFileSize.Value / (1024 * 1024)}} MB.\",");
            AppendLine("    statusCode: 413);");
        });
        AppendLine($"if (__fileOpts.DefaultAllowedContentTypes is {{ Length: > 0 }} __allowedTypes && !__allowedTypes.Any(t => t.Contains(\"*\") ? {varName}.ContentType.StartsWith(t.Replace(\"*\", \"\")) : string.Equals(t, {varName}.ContentType, System.StringComparison.OrdinalIgnoreCase)))");
        Block(() =>
        {
            AppendLine("return Microsoft.AspNetCore.Http.Results.Problem(");
            AppendLine($"    $\"File '{propertyName}' has unsupported content type '{{{varName}.ContentType}}'. Allowed: {{string.Join(\", \", __allowedTypes)}}\",");
            AppendLine("    statusCode: 415);");
        });
    }

    private void RenderFileChecks(FormParameterModel param, string fileVar, bool isCollection)
    {
        if (param.MaxFileSize.HasValue)
        {
            var maxBytes = param.MaxFileSize.Value;
            var humanSize = FormatFileSize(maxBytes);
            AppendLine($"if ({fileVar}.Length > {maxBytes}L)");
            Block(() =>
            {
                if (isCollection)
                {
                    AppendLine($"return Microsoft.AspNetCore.Http.Results.Problem(");
                    AppendLine($"    $\"File '{{{fileVar}.FileName}}' in '{param.PropertyName}' exceeds the maximum allowed size of {humanSize}.\",");
                    AppendLine($"    statusCode: 413);");
                }
                else
                {
                    AppendLine($"return Microsoft.AspNetCore.Http.Results.Problem(");
                    AppendLine($"    \"File '{param.PropertyName}' exceeds the maximum allowed size of {humanSize}.\",");
                    AppendLine($"    statusCode: 413);");
                }
            });
        }

        if (!param.AllowedContentTypes.IsDefaultOrEmpty)
        {
            var types = string.Join(", ", param.AllowedContentTypes.Select(t => $"\"{t}\""));
            AppendLine($"if (!new[] {{ {types} }}.Contains({fileVar}.ContentType, System.StringComparer.OrdinalIgnoreCase))");
            Block(() =>
            {
                var allowed = string.Join(", ", param.AllowedContentTypes);
                if (isCollection)
                {
                    AppendLine($"return Microsoft.AspNetCore.Http.Results.Problem(");
                    AppendLine($"    $\"File '{{{fileVar}.FileName}}' in '{param.PropertyName}' has unsupported content type '{{{fileVar}.ContentType}}'. Allowed: {allowed}\",");
                    AppendLine($"    statusCode: 415);");
                }
                else
                {
                    AppendLine($"return Microsoft.AspNetCore.Http.Results.Problem(");
                    AppendLine($"    $\"File '{param.PropertyName}' has unsupported content type '{{{fileVar}.ContentType}}'. Allowed: {allowed}\",");
                    AppendLine($"    statusCode: 415);");
                }
            });
        }
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024 * 1024 * 1024)} GB",
            >= 1024 * 1024 => $"{bytes / (1024 * 1024)} MB",
            >= 1024 => $"{bytes / 1024} KB",
            _ => $"{bytes} bytes"
        };
    }

    /// <summary>
    ///     Renders HTTP-boundary size/extension validation for an [HasAttachments] upload endpoint
    ///     before the action is invoked. Returns 413 (size) or 415 (extension) on violation.
    /// </summary>
    private void RenderAttachmentUploadValidation()
    {
        var up = _model.AttachmentUpload!;
        AppendLine("// Attachment upload validation (HTTP-boundary)");

        if (up.MaxFileSizeBytes > 0)
        {
            var humanSize = FormatFileSize(up.MaxFileSizeBytes);
            AppendLine($"if (file.Length > {up.MaxFileSizeBytes}L)");
            Block(() =>
            {
                AppendLine("return Microsoft.AspNetCore.Http.Results.Problem(");
                AppendLine($"    \"The uploaded file exceeds the maximum allowed size of {humanSize}.\",");
                AppendLine("    statusCode: 413);");
            });
        }

        if (!up.AllowedExtensions.IsDefaultOrEmpty)
        {
            var exts = string.Join(", ", up.AllowedExtensions.Select(e => $"\"{e}\""));
            var allowed = string.Join(", ", up.AllowedExtensions);
            AppendLine($"var __ext = System.IO.Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;");
            AppendLine($"if (!new[] {{ {exts} }}.Contains(__ext, System.StringComparer.OrdinalIgnoreCase))");
            Block(() =>
            {
                AppendLine("return Microsoft.AspNetCore.Http.Results.Problem(");
                AppendLine($"    $\"File extension '{{__ext}}' is not allowed. Allowed: {allowed}\",");
                AppendLine("    statusCode: 415);");
            });
        }

        AppendLine();
    }
}
