using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Templates;

/// <summary>
///     Emits the mutation type <c>[Resource]</c> scaffolds for one write capability.
/// </summary>
/// <remarks>
///     <para>
///         Only the declaration and the input properties. Everything that makes it work —
///         <c>ApplyToEntity</c>, the invoker, <c>SetDependencies</c>, the request body, the endpoint —
///         is generated from the injected <c>MutationModel</c> by the stages every hand-written mutation
///         goes through. That is the point of the change: one write pipeline, not two.
///     </para>
///     <para>
///         <b>No <c>[RequirePermission]</c> here.</b> The default travels on the model as a permission
///         value. Attributes on a partial class combine across its parts, so writing the default as an
///         attribute would make a developer's own <c>[RequirePermission]</c> an extra requirement rather
///         than a replacement — they could tighten it and never change it.
///     </para>
/// </remarks>
internal sealed class ResourceMutationTemplate : CSharpTemplate
{
    private readonly MutationModel _model;
    private readonly string _kind;
    private readonly Models.ResourceOverrideModel? _decoration;

    public ResourceMutationTemplate(
        MutationModel model, string kind, Models.ResourceOverrideModel? decoration = null)
    {
        _model = model;
        _kind = kind;
        _decoration = decoration;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Resource";
    protected override string? SourceInfo => $"{_model.EntityTypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Resource] {_kind} for {_model.EntityTypeName}";

    public override Artifact RenderOutput()
        => new($"_Resource.{_model.EntityTypeName}.{_kind}.g.cs", ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Actions.Mutation");
        AddUsing("Pragmatic.Persistence.Entity");
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"SG-generated: {_kind.ToLowerInvariant()}s a <see cref=\"{_model.EntityTypeName}\"/>.");
        RenderPermissionRemark();

        AppendLine($"[Mutation(Mode = MutationMode.{_model.Mode})]");
        if (_model.BelongsToTypeName is not null)
            // Already fully qualified on the model — see ResourceModel.BoundaryFullTypeName.
            AppendLine($"[BelongsTo<{_model.BelongsToTypeName}>]");

        Class(_model.TypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true },
            baseType: $"Mutation<{_model.EntityTypeName}>");
    }

    /// <summary>
    ///     Names the permission in force in the generated file, since the attribute is deliberately
    ///     absent — and says so when it is the developer's own rather than the scaffolded default.
    /// </summary>
    private void RenderPermissionRemark()
    {
        var lines = ResourceDecorationRemark.Lines(
            _decoration, _model.TypeName, _model.RequireAllPermissions.AsImmutableArray());

        foreach (var line in lines)
            AppendLine(line);
    }

    private void RenderBody()
    {
        // Delete and Restore take nothing but the id, which the invoker binds from the route — and
        // which MutationIdTemplate declares, for a scaffolded operation and a hand-written one alike.
        // It stays in InputProperties because the boundary facade builds its overload from them.
        foreach (var prop in _model.InputProperties)
        {
            if (_model.GeneratesIdProperty && prop.Name == "Id")
                continue;

            var required = prop.IsRequired ? "required " : "";
            AppendLine($"public {required}{prop.TypeName} {prop.Name} {{ get; init; }}");
        }
    }
}
