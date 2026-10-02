using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration.Templates;

/// <summary>
///     Generates <c>_Infra.Configuration.Catalog.g.cs</c> — the one call that puts this assembly's
///     <c>[Configuration]</c> sections into <c>IConfigurationCatalog</c>.
/// </summary>
/// <remarks>
///     <para>
///         The <c>MetadataCategory.Configuration</c> payload described these sections from the start and
///         nothing ever read it: three commits, all on the producer. The class comment named three
///         intended uses — discovery, a management UI, pre-deploy validation — and none existed.
///         This is the reader for the first, and what the third is built on.
///     </para>
///     <para>
///         Schema, never values. The catalogue says a key is <c>[Sensitive]</c>; it does not say what it
///         holds, so nothing that can read the catalogue can read a secret through it.
///     </para>
///     <para>
///         Startup validation is not this. <c>ValidateOnStart</c> and the generated
///         <c>IValidateOptions</c> already refuse to start a misconfigured host, and duplicating that
///         here would be a second validator free to disagree with the first.
///         <c>ConfigurationPreflight</c> answers the question <c>ValidateOnStart</c> cannot — is this
///         environment's configuration complete, asked before anything is started.
///     </para>
/// </remarks>
internal sealed class ConfigurationCatalogTemplate : CSharpTemplate
{
    /// <summary>The generated class name, shared with whatever tells the host to call it.</summary>
    public const string ClassName = "PragmaticConfigurationCatalogRegistration";

    /// <summary>The generated method name, shared with whatever tells the host to call it.</summary>
    public const string MethodName = "AddGeneratedConfiguration";

    private readonly ImmutableArray<ConfigurationModel> _models;
    private readonly string _namespace;

    public ConfigurationCatalogTemplate(ImmutableArray<ConfigurationModel> models, string @namespace)
    {
        _models = models;
        _namespace = @namespace;
    }

    /// <summary>The <c>Namespace.Class.Method</c> the host calls for this assembly's sections.</summary>
    public static string FqnFor(string @namespace)
        => string.IsNullOrEmpty(@namespace)
            ? ClassName + "." + MethodName
            : @namespace + "." + ClassName + "." + MethodName;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Configuration";

    protected override bool Validate() => !_models.IsDefaultOrEmpty;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Configuration", "Catalog"), ToSourceText());

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_namespace))
        {
            AppendNamespace(_namespace);
            AppendLine();
        }

        XmlSummary($"Contributes this assembly's {_models.Length} [Configuration] section(s) to the catalogue.");

        Class(ClassName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        var returnType = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";

        Method(MethodName, RenderMethodBody, returnType,
            [
                new MethodParameter("this " + returnType, "services"),
                new MethodParameter("global::Microsoft.Extensions.Configuration.IConfiguration", "configuration")
            ],
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        // Binding first. AggregatorTemplate emits AddOptions + Bind + ValidateOnStart per section into a
        // method the application had to call by hand in Program.cs, and no host called it — not even the
        // Showcase, where two integration tests claimed [Configuration] worked by asserting the app
        // boots. It boots whether or not the options are bound. One generated entry point per assembly
        // now covers both: what the sections are, and that they are bound.
        AppendLine($"global::{AggregatorTemplate.FqnFor(_models)}(services, configuration);");
        AppendLine();

        // One shared instance, appended to — the shape AddRequiredConfiguration already uses, so an
        // assembly composed after another adds to the catalogue instead of replacing it.
        AppendLine("var catalog = global::System.Linq.Enumerable.FirstOrDefault(services,");
        AppendLine("    d => d.ServiceType == typeof(global::Pragmatic.Configuration.Discovery.ConfigurationCatalog))");
        AppendLine("    ?.ImplementationInstance as global::Pragmatic.Configuration.Discovery.ConfigurationCatalog;");
        AppendLine();
        AppendLine("if (catalog is null)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("catalog = new global::Pragmatic.Configuration.Discovery.ConfigurationCatalog();");
        AppendLine("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"
            + ".AddSingleton(services, catalog);");
        AppendLine("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"
            + ".AddSingleton<global::Pragmatic.Configuration.Discovery.IConfigurationCatalog>(services, catalog);");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();

        AppendLine("catalog.Contribute(new global::Pragmatic.Configuration.Discovery.ConfigurationSectionDescriptor[]");
        AppendLine("{");
        IncreaseIndent();

        foreach (var model in _models.OrderBy(m => m.TypeName, System.StringComparer.Ordinal))
            RenderSection(model);

        DecreaseIndent();
        AppendLine("});");
        AppendLine();
        AppendLine("return services;");
    }

    private void RenderSection(ConfigurationModel model)
    {
        AppendLine("new global::Pragmatic.Configuration.Discovery.ConfigurationSectionDescriptor");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"SectionPath = \"{StringHelper.CSharpLiteral(model.SectionPath)}\",");
        AppendLine($"TypeName = \"{StringHelper.CSharpLiteral(model.FullTypeName)}\",");
        AppendLine($"ValidateOnStart = {(model.ValidateOnStart ? "true" : "false")},");
        AppendLine("Properties = new global::Pragmatic.Configuration.Discovery.ConfigurationPropertyDescriptor[]");
        AppendLine("{");
        IncreaseIndent();

        if (!model.Properties.IsDefaultOrEmpty)
        {
            foreach (var property in model.Properties)
            {
                AppendLine("new global::Pragmatic.Configuration.Discovery.ConfigurationPropertyDescriptor");
                AppendLine("{");
                IncreaseIndent();
                AppendLine($"Name = \"{StringHelper.CSharpLiteral(property.Name)}\",");
                AppendLine($"TypeName = \"{StringHelper.CSharpLiteral(property.TypeFullName)}\",");
                AppendLine($"IsRequired = {(property.IsRequired ? "true" : "false")},");
                AppendLine($"IsSensitive = {(property.IsSensitive ? "true" : "false")},");
                DecreaseIndent();
                AppendLine("},");
            }
        }

        DecreaseIndent();
        AppendLine("},");
        DecreaseIndent();
        AppendLine("},");
    }
}
