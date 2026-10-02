using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     ToEntity and ApplyTo generation: creates entity from DTO and applies DTO to existing entity.
/// </summary>
internal sealed partial class MappingTemplate
{
    // ═══════════════════════════════════════════════════════════════════════════
    // ToEntity Generation
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderToEntity()
    {
        XmlSummary($"Creates a new {_model.TargetTypeName} entity from this DTO.");
        XmlReturns($"A new {_model.TargetTypeName} instance.");

        Method("ToEntity", RenderToEntityBody, $"global::{_model.TargetTypeFullName}");
    }

    private void RenderToEntityBody()
    {
        // BeforeToEntity hook — a user implementation can fully replace the construction.
        AppendLine($"global::{_model.TargetTypeFullName}? customResult = null;");
        AppendLine("BeforeToEntity(ref customResult);");
        If("customResult is not null", () =>
            AppendLine(_model.TargetTypeIsValueType ? "return customResult.Value;" : "return customResult;"));
        AppendLine();

        // The write path uses the [MapTo] property model for bidirectional DTOs.
        var propsToMap = _model.EffectiveWriteProperties
            .Where(p => !p.IsIgnored && p.Resolution != MappingResolution.None)
            .Where(p => !p.IsIdProperty || p.ForceIncludeId) // Exclude ID by default
            // A list of keys is not a value to assign: linking rows needs the change tracker, which
            // this form does not have. PRAG0335 reports the declaration rather than leaving silence.
            .Where(p => p.LinkNavigation is not { Length: > 0 })
            // Nothing to assign to: the entity computes it. PRAG0336 says so at compile time.
            .Where(p => !p.TargetIsReadOnly)
            .ToList();

        // Separate properties: direct vs nested (with TargetPath). A property the entity keeps private
        // cannot appear in an object initializer at all — it is written after construction, through
        // the setter the entity generator produced for exactly this.
        var directProps = propsToMap
            .Where(p => string.IsNullOrEmpty(p.TargetPath) && !p.TargetHasNonPublicSetter).ToList();
        var guardedProps = propsToMap
            .Where(p => string.IsNullOrEmpty(p.TargetPath) && p.TargetHasNonPublicSetter).ToList();
        var nestedProps = propsToMap.Where(p => !string.IsNullOrEmpty(p.TargetPath)).ToList();

        if (_model.ConstructorParameters.Length > 0)
        {
            // Use constructor — through the SAME expression the assignment paths use.
            //
            // ⚠️ Naming the DTO's property raw would make a target whose parameter type differs from
            // the DTO's a CS1503: `new Delivery(Reference, Channel)` with a string where a
            // DeliveryChannel is wanted. This is the one path that exists FOR such targets — a
            // constructor is what a value object, an immutable domain type or a record validating in
            // its ctor offers, and those are exactly the types whose parameters are not the wire types
            // (a string for an enum, a decimal for Money, a string for a strongly-typed id). It is
            // not specific to [MapConstructor]: best-match picks the parameterised constructor anyway,
            // so the same shape applies without the attribute.
            var ctorArgs = string.Join(", ", _model.ConstructorParameters.Select(ConstructorArgument));

            AppendLine($"var entity = new global::{_model.TargetTypeFullName}({ctorArgs})");
            Block(() =>
            {
                var setterProps = directProps
                    .Where(p => !_model.ConstructorParameters.Any(cp =>
                        string.Equals(cp.MatchingPropertyName, p.PropertyName, StringComparison.OrdinalIgnoreCase)));

                foreach (var prop in setterProps)
                {
                    var valueExpr = GenerateToEntityExpression(prop);
                    AppendLine($"{prop.PropertyName} = {valueExpr},");
                }
            });
            AppendLine(";");
        }
        else if (_model.TargetHasParameterlessFactory && !directProps.Any(p => p.TargetIsInitOnly))
        {
            // The generated factory, so a DTO builds the entity the way a mutation does. It is the
            // only path that applies [DefaultValue], [ComputedDefault] and the audit stamps: with
            // `new` the same row was born differently depending on which door it came through.
            //
            // ⚠️ Assignments, not an object initializer — one cannot follow a method call. That is
            // exactly why an init-only target keeps the branch below: `init` can be written in an
            // initializer and nowhere else, so building through the factory would not compile.
            AppendLine($"var entity = global::{_model.TargetTypeFullName}.Create();");

            foreach (var prop in directProps)
            {
                var valueExpr = GenerateToEntityExpression(prop);
                AppendLine($"entity.{prop.PropertyName} = {valueExpr};");
            }
        }
        else
        {
            // Use object initializer
            AppendLine($"var entity = new global::{_model.TargetTypeFullName}()");
            Block(() =>
            {
                foreach (var prop in directProps)
                {
                    var valueExpr = GenerateToEntityExpression(prop);
                    AppendLine($"{prop.PropertyName} = {valueExpr},");
                }
            });
            AppendLine(";");
        }

        // What the entity would not let an initializer touch.
        if (guardedProps.Count > 0)
        {
            AppendLine();
            foreach (var prop in guardedProps)
                AppendLine($"entity.Set{prop.PropertyName}({GenerateToEntityExpression(prop)});");
        }

        // Assign nested properties after creation
        if (nestedProps.Count > 0)
        {
            AppendLine();
            foreach (var prop in nestedProps)
                RenderNestedTargetAssignment(prop, GenerateToEntityExpression(prop), creating: true);
        }

        AppendLine();
        AppendLine("CustomizeToEntity(entity);");
        AppendLine("return entity;");
    }

