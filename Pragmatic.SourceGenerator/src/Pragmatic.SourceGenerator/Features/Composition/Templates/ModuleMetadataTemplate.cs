// Pragmatic.SourceGenerator - Composition - Module Metadata Template

using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.Module, ...)] attribute.
///     This enables host projects to discover module dependencies and build the topology.
/// </summary>
internal sealed class ModuleMetadataTemplate : CSharpTemplate
{
    private readonly bool _hasServices;
    private readonly bool _hasStartups;
    private readonly bool _indent;
    private readonly ModuleModel _module;
    private readonly string _namespacePrefix;

    public ModuleMetadataTemplate(
        ModuleModel module,
        string namespacePrefix,
        bool hasServices,
        bool hasStartups,
        bool indent)
    {
        _module = module;
        _namespacePrefix = namespacePrefix;
        _hasServices = hasServices;
        _hasStartups = hasStartups;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput() => new(
        "_Metadata.Module.g.cs",
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.Module, \"{MetadataSchemaVersions.Module}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Composition.SourceGenerator");
        builder.Property("name", _module.Name);

        if (_module.Version is not null)
            builder.Property("version", _module.Version);

        if (_module.Description is not null)
            builder.Property("description", _module.Description);

        // Package route prefix (when this assembly IS a package)
        if (_module.PackageRoutePrefix is not null)
            builder.Property("packageRoutePrefix", _module.PackageRoutePrefix);

        // Dependencies
        if (_module.DependsOn.Length > 0)
            builder.PropertyArray("dependsOn", _module.DependsOn);

        // Package imports — assembly names + route prefixes of [UsePackage<T>] declarations
        if (!_module.UsePackages.IsDefaultOrEmpty)
        {
            builder.Property("packages");
            builder.StartArray();
            foreach (var pkg in _module.UsePackages)
            {
                builder.StartObject();
                builder.Property("assembly", pkg.PackageAssemblyName);
                if (pkg.RoutePrefix is not null)
                    builder.Property("routePrefix", pkg.RoutePrefix);
                else
                    builder.PropertyNull("routePrefix");
                builder.EndObject();
            }
            builder.EndArray();
        }

        // Exposed endpoints (from [ExposeEndpoint<T>])
        if (!_module.ExposedEndpoints.IsDefaultOrEmpty)
        {
            builder.Property("exposedEndpoints");
            builder.StartArray();
            foreach (var ep in _module.ExposedEndpoints)
            {
                builder.StartObject();
                builder.Property("actionType", ep.ActionTypeName);
                builder.Property("actionSimpleName", ep.ActionSimpleName);
                builder.Property("httpVerb", ep.HttpVerb);
                builder.Property("route", ep.Route);
                builder.Property("actionAssembly", ep.ActionAssemblyName);
                builder.Property("allowAnonymous", ep.AllowAnonymous);

                if (ep.Name is not null)
                    builder.Property("name", ep.Name);

                if (ep.GroupTypeName is not null)
                    builder.Property("groupType", ep.GroupTypeName);

                if (!ep.AdditionalPermissions.IsDefaultOrEmpty)
                    builder.PropertyArray("additionalPermissions", ep.AdditionalPermissions);
                else
                    builder.PropertyArray("additionalPermissions", Enumerable.Empty<string>());

                // The action's inputs, for a verb that carries no body. The module declares them and
                // the host binds them from the query string: the host sees the action's assembly, but
                // asking a symbol for what a module already knows is a second reading: the module
                // declares, the host composes. Empty for POST/PUT/PATCH, which bind the whole action from the body.
                if (!ep.Inputs.IsDefaultOrEmpty)
                {
                    builder.Property("inputs");
                    builder.StartArray();
                    foreach (var input in ep.Inputs)
                    {
                        builder.StartObject();
                        builder.Property("name", input.Name);
                        builder.Property("type", input.TypeName);
                        builder.Property("nullable", input.IsNullable);
                        builder.Property("required", input.IsRequired);
                        builder.EndObject();
                    }
                    builder.EndArray();
                }

                builder.EndObject();
            }
            builder.EndArray();
        }

        // Registration methods
        builder.Property("registrations");
        builder.StartObject();

        if (_hasServices)
            builder.Property("di", $"{_namespacePrefix}.ServiceRegistrationExtensions.AddPragmaticServices");

        if (_hasStartups)
        {
            var ns = string.IsNullOrEmpty(_namespacePrefix) ? "Pragmatic" : _namespacePrefix;
            builder.Property("startup", $"{ns}.PragmaticDependencies.AddPipelineSteps");
        }

        builder.EndObject();

        builder.EndObject();

        return builder.ToString();
    }
}
