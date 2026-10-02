namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Endpoint instantiation rendering for EndpointHandlerTemplate.
/// </summary>
internal sealed partial class EndpointHandlerTemplate
{
    private static string GetFieldNameWithoutUnderscore(string fieldName)
    {
        if (fieldName.StartsWith("_"))
            return fieldName.Substring(1);
        return fieldName;
    }

    private void RenderEndpointInstantiation()
    {
        // Collect all properties that must be set in object initializer
        // (for init-only and required properties)
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

        // Body properties (always in initializer since they might be init-only)
        // Values bound from the query string because the verb carries no body.
        //
        // ⚠️ Emitted here as well as in the delegate signature: binding them and not assigning them
        // would produce `new TheAction()` with a required property unset — CS9035, on generated code the
        // author did not write. The same shape as the body properties below, which is what these are
        // when the verb has a body.
        foreach (var queryProperty in _model.QueryBoundProperties)
            initProps.Add($"{queryProperty.Name} = {ToCamelCase(queryProperty.Name)}");

        // Form fields, in the initializer with everything else. ⚠️ Assigned after construction, a
        // `required` or `init` form property, the shape every operation of this framework is written
        // in, would not compile: CS9035 on the first, CS8852 on the second. No declaration avoids both.
        foreach (var param in _model.FormParameters)
            initProps.Add($"{param.PropertyName} = {ToCamelCase(param.PropertyName)}");

        // And the unmarked ones a multipart request carries as form fields.
        foreach (var formProperty in _model.FormBoundProperties)
            initProps.Add($"{formProperty.Name} = {ToCamelCase(formProperty.Name)}");

        if (_model.HasDirectBodyParam)
            initProps.Add($"{_model.BodyProperties[0].Name} = {ToCamelCase(_model.BodyProperties[0].Name)}");
        else if (_model.NeedsBodyDto)
            foreach (var prop in _model.BodyProperties)
                initProps.Add($"{prop.Name} = body.{prop.Name}");

        // Generate with or without object initializer
        if (initProps.Count == 0)
        {
            AppendLine($"var endpoint = new {_model.FullTypeName}();");
        }
        else if (initProps.Count <= 3)
        {
            // Single line for short initializers
            var props = string.Join(", ", initProps);
            AppendLine($"var endpoint = new {_model.FullTypeName} {{ {props} }};");
        }
        else
        {
            // Multi-line for longer initializers
            AppendLine($"var endpoint = new {_model.FullTypeName}");
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
