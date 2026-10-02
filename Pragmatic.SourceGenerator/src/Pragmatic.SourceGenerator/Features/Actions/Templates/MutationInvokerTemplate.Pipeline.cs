using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Pipeline rendering methods: entity loading, creation, mode, persistence, and save.
/// </summary>
internal sealed partial class MutationInvokerTemplate
{
    private void RenderLoadEntityAsync()
    {
        var mutationType = _model.FullTypeName;
        var entityType = _model.EntityFullTypeName;

        XmlSummary("Loads the entity by ID with optional includes.");

        if (_model.IdPropertyName is null || _model.EntityIdTypeName is null)
        {
            // No ID — no async work needed, avoid CS1998
            AppendLine(
                $"protected override global::System.Threading.Tasks.Task<{entityType}?> LoadEntityAsync({mutationType} mutation, global::System.Threading.CancellationToken ct)");
            Block(() =>
            {
                AppendLine($"return global::System.Threading.Tasks.Task.FromResult<{entityType}?>(null);");
            });
            return;
        }

        AppendLine(
            $"protected override async global::System.Threading.Tasks.Task<{entityType}?> LoadEntityAsync({mutationType} mutation, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {

            // [WithoutFilter<T>] / [FilterMode] — disable specific filters before loading
            if (_model.HasFilterOverrides)
            {
                RenderFilterOverrideScopes();
            }

            // A restore lifts the filter that hides the row it is restoring, and no other. Both halves
            // of the load name one filter: this one names the entity's own generated SoftDeleteFilter —
            // what DefaultQueryFilterProvider compares filter.GetType() against — and the EF half below
            // names "SoftDelete" in IgnoreQueryFilters. DisableAll() stood here, and
            // QueryFilterToggle.IsDisabled answers true for every filter while such a scope is open, so
            // the ownership and access-scope filters went down with the soft-delete one and a restore by
            // id reached another owner's deleted row inside the same tenant.
            if (_model.IsRestore && _model.EntityDeclaresSoftDelete)
            {
                AppendLine($"using var _ = _filterToggle?.Disable(typeof({entityType}.SoftDeleteFilter));");
            }

            // Merge explicit includes with auto-includes for cascade soft-delete targets
            var allIncludes = _model.HasIncludes
                ? _model.Includes.ToList()
                : new List<string>();

            if (_model.SoftDelete?.HasCascadeTargets == true)
            {
                foreach (var target in _model.SoftDelete!.CascadeTargets)
                {
                    if (!allIncludes.Contains(target.PropertyName))
                        allIncludes.Add(target.PropertyName);
                }
            }

            // The children this mutation writes. Not an optimisation: a keyed merge decides what to
            // remove by looking at what is there, so against a collection nobody loaded it removes
            // nothing and adds everything — a duplicate per element, on the first update.
            foreach (var child in _model.Children)
            {
                if (child.IsWritable && !allIncludes.Contains(child.TargetPropertyName))
                    allIncludes.Add(child.TargetPropertyName);
            }

            // ⚠️ And what the children write in turn. A child DTO carrying children of its own merges
            // them the same way — the generated ApplyTo passes itself as the updater — so the same
            // reasoning applies one level down, where including only the child leaves the grandchildren
            // unloaded. Measured on a three-level shape: two rows became four, silently, on a write that
            // sent the same two back. The paths come from the child DTO's own WrittenNavigations —
            // what it writes, not what it reads. RequiredNavigations answers the other question, and
            // using it here loaded whatever the child's read shape happened to reach while missing a
            // navigation it writes and does not read. They are emitted at runtime because that list is
            // a generated static, not something this transform can enumerate.
            // CanCreate, not IsWritable: a patch-only child is writable and has no members from
            // Mapping at all, so naming one breaks a file the author cannot edit.
            var deepWrites = _model.Children
                .Where(c => c is { IsWritable: true, CanCreate: true })
                .Select(c => (c.TargetPropertyName, c.ChildDtoFullTypeName))
                .ToList();

            // What the response shape needs. Mapping already works out the navigations a DTO reaches
            // through — explicit paths, auto-flattening, nested DTOs, collections — and publishes them
            // on the DTO, so this iterates that list instead of deriving a second, poorer version of
            // the same rule. Without it, FromEntity walks entity.Property.Name on a navigation that a
            // load-by-id never populated.
            // Only while the DTO is the response: a mutation that answers its key never builds it
            // (PRAG0535 on the endpoint), and loading what it reads would be a join for nobody.
            var responseNavigations = _model.EffectiveReturnType == MutationReturnTypeValue.Entity
                ? _model.ResponseDtoFullTypeName
                : null;

            if (allIncludes.Count > 0 || responseNavigations is not null)
            {
                AppendLine("var query = _repository.Query();");

                // Restore must bypass the "SoftDelete" named EF filter to find the deleted row, but MUST
                // keep the "Tenant" named filter active — a blanket IgnoreQueryFilters() dropped tenant
                // isolation too, letting a caller restore another tenant's soft-deleted row by id.
                if (_model.IsRestore)
                {
                    AppendLine("query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query, new[] { \"SoftDelete\" });");
                }

                foreach (var include in allIncludes)
                {
                    AppendLine(
                        $"query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(query, \"{include}\");");
                }

                foreach (var (navigation, childDto) in deepWrites)
                {
                    AppendLine($"foreach (var __deep in {childDto}.WrittenNavigations)");
                    Block(() => AppendLine(
                        "query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
                        + $".Include(query, \"{navigation}.\" + __deep);"));
                }

                if (responseNavigations is not null)
                {
                    AppendLine($"foreach (var __navigation in {responseNavigations}.RequiredNavigations)");
                    Block(() => AppendLine(
                        "query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
                        + ".Include(query, __navigation);"));
                }

                AppendLine(
                    $"return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, e => e.PersistenceId.Equals(mutation.{_model.IdPropertyName}), ct).ConfigureAwait(false);");
            }
            else if (_model.IsRestore)
            {
                // Restore without includes — bypass ONLY the "SoftDelete" named EF filter, keeping the
                // "Tenant" filter active so a cross-tenant restore-by-id returns no row (see above).
                AppendLine("var query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(_repository.Query(), new[] { \"SoftDelete\" });");
                AppendLine(
                    $"return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, e => e.PersistenceId.Equals(mutation.{_model.IdPropertyName}), ct).ConfigureAwait(false);");
            }
            else
            {
                AppendLine(
                    $"return await _repository.GetByIdAsync(mutation.{_model.IdPropertyName}, ct).ConfigureAwait(false);");
            }
        });
    }

    /// <summary>
    ///     The top-up for an entity the caller handed over: load what is missing, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Emitted only when the mutation writes children — with nothing to merge there is nothing
    ///         a partial graph can lose, and the base method already does nothing.
    ///     </para>
    ///     <para>
    ///         Three costs, and the common one is free: the planner reads the change tracker without
    ///         querying, so a graph that is already complete costs <b>zero</b>; anything missing costs
    ///         <b>one</b> read with exactly those includes, never one per row. The result is discarded
    ///         on purpose — EF's identity resolution attaches the rows to the instance already tracked,
    ///         which is the one being written.
    ///     </para>
    ///     <para>
    ///         ⚠️ A detached entity is attached first, because writing into a graph the context does
    ///         not track never reaches <c>SaveChanges</c>. Attaching does not make its children
    ///         authoritative: EF does not mark <c>IsLoaded</c> on an attach, so the planner still
    ///         reports them missing and the read happens — measured, not assumed.
    ///     </para>
    /// </remarks>
    private void RenderEnsureWrittenGraphLoaded()
    {
        var deepWrites = _model.Children
            .Where(c => c is { IsWritable: true, CanCreate: true })
            .Select(c => (c.TargetPropertyName, c.ChildDtoFullTypeName))
            .ToList();

        var writableChildren = _model.Children.Where(c => c.IsWritable).ToList();
        if (writableChildren.Count == 0)
            return;

        var entityType = _model.EntityFullTypeName;
        AppendLine();
        XmlSummary("Loads only the written navigations a handed-over entity is missing.");

        AppendLine(
            $"protected override async global::System.Threading.Tasks.Task EnsureWrittenGraphLoadedAsync({entityType} entity, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            AppendLine("var __paths = new global::System.Collections.Generic.List<string>();");

            foreach (var child in writableChildren)
                AppendLine($"__paths.Add(\"{child.TargetPropertyName}\");");

            foreach (var (navigation, childDto) in deepWrites)
            {
                AppendLine($"foreach (var __deep in {childDto}.WrittenNavigations)");
                Block(() => AppendLine($"__paths.Add(\"{navigation}.\" + __deep);"));
            }

            AppendLine();
            Comment("A capability, not a DbContext: this assembly's invoker stays free of EF Core, and");
            Comment("the repository does the top-up where the context already is.");
            AppendLine($"if (_repository is global::Pragmatic.Persistence.Repository.INavigationLoader<{entityType}> __loader)");
            Block(() => AppendLine("await __loader.EnsureLoadedAsync(entity, __paths, ct).ConfigureAwait(false);"));
        });
    }

    /// <summary>
    ///     The rows this mutation chose by key — <c>[LinkIds]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not written by <c>ApplyToEntity</c>, and it is not an omission: attaching a row named
    ///         only by its key means marking it <c>Unchanged</c> in the change tracker, which needs a
    ///         <c>DbContext</c>. This assembly has none by design, so the write goes through the same
    ///         kind of capability the load already uses — asked of the repository, where the context
    ///         is.
    ///     </para>
    ///     <para>
    ///         ⚠️ The related entity's type is a type argument, not a name: creating one by reflection
    ///         is ruled out, since new code uses no reflection, and <c>new TRelated()</c> would exclude every entity with a
    ///         required member. The factory the generated entity already has answers both.
    ///     </para>
    /// </remarks>
    private void RenderLinkChosenRows()
    {
        if (_model.Links.Length == 0)
            return;

        var entityType = _model.EntityFullTypeName;
        var mutationType = _model.FullTypeName;

        AppendLine();
        AppendLine(
            $"protected override async global::System.Threading.Tasks.Task LinkChosenRowsAsync({mutationType} mutation, {entityType} entity, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            // Not a silent return. [LinkIds] declared which rows this mutation points at; a repository
            // that cannot link them means the declaration takes no effect, and the request that asked
            // for it would answer 200 having changed nothing. Same call the QueryHints applier makes:
            // say what is missing, at the moment it is needed.
            AppendLine($"if (_repository is not global::Pragmatic.Persistence.Repository.INavigationLinker<{entityType}> __linker)");
            Block(() =>
            {
                AppendLine("throw new global::System.InvalidOperationException(");
                IncreaseIndent();
                AppendLine(
                    $"\"{_model.TypeName} links rows by id, but the repository for {_model.EntityTypeName} \"");
                AppendLine(
                    "+ \"does not implement INavigationLinker. Linking needs the change tracker, so it is \"");
                AppendLine(
                    "+ \"implemented by Pragmatic.Persistence.EFCore; reference it, or drop [LinkIds].\");");
                DecreaseIndent();
            });

            foreach (var link in _model.Links)
            {
                AppendLine();
                AppendLine($"await __linker.LinkAsync<{link.RelatedEntityFullTypeName}>(");
                IncreaseIndent();
                AppendLine("entity,");
                AppendLine($"\"{link.Navigation}\",");
                AppendLine(
                    $"global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object>(mutation.{link.PropertyName})),");
                AppendLine($"\"{link.Key}\",");
                AppendLine($"\"{link.Strategy}\",");
                AppendLine($"static () => {link.RelatedEntityFullTypeName}.Create(),");
                AppendLine("ct).ConfigureAwait(false);");
                DecreaseIndent();
            }
        });
    }

    /// <summary>The validation of the tree, for the pipeline to run.</summary>
    /// <remarks>
    ///     ⚠️ Two reasons to emit it, not one: nested children, and values to convert. A mutation with no
    ///     children but a fallible conversion needs the override too — otherwise the method is generated
    ///     and called by nobody, and the conversion fails with a 500. The Conformance case
    ///     <c>TheConvertedScalars</c> covers it.
    /// </remarks>
    private void RenderValidateNestedTree()
    {
        if (!_model.ValidationIsAvailable)
            return;

        var hasNestedRules = _model.Children.Any(c => c.IsChildMutation);
        var hasConversionsToCheck = _model.MappedProperties.Any(p => p.CanConvertCheck is not null);

        if (!hasNestedRules && !hasConversionsToCheck)
            return;

        AppendLine();
        AppendLine("protected override global::Pragmatic.Validation.Types.ValidationError? ValidateNestedTree("
            + $"{_model.FullTypeName} mutation) => mutation.ValidateNestedTree();");
    }

    /// <summary>The nested operations, for the pipeline to check their permissions.</summary>
    /// <remarks>
    ///     Not emitted when the parent declares <c>[AbsorbsChildPermissions]</c>: the list stays empty and
    ///     the invoker asks nothing for the children. Absorbing is the absence of the question, not a
    ///     different answer — so there is no second place where the permission could be evaluated
    ///     inconsistently.
    /// </remarks>
    private void RenderNestedOperations()
    {
        if (_model.AbsorbsChildPermissions || !_model.Children.Any(c => c.IsChildMutation))
            return;

        AppendLine();
        AppendLine("protected override global::System.Collections.Generic.IReadOnlyList<global::System.Type> "
            + $"NestedOperations => {_model.FullTypeName}.NestedOperations;");
    }

    private void RenderCreateEntity()
    {
        var entityType = _model.EntityFullTypeName;

        AppendLine($"protected override {entityType} CreateEntity()");
        Block(() =>
        {
            // How the entity is built, in the order that keeps this path and Mapping's ToEntity
            // saying the same thing:
            //
            //  1. a constructor the author DECLARED, chosen by Mapping's own ConstructorAnalyzer —
            //     [MapConstructor] first, otherwise the one whose parameters match most of this
            //     mutation's properties. Observed from real symbols, so nothing is predicted and the
            //     two paths cannot drift apart.
            //  2. the generated factory, when there is no declared constructor and the factory takes
            //     no parameters. It is the only path that applies [DefaultValue], [ComputedDefault]
            //     and the audit stamps, which `new` skips and nothing downstream puts back.
            //  3. `new`, when neither is available. Unchanged behaviour, and the fallback the
            //     prediction is deliberately conservative towards.
            string build;

            if (_model.ConstructorParameters.Length > 0)
            {
                // ⚠️ Named arguments, not positional, because an optional parameter the mutation does
                // not carry must be **omitted** — so it takes its own default. A positional `default` in
                // its place is not the parameter's default: `string currency = "EUR"` would become null.
                // A required parameter no property matches is PRAG0446.
                var ctorArgs = string.Join(", ", _model.ConstructorParameters
                    .Where(p => p.MatchingPropertyName is not null)
                    .Select(p => $"{p.Name}: this.{p.MatchingPropertyName}"));

                build = $"new {entityType}({ctorArgs})";
            }
            else
            {
                build = _model.EntityHasParameterlessFactory
                    ? $"{entityType}.Create()"
                    : $"new {entityType}()";
            }

            if (_model.IsOwnedEntity)
            {
                AppendLine($"var entity = {build};");
                AppendLine("if (_currentUser?.Id is null)");
                AppendLine("    throw new global::System.InvalidOperationException(\"[HasOwner] requires an authenticated user (ICurrentUser.Id was null).\");");
                AppendLine("entity.SetOwnerId(_currentUser.Id);");
                AppendLine("return entity;");
            }
            else
            {
                AppendLine($"return {build};");
            }
        });
    }

    private void RenderGetMode()
    {
        var modeEnumType = "global::Pragmatic.Actions.Mutation.MutationMode";
        var modeValue = _model.Mode switch
        {
            MutationModeValue.Create => $"{modeEnumType}.Create",
            MutationModeValue.Update => $"{modeEnumType}.Update",
            MutationModeValue.CreateOrUpdate => $"{modeEnumType}.CreateOrUpdate",
            MutationModeValue.Delete => $"{modeEnumType}.Delete",
            MutationModeValue.Restore => $"{modeEnumType}.Restore",
            _ => $"{modeEnumType}.Create"
        };

        ExpressionMethod("GetMode", modeValue, modeEnumType,
            accessModifier: AccessModifier.Protected,
            modifiers: new MethodModifiers { IsOverride = true });
    }

    /// <summary>
    ///     The typed answer to "did the caller send an id", which decides the create fallback.
    /// </summary>
    /// <remarks>
    ///     The base class has a string-based default so a hand-written invoker keeps working, but the
    ///     default of a <c>Guid</c> is not an empty string: asking the string would read an unsent id
    ///     as sent, and the row the caller never named would be refused instead of created. The
    ///     generator holds the type, so it emits the comparison.
    /// </remarks>
    private void RenderHasEntityId()
    {
        if (_model.IdPropertyName is null)
            return;

        ExpressionMethod("HasEntityId",
            $"mutation.{_model.IdPropertyName} != default",
            "bool",
            new List<MethodParameter> { new(_model.FullTypeName, "mutation") },
            AccessModifier.Protected,
            new MethodModifiers { IsOverride = true });
    }

    private void RenderGetEntityIdString()
    {
        var mutationType = _model.FullTypeName;

        var body = _model.IdPropertyName is not null
            ? $"mutation.{_model.IdPropertyName}.ToString()"
            : "null";

        ExpressionMethod("GetEntityIdString",
            body,
            "string?",
            new List<MethodParameter> { new(mutationType, "mutation") },
            AccessModifier.Protected,
            new MethodModifiers { IsOverride = true });
    }

    private void RenderPersistNew()
    {
        var entityType = _model.EntityFullTypeName;

        if (_model.EntityIdTypeName is not null)
        {
            ExpressionMethod("PersistNew",
                "_repository.Add(entity)",
                "void",
                new List<MethodParameter> { new(entityType, "entity") },
                AccessModifier.Protected,
                new MethodModifiers { IsOverride = true });
        }
        else
        {
            AppendLine($"protected override void PersistNew({entityType} entity)");
            Block(() =>
            {
                Comment("Entity type does not implement IEntity — cannot persist via repository");
                AppendLine(
                    "throw new global::System.NotSupportedException(\"Entity must implement IEntity for persistence.\");");
            });
        }
    }

    private void RenderSaveChangesAsync()
    {
        ExpressionMethod("SaveChangesAsync",
            "_unitOfWork.SaveChangesAsync(ct)",
            "global::System.Threading.Tasks.Task",
            new List<MethodParameter>
            {
                new("global::System.Threading.CancellationToken", "ct")
            },
            AccessModifier.Protected,
            new MethodModifiers { IsOverride = true });
    }

    private void RenderFilterOverrideScopes()
    {
        foreach (var line in FilterOverrideEmitter.ScopeLines(_model.FilterOverrides!, "_filterToggle?"))
            AppendLine(line);
    }
}
