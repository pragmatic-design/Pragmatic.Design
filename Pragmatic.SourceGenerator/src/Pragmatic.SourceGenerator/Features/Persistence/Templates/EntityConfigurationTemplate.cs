using System;
using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating EF Core EntityConfiguration based on entity metadata.
/// </summary>
internal sealed class EntityConfigurationTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public EntityConfigurationTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Entity] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForEntityConfig(_model.TypeName, _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.EntityFrameworkCore.Metadata.Builders");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AddUsing(_model.Namespace);

        // EntityConfig is generated at HOST level (cross-assembly) — cannot use partial class nesting.
        // Uses entity namespace directly (not .EntityConfigurations sub-namespace).
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        Class($"{_model.TypeName}EntityConfig", RenderBody,
            interfaces: new List<string> { $"IEntityTypeConfiguration<{_model.TypeName}>" },
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        XmlSummary($"Configures the entity type <see cref=\"{_model.TypeName}\"/>.");
        XmlParam("builder", "The entity type builder.");
        Method("Configure", RenderConfigure, "void",
            new List<MethodParameter> { new($"EntityTypeBuilder<{_model.TypeName}>", "builder") });
    }

    /// <summary>
    ///     Tells EF that the key is not its to generate, when the trait already assigned it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <c>Guid</c> primary key is <c>ValueGeneratedOnAdd</c> by EF convention, and the trait
    ///         initialises <c>PersistenceId</c> at construction. Those two together are a contradiction
    ///         EF resolves against us: when it meets an untracked entity through a tracked parent's
    ///         navigation it reads the key to decide what the entity is, and a generated key that
    ///         already holds a value means "this row is in the store" — so a child that was just
    ///         constructed is tracked as <c>Modified</c> and saved as an <c>UPDATE</c> of a row that
    ///         does not exist. The write reports zero rows affected and surfaces as a
    ///         <c>DbUpdateConcurrencyException</c>, which names a conflict that never happened.
    ///     </para>
    ///     <para>
    ///         Only reachable since a mutation could write its children: every other path adds through
    ///         <c>repository.Add</c>, which states the intent instead of leaving it to be inferred.
    ///     </para>
    ///     <para>
    ///         Emitted under the same condition as the initializer itself: a key the trait does not
    ///         assign — declared by hand, or inherited from a base — is someone else's to fill, and
    ///         saying otherwise here would stop them.
    ///     </para>
    /// </remarks>
    private void RenderKeyIsNotGenerated()
    {
        if (_model.HasManualPersistenceId || _model.BaseEntityFullTypeName is not null)
        {
            return;
        }

        AppendLine("builder.Property(e => e.PersistenceId).ValueGeneratedNever();");
        AppendLine();
    }

    private void RenderConfigure()
    {
        // A derived entity shares its root's table, and the root owns everything that is about the
        // table rather than about this type: the name, the key, the query filters, the indexes. EF
        // refuses a key on a derived type — and refuses it while building the model, so a build that
        // was clean died at the first request through the boundary with "A key cannot be configured on
        // 'X' because it is a derived type". The migration schema counted the derived type as a table of
        // its own on top of merging its columns into the root's, so TPH came out as three tables.
        // Everything below this line that is skipped is skipped for that one reason.
        var isDerived = _model.BaseEntityFullTypeName is not null;

        if (!isDerived)
        {
            // Table name
            AppendLine($"builder.ToTable(\"{StringHelper.Pluralize(_model.TypeName)}\");");
            AppendLine();

            // Primary key
            AppendLine("builder.HasKey(e => e.PersistenceId);");
            AppendLine();
        }

        RenderKeyIsNotGenerated();

        // Logic key (unique index). For soft-delete entities the index is filtered (re-insert
        // after delete) — the filter is raw provider-specific SQL, so it is applied at Host
        // level in BoundaryDbContext.OnModelCreating where EfCoreProvider is known.
        if (!isDerived && _model.LogicKeys.Length > 0)
        {
            // One part keeps the single-column form it has always emitted, so no existing index moves;
            // the anonymous-type form appears only when the domain key really spans more than one.
            AppendLine($"builder.HasIndex({_model.LogicKeySelector}).IsUnique();");
            AppendLine();
        }

        // [Unique] indexes, beyond the domain key. Same tenant treatment as the domain key: the
        // columns as written, led by TenantId unless the index asked to be global.
        if (!isDerived)
        {
            foreach (var index in _model.UniqueIndexes)
            {
                var columns = _model.UniqueIndexColumns(index);
                var selector = columns.Length == 1
                    ? $"e => e.{columns[0]}"
                    : $"e => new {{ {string.Join(", ", columns.Select(c => $"e.{c}"))} }}";

                AppendLine($"builder.HasIndex({selector}).IsUnique();");
            }

            if (_model.UniqueIndexes.Length > 0)
                AppendLine();
        }

        // Properties with private setters — and every ProtectedValue, whatever its setter.
        //
        // ⚠️ The filter is about configuration EF could do without (a field access mode, a length, a
        // precision). A ProtectedValue has no mapping at all without its converter, so leaving it to
        // the setter's accessibility would make the property storable or not depending on a detail
        // that has nothing to do with it — and unstorable is how it was, which took
        // ErasureStrategy.DestroyKey with it.
        foreach (var prop in _model.Properties.Where(p => p.HasPrivateSetter || ProtectedValueType.Matches(p.TypeName)))
            RenderPropertyConfiguration(prop);

        // Collections of primitives (List<string>, string[], …) — EF Core 8+ primitive collections
        // stored as a JSON column. Mapped explicitly so they are not skipped as navigations.
        foreach (var coll in _model.PrimitiveCollectionProperties)
        {
            AppendLine();
            Comment($"{coll.Name}: collection of primitives stored as JSON column (EF Core primitive collection)");
            AppendLine($"builder.Property(e => e.{coll.Name}).HasColumnName(\"{coll.Name}\");");
        }

        // Navigations (skip cross-boundary — those entities are Ignored in DbContext)
        var navNames = new HashSet<string>();
        foreach (var nav in _model.Navigations)
        {
            navNames.Add(nav.Name);

            // On a self-reference the inverse lives on this same entity, so it is a navigation of ours
            // too. Without this it fell through to the ignore loop below and the configuration
            // contradicted itself: .WithMany(e => e.Children) on one line and builder.Ignore(e =>
            // e.Children) on the next, for a navigation EF had just been told to map.
            if (nav.InverseProperty is { Length: > 0 } inverse
                && nav.TargetFullTypeName is { Length: > 0 } target
                && string.Equals(target, _model.FullTypeName, StringComparison.Ordinal))
            {
                navNames.Add(inverse);
            }

            // The target's table belongs to another boundary: nothing to configure here, readable or
            // not. A [ReadAccess<T>] crossing has a member, and EF maps it by convention over the
            // generated {Nav}Id — the DbSet the attribute adds to this context is what makes the join.
            if (nav.IsCrossBoundary(_model.BoundaryTypeFullName))
                continue;
            RenderNavigationConfiguration(nav);
        }

        // Ignore infrastructure properties that EF Core cannot map
        // (e.g., DomainEvents, ModifiedProperties from SG-generated partial class)
        foreach (var ignoredName in _model.IgnoredPropertyNames)
        {
            // Navigation properties are configured above, not ignored
            if (navNames.Contains(ignoredName))
                continue;
            AppendLine();
            AppendLine($"builder.Ignore(e => e.{ignoredName});");
        }

        // Concurrency token — configured at Host level in BoundaryDbContext.OnModelCreating
        // where the EF Core provider (PostgreSQL, SQL Server, SQLite) is known.
        // See: BoundaryDbContextTemplate for provider-specific concurrency configuration.

        // Global query filter for soft-delete (applied automatically by EF Core).
        // NAMED filter ("SoftDelete"): EF Core 10 AND-combines multiple named query filters, so this
        // coexists with the "Tenant" named filter emitted by BoundaryDbContext.OnModelCreating for
        // tenant entities. An unnamed filter would REPLACE any other — hence the explicit name.
        if (!isDerived && _model.IsSoftDelete)
        {
            AppendLine();
            AppendLine("builder.HasQueryFilter(\"SoftDelete\", e => !e.IsDeleted);");
        }

        if (!isDerived)
            RenderVisibilityRules();

        // Index on SoftDelete for performance (most queries filter !IsDeleted)
        if (!isDerived && _model.IsSoftDelete)
        {
            AppendLine("builder.HasIndex(e => e.IsDeleted);");
        }

        // Ownership index for efficient owner-based queries
        if (!isDerived && _model.IsOwnedEntity)
        {
            AppendLine();
            AppendLine("builder.HasIndex(e => e.OwnerId);");
        }

        // Tenant index — tenant entities are filtered by TenantId on every query (the named "Tenant"
        // global filter + the runtime TenantFilter), so an index on it is on every read path.
        if (!isDerived && _model.IsTenantEntity)
        {
            AppendLine();
            AppendLine("builder.HasIndex(e => e.TenantId);");
        }

        // Scoped entity — AccessScopes is a List<string> stored as JSON (EF Core 8+ primitive collections)
        if (_model.IsScopedEntity)
        {
            AppendLine();
            Comment("AccessScopes: List<string> stored as JSON column (EF Core primitive collection)");
            AppendLine("builder.Property(e => e.AccessScopes).HasColumnName(\"AccessScopes\");");
        }

        // Tenant filtering: the runtime IQueryFilter<T> pipeline (TenantFilter) covers the Pragmatic query
        // path. The defence-in-depth EF Core named "Tenant" global filter (covering raw Set<T>() queries) is
        // emitted in BoundaryDbContext.OnModelCreating — the per-entity config has no DI access to the scoped
        // ITenantContext, but the DbContext does.
    }

    private const string MoneyTypeName = "Pragmatic.Internationalization.Types.Money";

    private void RenderPropertyConfiguration(PropertyMetadataModel prop)
    {
        // Money → EF Core complex type (Amount decimal + Currency string). Keeps both columns queryable in SQL
        // (computed [Projectable] over Money.Amount translates) instead of the opaque ValueConverter→string.
        if (prop.TypeName.TrimEnd('?') == MoneyTypeName)
        {
            RenderMoneyComplexProperty(prop);
            return;
        }

        // [ValueObject] → EF Core complex type. EF maps each scalar sub-property to a {Name}_{Sub} column
        // by convention; SchemaMetadataTransform emits the same columns so migrations match the EF model.
        if (prop.IsValueObject)
        {
            AppendLine($"builder.ComplexProperty(e => e.{prop.Name});");
            return;
        }

        var config = new List<string>
        {
            $"builder.Property(e => e.{prop.Name})"
        };

        // Always UsePropertyAccessMode for private setters
        config.Add(".UsePropertyAccessMode(PropertyAccessMode.Field)");

        // LocalizedString → JSON string value converter (works with both InMemory and relational providers)
        if (LocalizedStringType.Matches(prop.TypeName))
        {
            config.Add(
                ".HasConversion(" +
                "ls => global::System.Text.Json.JsonSerializer.Serialize(new global::System.Collections.Generic.Dictionary<string, string>(ls), global::Pragmatic.Serialization.PragmaticCommonJsonContext.Default.DictionaryStringString), " +
                "json => string.IsNullOrEmpty(json) ? new global::Pragmatic.Internationalization.Types.LocalizedString() : global::Pragmatic.Internationalization.Types.LocalizedString.From(global::System.Text.Json.JsonSerializer.Deserialize(json, global::Pragmatic.Serialization.PragmaticCommonJsonContext.Default.DictionaryStringString)!)" +
                ")");
        }

        // ProtectedValue → the bytes a column holds. The converter moves bytes and nothing else:
        // decryption stays explicit on ISubjectDataProtector, because reading protected data has three
        // outcomes and one of them is "this subject was erased" — which a value converter, being an
        // expression that can return or throw, cannot say.
        if (ProtectedValueType.Matches(prop.TypeName))
            config.Add($".HasConversion<global::{ProtectedValueType.ConverterFullName}>()");

        // MaxLength for strings
        if (prop.MaxLength.HasValue)
            config.Add($".HasMaxLength({prop.MaxLength.Value})");

        // Precision/Scale for decimals
        if (prop.Precision.HasValue)
        {
            var scale = prop.Scale ?? 2;
            config.Add($".HasPrecision({prop.Precision.Value}, {scale})");
        }

        // IsRequired
        if (!prop.IsNullable)
            config.Add(".IsRequired()");

        AppendLine(string.Join("", config) + ";");
    }

    /// <summary>
    ///     Configures a <c>Money</c> property as an EF Core complex type: <c>Amount</c> (decimal, native — so it
    ///     translates in SQL projections/aggregates) and <c>Currency</c> stored as its 3-letter code via a
    ///     value converter. Replaces the opaque whole-struct ValueConverter→string that blocked SQL projection.
    /// </summary>
    private void RenderMoneyComplexProperty(PropertyMetadataModel prop)
    {
        var precision = prop.Precision ?? 18;
        var scale = prop.Scale ?? 2;

        AppendLine($"builder.ComplexProperty(e => e.{prop.Name}, b =>");
        AppendLine("{");
        IncreaseIndent();
        if (!prop.IsNullable)
            AppendLine("b.IsRequired();");
        AppendLine($"b.Property(m => m.Amount).HasColumnName(\"{prop.Name}_Amount\").HasPrecision({precision}, {scale});");
        AppendLine($"b.Property(m => m.Currency).HasColumnName(\"{prop.Name}_Currency\").HasMaxLength(3).HasConversion(");
        AppendLine("    c => c.Code,");
        AppendLine("    s => global::Pragmatic.Internationalization.Types.CurrencyCode.FromCode(s));");
        DecreaseIndent();
        AppendLine("});");
    }

    private void RenderNavigationConfiguration(NavigationMetadataModel nav)
    {
        AppendLine();

        switch (nav.NavigationType)
        {
            case "OneToMany":
                RenderOneToManyConfiguration(nav);
                break;
            case "ManyToOne":
                RenderManyToOneConfiguration(nav);
                break;
            case "OneToOne":
                RenderOneToOneConfiguration(nav);
                break;
            case "ManyToMany":
                RenderManyToManyConfiguration(nav);
                break;
            case "Owned":
                RenderOwnedConfiguration(nav);
                break;
        }
    }

    private void RenderOwnedConfiguration(NavigationMetadataModel nav)
    {
        // Owned entity — flat in the parent table via OwnsOne
        // Column prefix defaults to "{NavigationName}_" (EF Core convention)
        AppendLine($"builder.OwnsOne(e => e.{nav.Name});");
    }

    private void RenderOneToManyConfiguration(NavigationMetadataModel nav)
    {
        AppendLine($"builder.HasMany(e => e.{nav.Name})");
        IncreaseIndent();

        if (!string.IsNullOrEmpty(nav.InverseProperty))
            AppendLine($".WithOne(e => e.{nav.InverseProperty})");
        else
            AppendLine(".WithOne()");

        AppendLine($".OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.{nav.OnDelete});");
        DecreaseIndent();
    }

    private void RenderManyToOneConfiguration(NavigationMetadataModel nav)
    {
        // A [Lookup] target has no EF navigation: the member of that name is the lookup feature's,
        // [NotMapped] and resolved from its cache. The key and its constraint are still configured,
        // bound by type instead of by navigation.
        if (nav.IsLookupReference)
        {
            var target = nav.TargetFullTypeName ?? nav.TargetTypeName;
            AppendLine($"builder.HasOne<global::{target.Replace("global::", "")}>()");
        }
        else
        {
            AppendLine($"builder.HasOne(e => e.{nav.Name})");
        }
        IncreaseIndent();

        if (!nav.IsLookupReference && !string.IsNullOrEmpty(nav.InverseProperty))
            AppendLine($".WithMany(e => e.{nav.InverseProperty})");
        else
            AppendLine(".WithMany()");

        if (!string.IsNullOrEmpty(nav.ForeignKeyProperty))
            AppendLine($".HasForeignKey(e => e.{nav.ForeignKeyProperty})");

        if (nav.IsRequired)
            AppendLine(".IsRequired()");

        AppendLine($".OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.{nav.OnDelete});");
        DecreaseIndent();
    }

    private void RenderOneToOneConfiguration(NavigationMetadataModel nav)
    {
        // Principal side should NOT generate FK configuration — only the dependent side holds the FK
        if (nav.IsPrincipal)
            return;

        AppendLine($"builder.HasOne(e => e.{nav.Name})");
        IncreaseIndent();

        if (!string.IsNullOrEmpty(nav.InverseProperty))
            AppendLine($".WithOne(e => e.{nav.InverseProperty})");
        else
            AppendLine(".WithOne()");

        if (!string.IsNullOrEmpty(nav.ForeignKeyProperty))
            AppendLine($".HasForeignKey<{_model.TypeName}>(e => e.{nav.ForeignKeyProperty})");

        AppendLine($".OnDelete(global::Microsoft.EntityFrameworkCore.DeleteBehavior.{nav.OnDelete});");
        DecreaseIndent();
    }

    /// <summary>The last segment of a possibly-qualified type name.</summary>
    private static string SimpleName(string typeName)
    {
        var trimmed = typeName.Replace("global::", "");
        var dot = trimmed.LastIndexOf('.');

        return dot >= 0 ? trimmed.Substring(dot + 1) : trimmed;
    }

    private void RenderManyToManyConfiguration(NavigationMetadataModel nav)
    {
        AppendLine($"builder.HasMany(e => e.{nav.Name})");
        IncreaseIndent();

        if (!string.IsNullOrEmpty(nav.InverseProperty))
            AppendLine($".WithMany(e => e.{nav.InverseProperty})");
        else
            AppendLine(".WithMany()");

        if (!string.IsNullOrEmpty(nav.JoinEntityTypeName))
        {
            // With the keys named, EF binds the properties the join entity declares. Without them it
            // invents shadow foreign keys named after the navigations — RelatedToPersistenceId — which
            // the migration never creates, so the table is written with your columns and read with
            // columns that do not exist, and the first write dies on 42703.
            if (!string.IsNullOrEmpty(nav.JoinLeftKey) && !string.IsNullOrEmpty(nav.JoinRightKey))
            {
                AppendLine($".UsingEntity<{nav.JoinEntityTypeName}>(");
                IncreaseIndent();
                AppendLine($"j => j.HasOne<global::{nav.TargetFullTypeName ?? nav.TargetTypeName}>()");
                AppendLine($"    .WithMany().HasForeignKey(\"{StringHelper.CSharpLiteral(nav.JoinRightKey!)}\"),");
                AppendLine($"j => j.HasOne<global::{_model.FullTypeName}>()");
                AppendLine($"    .WithMany().HasForeignKey(\"{StringHelper.CSharpLiteral(nav.JoinLeftKey!)}\"));");
                DecreaseIndent();
            }
            else
            {
                AppendLine($".UsingEntity<{nav.JoinEntityTypeName}>();");
            }
        }
        else
        {
            // The columns are named here rather than left to EF Core's convention, which derives them
            // from the navigations (TagsId/TermsId) while the migration derives them from the entities
            // (TagId/TermId). Left to the convention, the table would be created with one pair and read
            // with the other, and nothing would fail until the first write through the navigation.
            var target = SimpleName(nav.TargetTypeName);
            var (left, right) = JoinColumnNaming.For(
                _model.TypeName, target, nav.Name, nav.InverseProperty);

            // Named here even when the author did not name it. Leaving this to EF Core's convention
            // would be the other half of a disagreement: the schema builder skips a table with no name,
            // so EF would query one no migration created.
            var joinTable = JoinTableNaming.For(_model.TypeName, target, nav.JoinTable);

            AppendLine($".UsingEntity<global::System.Collections.Generic.Dictionary<string, object>>(");
            IncreaseIndent();
            AppendLine($"\"{StringHelper.CSharpLiteral(joinTable)}\",");
            AppendLine($"j => j.HasOne<global::{nav.TargetFullTypeName ?? nav.TargetTypeName}>()");
            AppendLine($"    .WithMany().HasForeignKey(\"{StringHelper.CSharpLiteral(right)}\"),");
            AppendLine($"j => j.HasOne<global::{_model.FullTypeName}>()");
            AppendLine($"    .WithMany().HasForeignKey(\"{StringHelper.CSharpLiteral(left)}\"));");
            DecreaseIndent();
        }

        DecreaseIndent();
    }

    /// <summary>
    ///     Installs each <c>[VisibleWhen&lt;TRule&gt;]</c> rule as an EF Core named global query filter.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why the rule is enforced here and not only in the Pragmatic provider.</b> A filter in
    ///         the provider is applied where the provider is called — the root of a query — and a
    ///         projected collection never passes through it: the executor filters the entity queryable
    ///         and adds the projection afterwards, so the navigation visitor is handed an expression
    ///         with no navigation left in it. EF applies a model-level filter to every query touching
    ///         the entity instead, an <c>Include</c> and a projected subquery included, which is what
    ///         a rule saying "these rows are not visible" has to mean. Soft-delete and tenant were
    ///         already installed this way; a declared rule is the same kind of statement.
    ///     </para>
    ///     <para>
    ///         The predicate is asked of the rule itself, once, while the model is built. That is why
    ///         a rule must be constructible without arguments (PRAG0718): the model is cached per
    ///         context type, so anything scoped captured here would be baked in and served to every
    ///         later request. A rule that genuinely depends on the caller is a different family — an
    ///         ownership or scope filter, which composes additively and stays on the Pragmatic side.
    ///     </para>
    ///     <para>
    ///         Named, like the other two: EF Core AND-combines named filters, while an unnamed one
    ///         replaces whatever else is there. The name also carries the opt-out — a handler with
    ///         <c>[WithoutFilter&lt;TRule&gt;]</c> lifts exactly this filter through
    ///         <c>IgnoreQueryFilters</c>, and nothing else.
    ///     </para>
    /// </remarks>
    private void RenderVisibilityRules()
    {
        if (_model.VisibilityRules.Length == 0)
            return;

        AppendLine();
        Comment("Declared visibility rules — [VisibleWhen<TRule>] on the entity");
        foreach (var rule in _model.VisibilityRules.AsImmutableArray())
        {
            var filterName = VisibilityFilterNaming.ForRule(rule);
            AppendLine($"builder.HasQueryFilter(\"{filterName}\", new {rule}().ToExpression());");
        }
    }
}