    /// <summary>
    ///     What one constructor parameter is given: the mapped expression for the property it matches.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read on <c>EffectiveWriteProperties</c> and not on the assignment list, because the two
    ///         answer different questions. A property whose target is read-only is dropped from the
    ///         object initializer — there is nothing to assign to — and a constructor parameter is
    ///         exactly how such a target receives its value; excluding it here would pass the raw name
    ///         for the one case the parameter exists to serve.
    ///     </para>
    ///     <para>
    ///         An ignored property, or one whose mapping resolved to nothing, is not a value: the
    ///         parameter falls back to its own name so the compiler reports what the author declared,
    ///         rather than to <c>default</c>, which would compile and lose it.
    ///     </para>
    /// </remarks>
    private string ConstructorArgument(Models.ConstructorParameterModel parameter)
    {
        if (parameter.MatchingPropertyName is not { Length: > 0 } name)
            return "default";

        var property = _model.EffectiveWriteProperties.FirstOrDefault(p =>
            string.Equals(p.PropertyName, name, StringComparison.OrdinalIgnoreCase)
            && !p.IsIgnored
            && p.Resolution != MappingResolution.None);

        return property is null ? name : GenerateToEntityExpression(property);
    }

    /// <summary>
    ///     Write-side customization hooks, symmetric to the read-side BeforeMapping/CustomizeMapping.
    ///     No-op unless the user implements them (partial methods).
    /// </summary>
    private void RenderWritePartialMethods()
    {
        AppendLine();
        Comment("Partial methods for write-side customization");
        AppendLine($"partial void BeforeToEntity(ref global::{_model.TargetTypeFullName}? result);");
        AppendLine($"partial void CustomizeToEntity(global::{_model.TargetTypeFullName} entity);");
        if (!_model.HasMutationAttribute)
            AppendLine($"partial void CustomizeApplyTo(global::{_model.TargetTypeFullName} entity);");
    }

