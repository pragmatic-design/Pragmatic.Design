using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
///     Builds the <see cref="MutationModel" />s that <c>[Resource]</c> scaffolds for its write
///     capabilities, for injection into <c>ActionsFeature</c>.
/// </summary>
/// <remarks>
///     <para>
///         The write half is mutations, not <c>DomainAction</c>s whose whole body is
///         <c>new Entity(); Set…(); _repository.Add(entity);</c>. That would be a second write path: no
///         validation, no permission requirement, no commit strategy, no domain-event hand-over — so the
///         same write would behave differently depending on whether a mutation or the scaffolding
///         performed it. As mutations they go through the pipeline every hand-written write goes through, and the
///         customisation story comes with them: <c>ApplyAsync</c> is virtual on the runtime base, and a
///         developer's partial part of the generated type can override it.
///     </para>
///     <para>
///         Read stays out of here — reading one row is a <c>[Query(Single = true)]</c>, and List and
///         Search are queries.
///     </para>
/// </remarks>
internal static class ResourceMutationModelBuilder
{
    private const int CapCreate = 1;
    private const int CapUpdate = 4;
    private const int CapDelete = 8;
    private const int CapRestore = 64;

    public static ImmutableArray<MutationModel> Build(ResourceCrudModel model)
    {
        var caps = model.Resource.Capabilities;
        var builder = ImmutableArray.CreateBuilder<MutationModel>();

        if ((caps & CapCreate) != 0)
            builder.Add(Write(model, MutationModeValue.Create, "Create", model.CreateProperties));

        // Patch semantics: every field optional, and the generated ApplyToEntity only calls the setter
        // for the ones that arrived — an absent field must not blank a column.
        if ((caps & CapUpdate) != 0)
            builder.Add(Write(model, MutationModeValue.Update, "Update", model.UpdateProperties, optional: true));

        // Delete and Restore carry no input beyond the id: the invoker loads the entity and flips it.
        if ((caps & CapDelete) != 0)
            builder.Add(Write(model, MutationModeValue.Delete, "Delete", []));

        // Restore only where there is something to put back. ResourceCapabilities.All asks for it, so
        // gating here rather than refusing the flag is what keeps All meaning "everything that applies"
        // instead of failing every entity without soft delete.
        if ((caps & CapRestore) != 0 && model.IsSoftDelete)
            builder.Add(Write(model, MutationModeValue.Restore, "Restore", []));

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The type name a developer writes on their partial part to decorate this operation.
    /// </summary>
    public static string TypeNameFor(string entityTypeName, string kind)
        => $"Resource{kind}{entityTypeName}Mutation";

    private static MutationModel Write(
        ResourceCrudModel model,
        MutationModeValue mode,
        string kind,
        EquatableArray<ResourcePropertyInfo> properties,
        bool optional = false)
    {
        var resource = model.Resource;
        var typeName = TypeNameFor(resource.TypeName, kind);
        var ns = resource.Namespace;
        var entityFullTypeName = resource.FullTypeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? resource.FullTypeName
            : $"global::{resource.FullTypeName}";

        // Every mode but Create loads an existing row, and the invoker finds it through the mutation's
        // Id — bound from the route by the generated endpoint. The property itself is written by
        // MutationIdTemplate, which does the same for a mutation written by hand: the scaffolding used
        // to declare it among its inputs, which was a second place saying the same thing.
        var needsId = mode != MutationModeValue.Create;
        var simpleIdType = SimpleName(resource.IdType);

        var mapped = properties
            .Select(p => new MutationPropertyMapModel
            {
                MutationPropertyName = p.Name,
                MutationPropertyTypeName = TypeFor(p, optional),
                EntityPropertyName = p.Name,
                IsNullable = optional || p.IsNullable,
            })
            .ToImmutableArray();

        var inputBuilder = ImmutableArray.CreateBuilder<ActionPropertyModel>();
        if (needsId)
            inputBuilder.Add(new ActionPropertyModel
            {
                Name = "Id", TypeName = simpleIdType, IsRequired = true,
            });

        foreach (var p in properties)
            inputBuilder.Add(new ActionPropertyModel
            {
                Name = p.Name,
                TypeName = TypeFor(p, optional),
                IsRequired = !optional && p.IsRequired,
                IsNullable = optional || p.IsNullable,
            });

        var inputs = inputBuilder.ToImmutable();

        return new MutationModel
        {
            Namespace = ns,
            TypeName = typeName,
            FullTypeName = $"global::{ns}.{typeName}",
            Accessibility = "public",
            EntityTypeName = resource.TypeName,
            EntityFullTypeName = entityFullTypeName,
            EntityIdTypeName = QualifiedIdType(resource.IdType),
            Mode = mode,
            // The scaffolding exists to answer HTTP, so the boundary interface exposes it like any
            // other mutation with an endpoint.
            IsInternal = false,
            BelongsToTypeName = resource.BoundaryFullTypeName,
            // The group its hand-written neighbours are in. The namespace cannot say it: an entity's
            // is flat, and this operation borrows it.
            SubBoundaryName = model.GroupName,
            MappedProperties = mapped,
            InputProperties = inputs,
            IdPropertyName = needsId ? "Id" : null,
            GeneratesIdProperty = needsId,
            WritesOwnStore = true,
            // A [Resource] sits on an [Entity], so the attribute alone settles whether the nested
            // SoftDeleteFilter exists. The scaffolded Restore needs it: without it the generated load
            // lifts no filter and the marked row it exists to find is the one the filter hides.
            EntityDeclaresSoftDelete = model.IsSoftDelete,
            RequireAllPermissions = DefaultPermission(resource, mode),
            PermissionSource = SourceResourceDefault,
            InvalidReason = MutationInvalidReason.None,
        };
    }

    /// <summary>Source label written into the manifest for a permission this scaffolding chose.</summary>
    internal const string SourceResourceDefault = "resource-default";

    /// <summary>
    ///     The permission a scaffolded write requires unless the developer says otherwise:
    ///     <c>{boundary}.{entity}.{verb}</c>, the same value <c>PermissionCatalogBuilder</c> already
    ///     emits a constant for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Fail-closed, and it can be: the entity and its CRUD constants are generated together, so
    ///         unlike the opt-in auto-derivation this is not inventing a name no role could grant. The
    ///         scaffolded actions this replaces required <b>nothing</b> — anyone authenticated could
    ///         create, update and delete through them.
    ///     </para>
    ///     <para>
    ///         Carried as the permission <b>value</b>, not emitted as a <c>[RequirePermission]</c> on the
    ///         generated type, and that is deliberate. Attributes on a partial class are <i>combined</i>
    ///         across its parts, so a default written as an attribute would turn a developer's
    ///         <c>[RequirePermission(Something.Else)]</c> into an <b>additional</b> requirement instead of
    ///         a replacement — they could tighten the default but never loosen or change it.
    ///     </para>
    /// </remarks>
    private static EquatableArray<string> DefaultPermission(ResourceModel resource, MutationModeValue mode)
    {
        var verb = mode switch
        {
            MutationModeValue.Create => "create",
            MutationModeValue.Update => "update",
            // Restoring is undoing a delete, and whoever may delete may put it back. A separate verb
            // would need a constant nothing emits, which is how a permission fails open.
            MutationModeValue.Delete or MutationModeValue.Restore => "delete",
            _ => null,
        };

        if (verb is null)
            return EquatableArray<string>.Empty;

        var value = PermissionNaming.ValueForEntityMember(
            resource.BoundaryName?.ToLowerInvariant(), resource.TypeName, verb);

        return new EquatableArray<string>(ImmutableArray.Create(value));
    }

    private static string QualifiedIdType(string idType)
        => idType.StartsWith("global::", System.StringComparison.Ordinal) ? idType : $"global::{idType}";

    private static string SimpleName(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName.Substring(dot + 1);
    }

    /// <summary>Under patch semantics every field is optional, so nothing sent is nothing written.</summary>
    private static string TypeFor(ResourcePropertyInfo property, bool optional)
        => optional && !property.IsNullable ? $"{property.TypeName}?" : property.TypeName;
}
