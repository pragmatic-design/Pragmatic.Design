using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration.Templates;

/// <summary>
///     Generates the <c>ITenantOptionsBinder&lt;T&gt;</c> <c>TenantOptions&lt;T&gt;</c> binds through: one typed
///     assignment per property, converted by <c>ConfigurationValue</c>, in place of a <c>ConfigurationBuilder</c>
///     and the reflective <c>Bind</c> on every call (#139).
/// </summary>
internal sealed class TenantOptionsBinderTemplate : CSharpTemplate
{
    private const string ResolutionNamespace = "global::Pragmatic.Configuration.Resolution";

    private readonly ConfigurationModel _model;

    public TenantOptionsBinderTemplate(ConfigurationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Configuration";

    /// <summary>The generated binder's simple type name.</summary>
    public string ClassName => NamingHelper.AppendSuffix(_model.TypeName, "TenantBinder");

    /// <summary>Fully-qualified binder type for the registration call.</summary>
    public string FullyQualifiedName => _model.Namespace is { Length: > 0 } ns
        ? $"global::{ns}.{ClassName}"
        : $"global::{ClassName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "TenantBinder", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsPartial && _model.HasTypedBinder;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"Binds <see cref=\"{_model.TypeName}\"/> for a tenant from raw configuration values, typed.");

        Class(ClassName, RenderBody,
            baseType: $"{ResolutionNamespace}.ITenantOptionsBinder<global::{_model.FullTypeName}>",
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        AppendLine($"public void Bind(global::{_model.FullTypeName} target, global::System.Func<string, string?> value)");
        AppendLine("{");
        IncreaseIndent();

        var index = 0;
        foreach (var binding in _model.TypedBindings)
        {
            var raw = $"raw{index++}";
            var key = EscapeLiteral(binding.Key);
            AppendLine($"if (value({key}) is {{ }} {raw})");
            IncreaseIndent();
            AppendLine($"target.{binding.PropertyName} = {Conversion(binding, raw, key)};");
            DecreaseIndent();
        }

        DecreaseIndent();
        AppendLine("}");
    }

    private static string Conversion(TypedBindingModel binding, string raw, string key)
    {
        if (binding.Converter is not { } converter)
            return raw;

        var converted = $"{ResolutionNamespace}.ConfigurationValue.{converter}({raw}, {key})";
        // An empty value binds a Nullable<T> to null, as ConfigurationBinder does.
        return binding.IsNullable ? $"{raw}.Length == 0 ? null : {converted}" : converted;
    }

    private static string EscapeLiteral(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
