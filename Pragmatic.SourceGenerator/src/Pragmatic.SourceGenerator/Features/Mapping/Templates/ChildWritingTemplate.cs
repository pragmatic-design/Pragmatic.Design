using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     Writes the children of an aggregate back onto it.
/// </summary>
/// <remarks>
///     <para>
///         Two templates need this and produce identical code: a patch's <c>ApplyPatch</c> and a
///         mutation's <c>ApplyToEntity</c>. They reach it from different transforms over different
///         models, which is exactly the shape that ends up as two copies drifting apart — a column of
///         audit data lived in four hand-written places in this repo and only one pair was tested.
///     </para>
///     <para>
///         What a child can be written with depends on what its DTO declares itself to be:
///         <c>[MapTo&lt;T&gt;]</c> yields <c>ToEntity()</c> and <c>ApplyTo()</c>, <c>[Patch&lt;T&gt;]</c>
///         yields <c>ApplyPatch()</c> and no way to create. A generator cannot see the members another
///         generator is about to write, so the attributes are what it reads.
///     </para>
/// </remarks>
internal abstract class ChildWritingTemplate : CSharpTemplate
{
    /// <summary>
    ///     Emits the write for one child.
    /// </summary>
    /// <param name="child">What is written, and what it can be written with.</param>
    /// <param name="receiver">The entity variable in the generated method — <c>entity</c> or <c>target</c>.</param>
    protected void RenderChildWrite(ChildWriteModel child, string receiver)
    {
        if (child.IsCollection)
            RenderCollectionWrite(child, receiver);
        else
            RenderNestedWrite(child, receiver);
    }

    /// <summary>
    ///     How a new child is built.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A child mutation has no <c>ToEntity()</c> and does not need one: the entity is built the
    ///         same way the invoker builds the root, and <c>ApplyToEntity</c> fills it. One call builds
    ///         and writes, so create and update share the same code path.
    ///     </para>
    ///     <para>
    ///         ⚠️ Through the generated factory when it can be named. <c>new</c> skips the entity's
    ///         <c>[DefaultValue]</c>, <c>[ComputedDefault]</c> and audit initialisers, and nothing
    ///         downstream puts them back — a child built with <c>new</c> while its parent used the
    ///         factory would be the same entity created two ways.
    ///     </para>
    /// </remarks>
    private static string Factory(ChildWriteModel child)
    {
        if (!child.IsChildMutation)
            return "d => d.ToEntity(),";

        // The factory, for the same reason the root uses it: `new` skips the entity's own defaults,
        // and a child deserves the same construction as its parent. Predicted conservatively — when
        // the factory takes parameters this cannot name it, and falls back.
        var build = child.ChildEntityHasParameterlessFactory
            ? $"{child.ChildEntityFullTypeName}.Create()"
            : $"new {child.ChildEntityFullTypeName}()";

        return $"d => {{ var __new = {build}; d.ApplyToEntity(__new); return __new; }},";
    }

    /// <summary>How an existing child is written in place.</summary>
    private static string Updater(ChildWriteModel child)
        => child.IsChildMutation ? "(d, e) => d.ApplyToEntity(e)"
            : child.CanPatch ? "(d, e) => d.ApplyPatch(e)"
            : "(d, e) => d.ApplyToLoaded(e)";