    /// <summary>
    ///     Assigns a dotted <c>Target</c> path. Creating an aggregate builds the shapes along the way;
    ///     updating one writes into what is already there, and says so when it is not.
    /// </summary>
    /// <param name="prop">The property whose <c>Target</c> path is being written.</param>
    /// <param name="valueExpr">The value to assign.</param>
    /// <param name="creating">
    ///     True inside <c>ToEntity</c>. The distinction is the whole point of this method: building a
    ///     new aggregate means every navigation on the path is missing by definition, so constructing
    ///     it is the work. Updating an existing one means a missing navigation is either an aggregate
    ///     that was never loaded or one that genuinely is not there — and <c>??= new()</c> would turn
    ///     both into an insert nobody asked for, while the old null-guard turned them into silence.
    /// </param>
    private void RenderNestedTargetAssignment(PropertyMappingModel prop, string valueExpr, bool creating)
    {
        if (prop.TargetPathIntermediates.Length == 0)
        {
            AppendLine($"entity.{prop.TargetPath} = {valueExpr};");
            return;
        }

        var mayConstruct = prop.TargetIntermediatesConstructible
            && (creating || !prop.TargetPathCrossesEntity);

        if (mayConstruct)
        {
            foreach (var prefix in prop.TargetPathIntermediates)
                AppendLine($"entity.{prefix} ??= new();");
            AppendLine($"entity.{prop.TargetPath} = {valueExpr};");
            return;
        }

        if (creating)
        {
            // Nothing to construct it with, and nothing to load either: this is a fresh entity.
            var guard = prop.TargetPathIntermediates[prop.TargetPathIntermediates.Length - 1].Replace(".", "?.");
            If($"entity.{guard} is not null", () => AppendLine($"entity.{prop.TargetPath} = {valueExpr};"));
            return;
        }

        foreach (var prefix in prop.TargetPathIntermediates)
            RenderNavigationPresenceGuard(prop, prefix);

        AppendLine($"entity.{prop.TargetPath} = {valueExpr};");
    }

