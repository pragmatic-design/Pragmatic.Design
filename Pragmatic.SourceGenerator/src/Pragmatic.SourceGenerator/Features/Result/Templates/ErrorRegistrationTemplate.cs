using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Result.Models;

namespace Pragmatic.SourceGenerator.Features.Result.Templates;

/// <summary>
///     Registers an error type with the two runtime registries, from a module initializer.
/// </summary>
/// <remarks>
///     <para>
///         <c>ErrorTypeRegistry</c> maps a JSON discriminator back to the concrete type. Only the
///         framework's own errors were ever in it — registered from the converters' static constructors —
///         so a user error serialized with its discriminator came back as <c>SerializedError</c>, and a
///         multi-error result then refused it as "not one of the declared error types" while advising the
///         reader to register it with an API nothing called.
///     </para>
///     <para>
///         <c>ErrorSchemaRegistry</c> carries the OpenAPI schema. <c>ErrorSchemaEnricher</c> preferred it
///         over reflection and it was likewise empty, so the reflective branch — <c>GetProperties</c> up
///         the inheritance chain plus <c>Activator.CreateInstance</c> to read Code, StatusCode and Title —
///         was the only one that ever ran. Both are known here.
///     </para>
/// </remarks>
internal sealed class ErrorRegistrationTemplate : CSharpTemplate
{
    private readonly ErrorModel _model;

    public ErrorRegistrationTemplate(ErrorModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Result";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"Error registration for {_model.TypeName}";

    protected override bool Validate() => _model.NeedsRegistration;

    public override Artifact RenderOutput()
    {
        var hintName = VirtualFolderHints.ForType(_model.UniqueName, "ErrorRegistration", _model.Namespace);
        return new Artifact(hintName, ToSourceText());
    }

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Registers {_model.TypeName} with the Pragmatic.Result registries.");
        Class(NamingHelper.AppendSuffix(_model.UniqueName, "ErrorRegistration"), RenderBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("internal static void Register()");
        Block(RenderRegistrations);
    }

    private void RenderRegistrations()
    {
        // The symbol's own fully-qualified name, already carrying global:: and joining a nested type
        // with '.', which Namespace + "." + TypeName gets wrong.
        var fullName = _model.FullyQualifiedName;

        AppendLine($"global::Pragmatic.Result.Serialization.ErrorTypeRegistry.Register<{fullName}>();");

        if (!_model.CanRegisterSchema)
            return;

        AppendLine();

        // A constant expression body is read at compile time; anything computed needs an instance, and
        // constructing one in typed C# is what the reflective path did through Activator.
        if (_model.NeedsProbe)
            AppendLine($"var probe = new {fullName}();");

        AppendLine($"global::Pragmatic.Result.ErrorSchemaRegistry.Register<{fullName}>(");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Result.ErrorSchemaMetadata(");
        IncreaseIndent();
        AppendLine($"{CodeExpression()},");
        AppendLine($"{StatusCodeExpression()},");
        AppendLine($"{TitleExpression()},");
        AppendLine("[");
        IncreaseIndent();

        for (var i = 0; i < _model.CustomProperties.Length; i++)
        {
            var property = _model.CustomProperties[i];
            var comma = i == _model.CustomProperties.Length - 1 ? "" : ",";
            AppendLine("new global::Pragmatic.Result.ErrorPropertyDescriptor(");
            IncreaseIndent();
            AppendLine($"\"{StringHelper.CSharpLiteral(property.CamelCaseName)}\",");
            AppendLine($"\"{property.JsonSchemaType}\",");
            // Verbatim the string the reflective branch produced, so a schema does not change shape
            // just because its metadata now arrives from the generator.
            AppendLine($"\"Error-specific extension: {StringHelper.CSharpLiteral(property.Name)}\",");
            AppendLine($"{(property.IsNullable ? "true" : "false")},");
            AppendLine(RenderEnumValues(property));
            DecreaseIndent();
            AppendLine($"){comma}");
        }

        DecreaseIndent();
        AppendLine("]));");
        DecreaseIndent();
        DecreaseIndent();
    }

    private string CodeExpression()
    {
        if (_model.ConstantCode is not null)
            return $"\"{StringHelper.CSharpLiteral(_model.ConstantCode)}\"";

        // Same fallback the reflective enricher used when Activator.CreateInstance returned null.
        return _model.NeedsProbe ? "probe.Code" : $"\"{StringHelper.CSharpLiteral(_model.TypeName)}\"";
    }

    private string StatusCodeExpression()
    {
        if (_model.ConstantStatusCode is not null)
            return _model.ConstantStatusCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return _model.NeedsProbe ? "probe.StatusCode" : "400";
    }

    private string TitleExpression()
    {
        if (_model.ConstantTitle is not null)
            return $"\"{StringHelper.CSharpLiteral(_model.ConstantTitle)}\"";

        return _model.NeedsProbe ? "probe.Title" : $"\"{StringHelper.CSharpLiteral(FormatTitle(_model.TypeName))}\"";
    }

    /// <summary>
    ///     "NotFoundError" → "Not Found": the title used when there is no instance to read one from.
    /// </summary>
    private static string FormatTitle(string typeName)
    {
        if (typeName.EndsWith("Error", System.StringComparison.Ordinal))
            typeName = typeName.Substring(0, typeName.Length - "Error".Length);

        var result = new System.Text.StringBuilder();
        for (var i = 0; i < typeName.Length; i++)
        {
            if (i > 0 && char.IsUpper(typeName[i]) && !char.IsUpper(typeName[i - 1]))
                result.Append(' ');
            result.Append(typeName[i]);
        }

        return result.ToString();
    }

    private static string RenderEnumValues(ErrorPropertyModel property)
    {
        if (property.EnumValues.Length == 0)
            return "null";

        var values = property.EnumValues.Select(v => $"\"{StringHelper.CSharpLiteral(v)}\"");
        return $"new string[] {{ {string.Join(", ", values)} }}";
    }
}
