namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Result handling rendering for EndpointHandlerTemplate.
/// </summary>
internal sealed partial class EndpointHandlerTemplate
{
    private void RenderResultHandling()
    {
        var hasTypedErrors = !_model.ErrorTypes.IsDefaultOrEmpty;

        if (_model.IsVoid)
        {
            // VoidEndpoint - return 204 on success
            AppendLine("return result.Match(");
            IncreaseIndent();
            AppendLine("() => Microsoft.AspNetCore.Http.Results.NoContent(),");

            // Error handlers - typed or generic
            if (hasTypedErrors)
                for (var i = 0; i < _model.ErrorTypes.Length; i++)
                {
                    var error = _model.ErrorTypes[i];
                    var comma = i < _model.ErrorTypes.Length - 1 ? "," : "";
                    AppendLine($"({error.TypeName} error) => MapError(error, httpContext){comma}");
                }
            else
                // Plain VoidResult carries no error payload: Match(onSuccess, onFailure) takes no argument.
                AppendLine("static () => Microsoft.AspNetCore.Http.Results.Problem(statusCode: 500)");

            DecreaseIndent();
            AppendLine(");");
        }
        else if (_model.IsFileResponse)
        {
            // FileResponse - use special handling with ETag and conditional requests
            AppendLine("return result.Match(");
            IncreaseIndent();
            AppendLine(
                "(success) => global::Pragmatic.Endpoints.Extensions.FileResponseExtensions.ToResult(success, httpContext),");

            // Error handlers - typed or generic
            if (hasTypedErrors)
                for (var i = 0; i < _model.ErrorTypes.Length; i++)
                {
                    var error = _model.ErrorTypes[i];
                    var comma = i < _model.ErrorTypes.Length - 1 ? "," : "";
                    AppendLine($"({error.TypeName} error) => MapError(error, httpContext){comma}");
                }
            else
                AppendLine("(error) => MapError(error, httpContext)");

            DecreaseIndent();
            AppendLine(");");
        }
        else
        {
            // Endpoint<T> - return response on success
            AppendLine("return result.Match(");
            IncreaseIndent();

            var successCode = _model.ComputedSuccessStatusCode;

            if (_model.HttpMethod == "Head")
                // HTTP forbids a response body on HEAD (PRAG0514 warns at compile time).
                AppendLine($"(success) => Microsoft.AspNetCore.Http.Results.StatusCode({successCode}),");
            else if (successCode == 200)
                AppendLine("(success) => Microsoft.AspNetCore.Http.Results.Ok(success),");
            else if (successCode == 201)
            {
            // [CreatedAt] template → real Location header; otherwise 201 with Location: null (B20).
            var location = _model.CreatedAtTemplate is { } template
                ? CreatedAtLocationRenderer.Render(template, "success")
                : "(string?)null";
            AppendLine($"(success) => Microsoft.AspNetCore.Http.Results.Created({location}, success),");
        }
            else if (successCode == 204)
                // 204 has no body by definition.
                AppendLine("(success) => Microsoft.AspNetCore.Http.Results.NoContent(),");
            else
                // Preserve the response body for non-standard success codes (e.g. 202 Accepted):
                // TypedResults.StatusCode would drop the payload despite the declared return type.
                AppendLine($"(success) => Microsoft.AspNetCore.Http.Results.Json(success, statusCode: {successCode}),");

            // Error handlers - typed or generic
            if (hasTypedErrors)
                for (var i = 0; i < _model.ErrorTypes.Length; i++)
                {
                    var error = _model.ErrorTypes[i];
                    var comma = i < _model.ErrorTypes.Length - 1 ? "," : "";
                    AppendLine($"({error.TypeName} error) => MapError(error, httpContext){comma}");
                }
            else
                // No typed errors - Result<T> uses Error base class
                AppendLine("(error) => MapError(error, httpContext)");

            DecreaseIndent();
            AppendLine(");");
        }

        // MapError helper — skipped for plain VoidResult, whose failure path has no error object
        if (!_model.IsVoid || hasTypedErrors)
        {
            AppendLine();
            AppendLine("static Microsoft.AspNetCore.Http.IResult MapError(global::Pragmatic.Result.IError error, Microsoft.AspNetCore.Http.HttpContext httpContext)");
            Block(() => { AppendLine("return global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(error, httpContext);"); });
        }
    }
}
