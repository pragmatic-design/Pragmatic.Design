using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Gives a class-level <c>[LogicKey]</c> part that names a generated foreign key its type, once the
///     relation graph has said which keys the entity gets.
/// </summary>
/// <remarks>
///     <para>
///         The per-symbol transform resolves a part against the properties the class declares and the
///         keys its own <c>[Relation.*]</c> produce. A key declared from the other side — the parent's
///         <c>[Relation.OneToMany&lt;Child&gt;]</c> puts <c>ParentId</c> on the child — is known only to
///         the graph, which runs over every entity at once. Rather than predict that naming a second
///         time here, the part waits with an empty type and takes it from
///         <see cref="EntityMetadataModel.GeneratedRelationProperties" />: one source for the name, one
///         for the type.
///     </para>
///     <para>
///         A part still empty afterwards names nothing the entity has, and <c>PRAG0636</c> says so where
///         the entity is validated.
///     </para>
/// </remarks>
internal static class LogicKeyPartResolver
{
    public static ImmutableArray<EntityMetadataModel> Complete(ImmutableArray<EntityMetadataModel> entities)
    {
        var builder = ImmutableArray.CreateBuilder<EntityMetadataModel>(entities.Length);

        foreach (var entity in entities)
        {
            if (!entity.LogicKeys.AsImmutableArray().Any(p => p.TypeName.Length == 0))
            {
                builder.Add(entity);
                continue;
            }

            var completed = ImmutableArray.CreateBuilder<LogicKeyPart>(entity.LogicKeys.Length);
            foreach (var part in entity.LogicKeys)
            {
                if (part.TypeName.Length > 0)
                {
                    completed.Add(part);
                    continue;
                }

                var generated = entity.GeneratedRelationProperties.AsImmutableArray()
                    .FirstOrDefault(gp => gp.Kind == RelationPropertyKind.ForeignKey && gp.Name == part.Name);

                completed.Add(generated is null ? part : part with { TypeName = generated.TypeName });
            }

            builder.Add(entity with { LogicKeys = completed.ToImmutable() });
        }

        return builder.ToImmutable();
    }
}