    /// <summary>
    ///     Writes many children through <c>MutationHelpers.MapOneToMany</c>, or — when the element is a
    ///     patch and cannot build an entity — by matching and patching what is already there.
    /// </summary>
    private void RenderCollectionWrite(ChildWriteModel child, string receiver)
    {
        var write = child.Collection!;
        var target = $"{receiver}.{child.TargetPropertyName}";

        if (write.Strategy == "Ignore")
        {
            Comment($"{child.PropertyName} is declared [CollectionStrategy(Ignore)] and is not written.");
            return;
        }

        if (child.CanCreate)
        {
            AppendLine("global::Pragmatic.Mapping.Mutation.MutationHelpers.MapOneToMany(");
            IncreaseIndent();
            AppendLine($"this.{child.PropertyName},");
            AppendLine($"{target},");
            // Replace matches nothing, so it resolves no key — but the helper's signature still asks
            // for the selectors, and its guards still reject null ones.
            AppendLine(write.DtoKeyProperty is { } dtoKey ? $"d => d.{dtoKey}," : "d => 0,");
            AppendLine(write.EntityKeyProperty is { } entityKey ? $"e => e.{entityKey}," : "e => 0,");
            AppendLine(Factory(child));
            AppendLine(Updater(child) + ",");
            AppendLine($"global::Pragmatic.Mapping.Mutation.CollectionStrategy.{write.Strategy});");
            DecreaseIndent();
            return;
        }

        // A patch-only element has no ToEntity(), so an unmatched item cannot be created. Matching and
        // patching what exists is the whole of what it can honestly do — the diagnostic says the rest.
        // Null-forgiving for the same reason the scalar assignments use it: neither a tracking check
        // nor an `is not null` on a property narrows it for the compiler.
        AppendLine($"foreach (var item in this.{child.PropertyName}!)");
        Block(() =>
        {
            AppendLine($"var existing = global::System.Linq.Enumerable.FirstOrDefault({target}, "
                + $"e => global::System.Object.Equals(e.{write.EntityKeyProperty}, item.{write.DtoKeyProperty}));");
            AppendLine("if (existing is not null)");
            Block(() => AppendLine("item.ApplyPatch(existing);"));
        });
    }

    /// <summary>
    ///     Writes one child: create it when it is not there, update it in place when it is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>This branch guards against null, as the mapping side does.</b> Without the guard the
    ///         two doors would disagree about what a null child means: through a <c>[MapTo]</c> DTO it
    ///         means "I am not telling you about this one", through a mutation it would reach
    ///         <c>MapOneToOne</c>, which reads null as detach — so an update that simply omitted an
    ///         optional child would <b>remove the link</b>.
    ///     </para>
    ///     <para>
    ///         Both doors mean <c>Merge</c>, and removing a link is something the shape has to say
    ///         out loud with <c>[ReferenceStrategy(Detach)]</c>.
    ///     </para>
    /// </remarks>
    private void RenderNestedWrite(ChildWriteModel child, string receiver)
    {
        if (child.ReferenceStrategy == "Ignore")
        {
            Comment($"{child.PropertyName} is declared [ReferenceStrategy(Ignore)] and is not written.");
            return;
        }

        var target = $"{receiver}.{child.TargetPropertyName}";
        var setter = child.EntityHasPrivateSetter
            ? $"v => {receiver}.Set{child.TargetPropertyName}(v)"
            : $"v => {target} = v";
        var detaches = child.ReferenceStrategy == "Detach";

        if (child.CanCreate)
        {
            void RenderCall()
            {
                AppendLine("global::Pragmatic.Mapping.Mutation.MutationHelpers.MapOneToOne(");
                IncreaseIndent();
                AppendLine($"this.{child.PropertyName},");
                AppendLine($"() => {target},");
                AppendLine($"{setter},");
                AppendLine(Factory(child));

                // Replace withholds the updater: the helper corrects what is there only when given
                // one, so not giving it is what "build a new child whatever is there" means.
                AppendLine(child.ReferenceStrategy == "Replace" ? "null);" : Updater(child) + ");");
                DecreaseIndent();
            }

            if (detaches)
            {
                RenderCall();
                return;
            }

            AppendLine($"if (this.{child.PropertyName} is not null)");
            Block(RenderCall);
            return;
        }

        // Patch-only: there is nothing to build the child from, so an absent child stays absent —
        // except under Detach, where removing a link needs no factory.
        var clear = child.EntityHasPrivateSetter
            ? $"{receiver}.Set{child.TargetPropertyName}(null);"
            : $"{target} = null;";

        if (detaches)
        {
            AppendLine($"if (this.{child.PropertyName} is null)");
            Block(() => AppendLine(clear));
            AppendLine($"else if ({target} is not null)");
            Block(() => AppendLine($"this.{child.PropertyName}.ApplyPatch({target});"));
            return;
        }

        AppendLine($"if ({target} is not null)");
        Block(() => AppendLine($"this.{child.PropertyName}!.ApplyPatch({target});"));
    }
}
