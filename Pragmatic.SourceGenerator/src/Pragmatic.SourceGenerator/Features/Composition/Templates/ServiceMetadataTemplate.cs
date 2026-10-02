// Pragmatic.SourceGenerator - Composition - Service Metadata Template

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.DI, ...)] attribute.
///     This enables host projects to discover and call AddPragmaticServices().
/// </summary>
internal sealed class ServiceMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<DecoratorModel> _decorators;
    private readonly bool _indent;
    private readonly ImmutableArray<ServiceModel> _services;

    public ServiceMetadataTemplate(
        ImmutableArray<ServiceModel> services,
        ImmutableArray<DecoratorModel> decorators,
        bool indent)
    {
        _services = services;
        _decorators = decorators;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput() => new(
        "_Metadata.DI.g.cs",
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        // Build the JSON
        var json = BuildJson();

        // Generate the assembly attribute
        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.DI, \"{MetadataSchemaVersions.Di}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Composition.SourceGenerator");

        // No extension method — host generates DI code directly from enriched metadata
        builder.PropertyNull("registrationMethod");

        builder.Property("data");
        builder.StartObject();

        // Counts for quick diagnostics
        builder.Property("servicesCount", _services.Length);
        builder.Property("decoratorsCount", _decorators.Length);

        // Enriched services array — ALWAYS present (host needs this to generate DI code)
        if (_services.Length > 0)
        {
            builder.Property("services");
            builder.StartArray();
            foreach (var service in _services.OrderBy(s => s.FullTypeName))
            {
                builder.StartObject();
                builder.Property("interface", service.ServiceTypeName);
                builder.Property("implementation", service.FullTypeName);
                builder.Property("lifetime", service.Lifetime);
                if (service.Key is not null)
                    builder.Property("key", service.Key);
                builder.Property("isOpenGeneric", service.IsOpenGeneric);
                if (service.IsOpenGeneric)
                {
                    if (service.OpenGenericServiceTypeName is not null)
                        builder.Property("openGenericInterface", service.OpenGenericServiceTypeName);
                    if (service.OpenGenericImplementationTypeName is not null)
                        builder.Property("openGenericImplementation", service.OpenGenericImplementationTypeName);
                }

                if (service.RequiresFactory)
                {
                    builder.Property("factory");
                    builder.StartObject();
                    BuildFactoryJson(builder, service);
                    builder.EndObject();
                }

                builder.EndObject();
            }

            builder.EndArray();
        }

        // Enriched decorators array — ALWAYS present
        if (_decorators.Length > 0)
        {
            builder.Property("decorators");
            builder.StartArray();
            foreach (var decorator in _decorators.OrderBy(d => d.Order))
            {
                builder.StartObject();
                builder.Property("interface", decorator.DecoratedInterface);
                builder.Property("implementation", decorator.FullTypeName);
                builder.Property("order", decorator.Order);
                builder.EndObject();
            }

            builder.EndArray();
        }

        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }

    private static void BuildFactoryJson(MetadataJsonBuilder builder, ServiceModel service)
    {
        if (!service.PropertyInjections.IsDefaultOrEmpty)
        {
            builder.Property("propertyInjections");
            builder.StartArray();
            foreach (var prop in service.PropertyInjections)
            {
                builder.StartObject();
                builder.Property("propertyName", prop.PropertyName);
                builder.Property("propertyType", prop.PropertyTypeName);
                builder.Property("isRequired", prop.IsRequired);
                if (prop.Key is not null)
                    builder.Property("key", prop.Key);
                builder.EndObject();
            }

            builder.EndArray();
        }

        if (!service.MethodInjections.IsDefaultOrEmpty)
        {
            builder.Property("methodInjections");
            builder.StartArray();
            foreach (var method in service.MethodInjections)
            {
                builder.StartObject();
                builder.Property("methodName", method.MethodName);
                builder.Property("parameters");
                builder.StartArray();
                foreach (var param in method.Parameters)
                {
                    builder.StartObject();
                    builder.Property("type", param.FullTypeName);
                    builder.Property("isOptional", param.IsOptional);
                    if (param.Key is not null)
                        builder.Property("key", param.Key);
                    builder.EndObject();
                }

                builder.EndArray();
                builder.EndObject();
            }

            builder.EndArray();
        }
    }
}
