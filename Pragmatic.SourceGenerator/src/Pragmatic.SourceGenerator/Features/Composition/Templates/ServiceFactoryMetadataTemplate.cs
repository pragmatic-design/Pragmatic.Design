// Pragmatic.SourceGenerator - Composition - ServiceFactory metadata (library mode)

using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Emits an <c>[assembly: PragmaticMetadata(MetadataCategory.DI, ...)]</c> carrying this library's
///     <c>[ServiceFactory]</c> classes, so a host can discover and register factories declared in a
///     referenced boundary library (cross-assembly companion to the host-local registration).
///     Relies on <c>PragmaticMetadataAttribute</c> being <c>AllowMultiple</c>.
/// </summary>
internal sealed class ServiceFactoryMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<ServiceFactoryModel> _factories;
    private readonly bool _indent;

    public ServiceFactoryMetadataTemplate(ImmutableArray<ServiceFactoryModel> factories, bool indent)
    {
        _factories = factories;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput() => new("_Metadata.ServiceFactories.g.cs", ToSourceText());

    protected override bool Validate() => _factories.Any(f => f.IsValid);

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();
        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.DI, \"{MetadataSchemaVersions.Di}\", \"\"\"");
        AppendLine(BuildJson());
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);
        builder.StartObject();
        builder.Property("generator", "Pragmatic.Composition.SourceGenerator");
        builder.PropertyNull("registrationMethod");
        builder.Property("data");
        builder.StartObject();
        builder.Property("serviceFactories");
        builder.StartArray();

        foreach (var factory in _factories.Where(f => f.IsValid).OrderBy(f => f.FactoryClassFullName))
        {
            builder.StartObject();
            builder.Property("class", factory.FactoryClassFullName);
            builder.Property("methods");
            builder.StartArray();
            foreach (var method in factory.Methods)
            {
                builder.StartObject();
                builder.Property("method", method.MethodName);
                builder.Property("returnType", method.ReturnTypeFullName);
                builder.Property("lifetime", method.Lifetime);
                builder.Property("parameters");
                builder.StartArray();
                foreach (var param in method.Parameters)
                {
                    builder.StartObject();
                    builder.Property("type", param.FullTypeName);
                    builder.Property("optional", param.IsOptional);
                    builder.EndObject();
                }

                builder.EndArray();
                builder.EndObject();
            }

            builder.EndArray();
            builder.EndObject();
        }

        builder.EndArray();
        builder.EndObject();
        builder.EndObject();
        return builder.ToString();
    }
}
