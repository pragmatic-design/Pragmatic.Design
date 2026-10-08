using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Result handling and action instantiation for Mutation endpoint handlers.
/// </summary>
internal sealed partial class MutationHandlerTemplate
{
    private void RenderResultHandling()
    {
        // Mutations always return Result<TEntity, IError> — never void
        AppendLine("return result.Match(");
        IncreaseIndent();

        var successCode = _model.ComputedSuccessStatusCode;

        // What goes on the wire. A mutation returns the tracked entity, which is the right answer for an
        // in-process caller and the wrong one for HTTP: serialised, it carries OwnerId, CreatedBy,
        // IsDeleted and every other column the server owns. A generated response factory projects it to
        // the DTO the matching read answers with, so one resource has one shape. A hand-written mutation
        // sets none and keeps answering with the entity, as it did before.
        //
        // [Mutation(ReturnType = Id | LogicalKey)] answers with a record instead, built from the same
        // entity: the key the boundary member returns, in a shape the JSON context can name.
        var body = _model switch
        {
            { MutationKeyResponseType: { } key, MutationReturnsId: true } => $"new {key} {{ Id = success.PersistenceId }}",
            { MutationKeyResponseType: { } key } => $"{key}.From(success)",
            { MutationResponseFactory: { } factory } => $"{factory}(success)",
            _ => "success"
        };

        if (successCode == 200)
            AppendLine($"(success) => {GeneratedResponseRenderer.Render(_model, body, 200) ?? $"Microsoft.AspNetCore.Http.Results.Ok({body})"},");
        else if (successCode == 201)
        {
            // [CreatedAt] template → that Location. Without one, a Create whose Single read answers at its
            // own route plus /{id} points there, from the request path, so the runtime route prefix and
            // any group prefix are already in it. Otherwise 201 with Location: null (B20) — an address
            // the generator would have to guess is worse than none.
            // The location is built from the entity, not the body: the id may not be on the DTO.
            var location = _model switch
            {
                { CreatedAtTemplate: { } template } => CreatedAtLocationRenderer.Render(template, "success"),
                { LocationFromReadRoute: true } =>
                    "$\"{(httpContext.Request.PathBase + httpContext.Request.Path).Value?.TrimEnd('/')}/{success.PersistenceId:D}\"",
                _ => "(string?)null"
            };
            AppendLine($"(success) => {GeneratedResponseRenderer.Render(_model, body, 201, location) ?? $"Microsoft.AspNetCore.Http.Results.Created({location}, {body})"},");
        }
        else if (successCode == 204)
            // 204 has no body by definition.
            AppendLine("(success) => Microsoft.AspNetCore.Http.Results.NoContent(),");
        else
            // Preserve the response body for non-standard success codes (e.g. 202 Accepted).
            AppendLine($"(success) => {GeneratedResponseRenderer.Render(_model, body, successCode) ?? $"Microsoft.AspNetCore.Http.Results.Json({body}, statusCode: {successCode})"},");

        AppendLine("(global::Pragmatic.Result.IError error) => MapError(error, httpContext)");
        DecreaseIndent();
        AppendLine(");");

        AppendLine();

        // Add MapError helper
        AppendLine("static Microsoft.AspNetCore.Http.IResult MapError(global::Pragmatic.Result.IError error, Microsoft.AspNetCore.Http.HttpContext httpContext)");
        Block(() => { AppendLine("return global::Pragmatic.Endpoints.Extensions.ErrorExtensions.ToResult(error, httpContext);"); });
    }

    private void RenderActionInstantiation()
    {
        var initProps = new List<string>();

        // Route parameters (always required)
        foreach (var param in _model.RouteParameters)
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

        // Required header parameters
        foreach (var param in _model.HeaderParameters.Where(p => p.IsRequired))
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

        // Required query parameters
        foreach (var param in _model.QueryParameters.Where(p => p.IsRequired))
            initProps.Add($"{param.PropertyName} = {param.Name}");

        // Optional header and query values on init properties
        initProps.AddRange(RequestValueBinding.InitializerEntries(_model));

        // Form fields, in the initializer with everything else. ⚠️ Assigned after construction, a
        // `required` or `init` form property, the shape every operation of this framework is written
        // in, would not compile: CS9035 on the first, CS8852 on the second. No declaration avoids both.
        foreach (var param in _model.FormParameters)
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

        // And the unmarked ones a multipart request carries as form fields.
        foreach (var formProperty in _model.FormBoundProperties)
            initProps.Add($"{formProperty.Name} = {ToCamelCase(formProperty.Name)}");

        // Body properties
        if (_model.HasDirectBodyParam)
        {
            var bp = _model.BodyProperties[0];
            initProps.Add($"{bp.Name} = {ToCamelCase(bp.Name)}");
        }
        else if (_model.NeedsBodyDto)
        {
            foreach (var prop in _model.BodyProperties)
                initProps.Add($"{prop.Name} = body.{prop.Name}");
        }

        // Generate with or without object initializer
        if (initProps.Count == 0)
        {
            AppendLine($"var action = new {_model.FullTypeName}();");
        }
        else if (initProps.Count <= 3)
        {
            var props = string.Join(", ", initProps);
            AppendLine($"var action = new {_model.FullTypeName} {{ {props} }};");
        }
        else
        {
            AppendLine($"var action = new {_model.FullTypeName}");
            AppendLine("{");
            IncreaseIndent();
            for (var i = 0; i < initProps.Count; i++)
            {
                var comma = i < initProps.Count - 1 ? "," : "";
                AppendLine($"{initProps[i]}{comma}");
            }

            DecreaseIndent();
            AppendLine("};");
        }

        AppendLine();
    }
}