    /// <summary>
    ///     Refuses to write through a navigation that is not there, naming it and what to do about it.
    /// </summary>
    /// <remarks>
    ///     The alternative it replaces was <c>if (entity.X is not null)</c> — the assignment quietly
    ///     skipped, the caller told the update had succeeded, and the field unchanged in the database.
    ///     Same reasoning as the read side's guard, and the same two ways out.
    /// </remarks>
    private void RenderNavigationPresenceGuard(PropertyMappingModel prop, string prefix)
    {
        var message =
            $"{_model.TypeName} writes {prop.TargetPath}, but {prefix} is not loaded on this "
            + $"{_model.TargetTypeName}. Load it — [EagerLoad(\\\"{prefix}\\\")] on the mutation — or "
            + $"write {prefix} itself instead of a property of it.";

        AppendLine($"if (entity.{prefix} is null)");
        IncreaseIndent();
        AppendLine($"throw new global::System.InvalidOperationException(\"{message}\");");
        DecreaseIndent();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ApplyTo Generation (Update Existing Entity)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>What the write body may assume about the entity in front of it.</summary>
    private enum ApplyMode
    {
        /// <summary>The caller promises the written navigations are loaded; nothing is asked.</summary>
        Loaded,

        /// <summary>A context is in scope, so each navigation is reached through its entry.</summary>
        Tracked
    }

    /// <summary>The properties the write path assigns.</summary>
    private List<PropertyMappingModel> WriteProperties()
        // The write path uses the [MapTo] property model for bidirectional DTOs.
        => _model.EffectiveWriteProperties
            .Where(p => !p.IsIgnored && p.Resolution != MappingResolution.None)
            .Where(p => !p.IsIdProperty || p.ForceIncludeId) // Exclude ID by default
            .ToList();

    /// <summary>Whether this DTO writes through a navigation at all.</summary>
    /// <remarks>
    ///     The question that decides which forms exist: a DTO that only assigns scalars cannot be
    ///     merging into something unloaded, so it keeps the plain <c>ApplyTo(entity)</c>.
    /// </remarks>
    private bool WritesANavigation()
        => WriteProperties().Any(p =>
            (p.CollectionWrite is { } c && c.Strategy != "Ignore")
            || p is { IsNestedDto: true, NestedDtoType: not null and not "" }
            // Linking by id writes a navigation too, and it is the one write that cannot be done at
            // all without the tracker — so the plain form must not be offered for it either.
            || p.LinkNavigation is { Length: > 0 });

    /// <summary>The nested call each mode makes on a child DTO.</summary>
    private static string NestedUpdate(ApplyMode mode)
        => mode == ApplyMode.Tracked ? "ApplyTo(e, context)" : "ApplyToLoaded(e)";

    /// <summary>The write body itself, under a name that states its precondition.</summary>
    /// <remarks>
    ///     ⚠️ Generated unconditionally, EF or not, because generated code elsewhere names it without
    ///     being able to see this model: <c>ChildWritingTemplate</c> writes a mutation's
    ///     <c>ApplyToEntity</c>, an override with no context to pass on. That path is tracked
    ///     <em>and</em> already included by the invoker, so the precondition holds — which is why the
    ///     name says "Loaded" and not "Detached". Detached would have been false for exactly the
    ///     caller that needs it.
    /// </remarks>
    private void RenderApplyToLoaded()
    {
        AppendLine();
        // "an existing X" rather than "a X": the type name is substituted, so an article chosen here
        // is wrong for every entity starting with a vowel — thirty ungrammatical doc comments.
        XmlSummary($"Applies this DTO to an existing {_model.TargetTypeName} whose written "
                   + "navigations are already loaded.");
        XmlParam("entity", "The entity to update.");
        XmlRemarks("The caller promises the navigations this DTO writes are loaded; nothing is "
                   + "checked. Merging into a collection the context never loaded writes every "
                   + "element as new. Where a DbContext is at hand, prefer ApplyTo(entity, context), "
                   + "which asks EF instead of trusting.");

        Method("ApplyToLoaded", () => RenderApplyToBody(ApplyMode.Loaded), "void",
            [new MethodParameter($"global::{_model.TargetTypeFullName}", "entity")]);
    }

    /// <summary>The plain form — generated only for a DTO that writes no navigation.</summary>
    /// <remarks>
    ///     ⚠️ The call that could be wrong is <c>dto.ApplyTo(entity)</c> on a DTO that merges
    ///     children, because <c>ICollection</c> cannot say whether it was loaded and an unloaded one is
    ///     indistinguishable from an empty one. Rather than warn at call sites that are usually right,
    ///     the form is not generated: where EF is present and the DTO writes a navigation, the entry
    ///     points are <c>ApplyTo(entity, context)</c> and <c>ApplyToLoaded(entity)</c>, and the
    ///     ambiguous call does not compile.
    /// </remarks>
    private void RenderApplyTo()
    {
        if (_model.HasEfCore && WritesANavigation())
            return;

        AppendLine();
        XmlSummary($"Applies the non-null properties of this DTO to an existing {_model.TargetTypeName} entity.");
        XmlParam("entity", "The entity to update.");

        Method("ApplyTo", () => AppendLine("ApplyToLoaded(entity);"), "void",
            [new MethodParameter($"global::{_model.TargetTypeFullName}", "entity")]);
    }

    private void RenderApplyToBody(ApplyMode mode)
    {
        AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(entity);");
        if (mode == ApplyMode.Tracked)
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(context);");
        AppendLine();

        foreach (var prop in WriteProperties())
        {
            RenderApplyToProperty(prop, mode);
        }

        AppendLine();
        AppendLine("CustomizeApplyTo(entity);");
    }

    /// <summary>Writes the tracked form: each navigation is reached through its entry.</summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A keyed merge decides what to keep by looking at what is there, and in EF a
    ///         collection that was never included and an empty one are the same object, so every
    ///         incoming element looks new: a write that sends the same two rows back writes four, with
    ///         no exception and a successful <c>SaveChanges</c>.
    ///     </para>
    ///     <para>
    ///         The guard is the parameter type, not a check bolted on top: <c>EfMutationHelpers</c>
    ///         takes the <c>CollectionEntry</c>/<c>ReferenceEntry</c>, which carries <c>IsLoaded</c>
    ///         and cannot be obtained without a context. The question is answered at each navigation
    ///         actually written, at every level, instead of being predicted up front from a path list.
    ///     </para>
    /// </remarks>
    private void RenderApplyToWithContext()
    {
        AppendLine();
        XmlSummary($"Applies this DTO to a tracked {_model.TargetTypeName}, refusing to merge into a "
                   + "navigation the context never loaded.");
        XmlParam("entity", "The tracked entity to update.");
        XmlParam("context", "The context tracking it, asked whether each navigation is loaded.");
        XmlRemarks("Merging into an unloaded collection writes every incoming element as new, and "
                   + "into an unloaded reference builds a second child beside the one already there. "
                   + "This form turns that into an exception naming the navigation, instead of "
                   + "duplicate rows and a successful save.");

        Method("ApplyTo", () =>
        {
            if (!WritesANavigation())
            {
                AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(entity);");
                AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(context);");
                AppendLine();
                AppendLine("ApplyToLoaded(entity);");
                return;
            }

            RenderApplyToBody(ApplyMode.Tracked);
        }, "void",
        [
            new MethodParameter($"global::{_model.TargetTypeFullName}", "entity"),
            new MethodParameter("global::Microsoft.EntityFrameworkCore.DbContext", "context")
        ]);
    }

    /// <summary>
    ///     Merges a single navigation instead of replacing it: update what is there, build only what
    ///     is not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A null in the DTO leaves the navigation alone rather than detaching it. That is the
    ///         partial-update reading the scalar branch already uses — "I am not telling you about
    ///         this" rather than "remove it" — and it is the conservative one. <c>MapOneToOne</c>
    ///         reads null as detach, which is why this does not simply hand the null through: the
    ///         guard is what turns the helper's detach into a merge.
    ///     </para>
    ///     <para>
    ///         Which is also why <c>[ReferenceStrategy]</c> is expressed here and not in the helper.
    ///         <c>Detach</c> drops the guard and lets the null reach the branch that was always
    ///         there; <c>Replace</c> hands no updater, so the helper builds instead of correcting;
    ///         <c>Ignore</c> emits nothing at all.
    ///     </para>
    ///     <para>
    ///         The tracked form passes the reference entry <em>and</em> the setter. The entry answers
    ///         the load question; the setter is kept because an entity that keeps its state private is
    ///         written through the setter its own generator produced, and routing the write through
    ///         EF instead would quietly change which code runs.
    ///     </para>
    /// </remarks>
    private void RenderSingleNavigationMerge(PropertyMappingModel prop, ApplyMode mode)
    {
        var name = prop.TargetPath ?? prop.PropertyName;
        var target = $"entity.{name}";
        var setter = prop.TargetHasNonPublicSetter
            ? $"v => entity.Set{name}(v)"
            : $"v => {target} = v";

        // A dotted target is not a navigation of this entity, so EF has no entry for it: that write
        // keeps the plain form whatever the mode.
        var throughTheEntry = mode == ApplyMode.Tracked && !name.Contains('.');

        if (prop.ReferenceStrategy == "Ignore")
        {
            Comment($"{prop.PropertyName} is declared [ReferenceStrategy(Ignore)] and is not written.");
            return;
        }

        void RenderCall()
        {
            AppendLine(throughTheEntry
                ? "global::Pragmatic.Mapping.EFCore.Mutation.EfMutationHelpers.MapOneToOne("
                : "global::Pragmatic.Mapping.Mutation.MutationHelpers.MapOneToOne(");
            IncreaseIndent();
            AppendLine($"this.{prop.PropertyName},");
            AppendLine(throughTheEntry
                ? $"context.Entry(entity).Reference(__e => __e.{name}),"
                : $"() => {target},");
            AppendLine($"{setter},");
            AppendLine(ChildFactory(prop));

            // Replace passes no updater: the helper corrects what is there only when it is given one,
            // so withholding it is what "build a new child whatever is there" means. The parameter is
            // still written, as null, because the two helpers differ in what follows it.
            AppendLine(prop.ReferenceStrategy == "Replace"
                ? "null);"
                : ChildUpdater(prop, NestedUpdate(mode)) + ");");
            DecreaseIndent();
        }

        // Detach is the whole point of the attribute: without the guard the null reaches MapOneToOne,
        // which has read it as "remove the link" since it was written. The guard is the merge.
        if (prop.ReferenceStrategy == "Detach")
        {
            RenderCall();
            return;
        }

        AppendLine($"if (this.{prop.PropertyName} is not null)");
        Block(RenderCall);
    }

    /// <summary>
    ///     Links a navigation to rows named only by their keys — <c>[LinkIds]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only on the tracked path. Attaching a row named by its key means marking it
    ///         <c>Unchanged</c> in the change tracker, and there is no way to do that without a
    ///         <c>DbContext</c>: without one EF would take the stub for a new row and insert it, which
    ///         is a duplicate key rather than a link.
    ///     </para>
    ///     <para>
    ///         So the context-free <c>ToEntity</c> writes nothing for it and says so in a comment. It
    ///         is not silence: <c>PRAG0335</c> reports the declaration that can never take effect.
    ///     </para>
    /// </remarks>
    private void RenderLinkIds(PropertyMappingModel prop, ApplyMode mode)
    {
        if (mode != ApplyMode.Tracked)
        {
            // A mutation is not missing the write — it is somewhere else. The invoker holds the
            // repository, so Actions emits LinkChosenRowsAsync and links through INavigationLinker.
            // Saying "needs the tracked form" here would send a reader looking for a bug that is a
            // working feature one file over.
            Comment(_model.IsMutationBody
                ? $"{prop.PropertyName} links by id: the invoker does it, in LinkChosenRowsAsync."
                : $"{prop.PropertyName} links by id, which needs the tracked form — PRAG0335.");
            return;
        }

        AppendLine("global::Pragmatic.Mapping.EFCore.Mutation.EfMutationHelpers.MapIdsToMany(");
        IncreaseIndent();
        AppendLine($"this.{prop.PropertyName},");
        AppendLine($"context.Entry(entity).Collection(__e => __e.{prop.LinkNavigation}),");
        AppendLine($"\"{prop.LinkKey}\",");
        AppendLine($"() => {prop.LinkEntityFullTypeName}.Create(),");
        AppendLine($"global::Pragmatic.Mapping.Mutation.CollectionStrategy.{prop.LinkStrategy});");
        DecreaseIndent();
    }

    private void RenderApplyToProperty(PropertyMappingModel prop, ApplyMode mode)
    {
        // The entity computes it; there is no setter to call. PRAG0336 reports the declaration.
        if (prop.TargetIsReadOnly)
            return;

        // A list of keys chooses which rows the navigation points at, without loading them. Checked
        // first: the property is a collection of scalars, so every branch below would treat it as a
        // value to assign.
        if (prop.LinkNavigation is { Length: > 0 })
        {
            RenderLinkIds(prop, mode);
            return;
        }

        // A collection of DTOs is synchronised, not replaced. Assigning a freshly built list to a
        // tracked entity discards every existing child — identity, audit columns, soft-delete state
        // and anything pointing at the old rows — which is right when creating and almost never what
        // an update means.
        if (prop.CollectionWrite is { } collection)
        {
            RenderCollectionSync(prop, collection, mode);
            return;
        }

        // ⚠️ And a single navigation is merged for exactly the same reason. Assigning
        // this.Address.ToEntity() to a tracked entity builds a new row and abandons the old one: the
        // paragraph above says why that is wrong for a list, and nothing about it was ever true only
        // for lists. Measured against PostgreSQL before this branch existed — updating an address
        // moved it from id 1 to id 3, silently, on every update.
        //
        // MapOneToOne already did the right thing and nobody called it from here: it updates in place
        // when the navigation is there and builds only when it is not.
        if (prop is { IsNestedDto: true, NestedDtoType: not null and not "" })
        {
            RenderSingleNavigationMerge(prop, mode);
            return;
        }

        var valueExpr = GenerateToEntityExpression(prop);

        // Dotted Target paths go through the null-safe nested assignment (??= new() / guard).
        var hasNestedTarget = prop.TargetPathIntermediates.Length > 0;

        // An entity that keeps its state private is written through the setter its own generator
        // produced. Assigning it directly would make [MapTo] over a properly encapsulated entity
        // fail with CS0272.
        var write = prop.TargetHasNonPublicSetter
            ? $"entity.Set{prop.TargetPath ?? prop.PropertyName}({valueExpr});"
            : $"entity.{prop.TargetPath ?? prop.PropertyName} = {valueExpr};";

        // For nullable properties, only apply if not null (partial update semantics)
        if (prop.IsNullable)
        {
            // Check if property has value before applying
            If($"this.{prop.PropertyName} is not null", () =>
            {
                if (hasNestedTarget)
                    RenderNestedTargetAssignment(prop, valueExpr, creating: false);
                else
                    AppendLine(write);
            });
        }
        else if (hasNestedTarget)
        {
            RenderNestedTargetAssignment(prop, valueExpr, creating: false);
        }
        else
        {
            // Non-nullable: always apply
            AppendLine(write);
        }
    }

    /// <summary>
    ///     Writes a collection of DTOs onto the entity's collection through <c>MapOneToMany</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The runtime is <c>Pragmatic.Mapping.Mutation.MutationHelpers</c>, which does this
    ///         correctly — add, update, remove, reject duplicate keys. Without a caller in generated
    ///         code, every DTO collection would go through a wholesale replacement instead.
    ///     </para>
    ///     <para>
    ///         <c>Ignore</c> emits nothing at all rather than a call that returns immediately: a
    ///         read-only collection should leave no trace in the generated update.
    ///     </para>
    ///     <para>
    ///         ⚠️ And a null collection is not written, for the same reason a null single navigation
    ///         is not: absent is "I am not telling you about these", not a value. Emitted bare, the call
    ///         would let the null reach <c>MapOneToMany</c>, which reads it as an empty incoming set —
    ///         and under <c>Sync</c> an empty incoming set removes every child that was there. An empty list still means "none of them": that is the strategy's reading, and
    ///         the guard leaves it alone.
    ///     </para>
    /// </remarks>
    private void RenderCollectionSync(
        PropertyMappingModel prop, CollectionWriteModel collection, ApplyMode mode)
    {
        if (collection.Strategy == "Ignore")
        {
            Comment($"{prop.PropertyName}: [CollectionStrategy(Ignore)] — read-only, never written back.");
            return;
        }

        AppendLine($"if (this.{prop.PropertyName} is not null)");
        Block(() => RenderCollectionSyncCall(prop, collection, mode));
    }

    private void RenderCollectionSyncCall(
        PropertyMappingModel prop, CollectionWriteModel collection, ApplyMode mode)
    {
        var strategy = $"global::Pragmatic.Mapping.Mutation.CollectionStrategy.{collection.Strategy}";
        var elementDto = prop.ElementDtoType ?? "object";

        // Replace rebuilds from scratch, so it matches nothing: the key selectors are still required
        // by the signature and are never invoked.
        var dtoKey = collection.DtoKeyProperty is { } dk ? $"d => d.{dk}" : "d => 0";
        var entityKey = collection.EntityKeyProperty is { } ek ? $"e => e.{ek}" : "e => 0";

        var target = prop.TargetPath ?? prop.PropertyName;

        // A dotted target is not a navigation of this entity, so EF has no entry for it.
        var throughTheEntry = mode == ApplyMode.Tracked && !target.Contains('.');

        AppendLine(throughTheEntry
            ? "global::Pragmatic.Mapping.EFCore.Mutation.EfMutationHelpers.MapOneToMany("
            : "global::Pragmatic.Mapping.Mutation.MutationHelpers.MapOneToMany(");
        IncreaseIndent();
        AppendLine($"this.{prop.PropertyName},");
        AppendLine(throughTheEntry
            ? $"context.Entry(entity).Collection(__e => __e.{target}),"
            : $"entity.{target},");
        AppendLine($"{dtoKey},");
        AppendLine($"{entityKey},");
        AppendLine(ChildFactory(prop));
        AppendLine(ChildUpdater(prop, NestedUpdate(mode)) + ",");
        AppendLine($"{strategy});");
        DecreaseIndent();
    }

    /// <summary>
    ///     How a child that does not exist yet is created.
    /// </summary>
    /// <remarks>
    ///     A DTO can build itself (<c>ToEntity</c>); a mutation cannot — the invoker loads the entity —
    ///     so the new child comes from the entity's factory and <c>ApplyToEntity</c> fills it. The same
    ///     shape as <c>ChildWritingTemplate</c>, because it is the same problem.
    /// </remarks>
    private static string ChildFactory(PropertyMappingModel prop)
        => prop is { ChildIsMutation: true, ChildEntityFullTypeName: not null and not "" }
            ? $"d => {{ var __new = {prop.ChildEntityFullTypeName}.Create(); d.ApplyToEntity(__new); return __new; }},"
            : "d => d.ToEntity(),";

    /// <summary>How a child that already exists is updated.</summary>
    private static string ChildUpdater(PropertyMappingModel prop, string nested)
        => prop.ChildIsMutation ? "(d, e) => d.ApplyToEntity(e)" : $"(d, e) => d.{nested}";

    /// <summary>
    ///     Generates the expression for mapping a DTO property to entity property in ToEntity.
    /// </summary>
    private static string GenerateToEntityExpression(PropertyMappingModel prop)
    {
        // Nested DTO - call ToEntity()
        if (prop.IsNestedDto && !string.IsNullOrEmpty(prop.NestedDtoType))
        {
            // Handle nullable nested DTO
            var nullCheck = prop.IsNullable ? "?" : "";
            return $"this.{prop.PropertyName}{nullCheck}.ToEntity()";
        }

        // Collection of DTOs - call ToEntity() on each element. Materialize what the ENTITY declares
        // (TargetCollectionKind), falling back to the DTO's kind for unresolved targets.
        if (prop.CollectionKind != CollectionKind.None && prop.IsElementDto &&
            !string.IsNullOrEmpty(prop.ElementDtoType))
        {
            var effectiveKind = prop.TargetCollectionKind != CollectionKind.None
                ? prop.TargetCollectionKind
                : prop.CollectionKind;
            var toCollection = effectiveKind switch
            {
                CollectionKind.List => ".ToList()",
                CollectionKind.Array => ".ToArray()",
                CollectionKind.HashSet => ".ToHashSet()",
                CollectionKind.ImmutableArray => ".ToImmutableArray()",
                CollectionKind.ImmutableList => ".ToImmutableList()",
                _ => ".ToList()"
            };

            // ImmutableArray<T> is a struct: `?.` is invalid — guard with IsDefaultOrEmpty instead.
            return prop.CollectionKind == CollectionKind.ImmutableArray
                ? $"this.{prop.PropertyName}.IsDefaultOrEmpty ? [] : this.{prop.PropertyName}.Select(x => x.ToEntity()){toCollection}"
                : $"this.{prop.PropertyName}?.Select(x => x.ToEntity()){toCollection} ?? []";
        }

        // [MapConverter] on the write path — the IValueConverter contract documents
        // ConvertBack for DTO→entity mapping. Cached static instance, no per-call alloc.
        if (prop.HasConverter)
            return $"{ConverterFieldName(prop.ConverterType!)}.ConvertBack(this.{prop.PropertyName})";

        // And the conversions the framework does by itself — string to enum, numeric widening, the
        // rest. SourcePropertyType is the ENTITY's type here: on this side the roles are inverted.
        if (prop is { RequiresConversion: true, TargetFullTypeName: not null and not "" })
            return Analysis.TypeConversionHelper.GenerateConversionExpression(
                $"this.{prop.PropertyName}",
                prop.TargetFullTypeName!,
                prop.Conversion,
                prop.IsNullable,
                prop.Format,
                prop.EnumOnUnknown,
                prop.EnumAliases.AsImmutableArray());

        // Nullable value type (int?) → non-nullable value-type entity property (int): unwrap.
        // In ApplyTo this sits inside an `is not null` guard (value present); in ToEntity a null DTO
        // value maps to the entity type's default — symmetric with the read-side auto-default.
        if (prop.NeedsNullableValueUnwrap)
            return $"this.{prop.PropertyName}.GetValueOrDefault()";

        // Simple property
        return $"this.{prop.PropertyName}";
    }
}
