using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     Expression generation methods for MappingTemplate.
/// </summary>
internal sealed partial class MappingTemplate
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Unified Entry Point
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Generates a mapping expression for a property in the specified mode.
    ///     Delegates to mode-specific logic for nested DTOs, collections, and auto-defaults,
    ///     while sharing common logic for type conversions and simple mappings.
    /// </summary>
    private string GenerateExpression(PropertyMappingModel prop, GenerationMode mode)
    {
        return mode switch
        {
            GenerationMode.Runtime => GenerateMappingExpression(prop),
            GenerationMode.ExpressionTree => GenerateProjectionExpression(prop),
            _ => GenerateMappingExpression(prop)
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Mapping Expression Generation
    // ═══════════════════════════════════════════════════════════════════════════

    private string GenerateMappingExpression(PropertyMappingModel prop)
    {
        // Converter (explicit converter takes precedence) — cached static instance, no per-call alloc.
        if (prop.HasConverter)
            return $"{ConverterFieldName(prop.ConverterType!)}.Convert({prop.SourceExpression})";

        // Nested DTO
        if (prop.IsNestedDto && !string.IsNullOrEmpty(prop.NestedDtoType))
            return GenerateNestedDtoExpression(prop);

        // Collection of DTOs
        if (prop.CollectionKind != CollectionKind.None && prop.IsElementDto &&
            !string.IsNullOrEmpty(prop.ElementDtoType))
            return GenerateDtoCollectionExpression(prop);

        // Collection of simple types (copy, don't share reference)
        if (prop.CollectionKind != CollectionKind.None && prop.IsElementSimple)
            return GenerateSimpleCollectionExpression(prop);

        // Dictionary (simple copy)
        if (prop is { IsDictionary: true, IsDictionaryValueSimple: true })
            return GenerateDictionaryExpression(prop);

        // Dictionary with DTO values
        if (prop is { IsDictionary: true, IsDictionaryValueSimple: false } && !string.IsNullOrEmpty(prop.DictionaryValueDtoType))
            return GenerateDictionaryWithDtoExpression(prop);

        // Enum → different enum type: by-name switch, members validated at analysis time (PRAG0328).
        if (prop is { Conversion: ConversionKind.EnumToEnum, SourceExpression: not null }
            && prop.EnumToEnumMembers.Length > 0)
        {
            var srcType = Globalize(prop.SourcePropertyType!);
            var tgtType = Globalize(prop.PropertyType);
            var arms = string.Join(", ", prop.EnumToEnumMembers.AsImmutableArray()
                .Select(m => $"{srcType}.{m} => {tgtType}.{m}"));
            return $"{prop.SourceExpression} switch {{ {arms}, _ => throw new global::System.ArgumentOutOfRangeException(nameof(entity), $\"Unmapped enum value: {{{prop.SourceExpression}}}\") }}";
        }

        // Automatic type conversion
        if (prop is { RequiresConversion: true, SourceExpression: not null })
            return TypeConversionHelper.GenerateConversionExpression(
                prop.SourceExpression,
                // Qualified: a cast in generated code names the type the way the rest of the file
                // does, and a simple name resolves against whatever happens to be in scope.
                prop.PropertyFullTypeName ?? prop.PropertyType,
                prop.Conversion,
                prop.SourceIsNullable,
                prop.Format,
                prop.EnumOnUnknown,
                prop.EnumAliases.AsImmutableArray());

        // Format string (for non-conversion cases)
        if (!string.IsNullOrEmpty(prop.Format))
        {
            var nullCheck = prop.SourceIsNullable ? "?" : "";
            return $"{prop.SourceExpression}{nullCheck}.ToString(\"{TypeConversionHelper.EscapeLiteral(prop.Format!)}\")";
        }

        // Null substitution (explicit Default): applies to nullable sources regardless of target
        // nullability — nullable→non-nullable (required) AND nullable→nullable (fallback value).
        if (prop.SourceIsNullable && !string.IsNullOrEmpty(prop.DefaultValue))
        {
            var defaultVal = IsStringDefault(prop) ? $"\"{prop.DefaultValue}\"" : prop.DefaultValue;
            return $"{prop.SourceExpression} ?? {defaultVal}";
        }

        // Known-incompatible simple types: PRAG0304 is already reported, and the refusal comes before
        // the auto-default below. The auto-default only asks whether the source can be absent — it
        // never compares the unwrapped source with the target — so a nullable source on a narrower
        // target would take that branch and emit `(x).GetValueOrDefault()`, which is the very CS0266
        // this `default!` exists to prevent: the author would see the clean diagnostic *and* a cryptic
        // one on a line they did not write.
        if (TypeConversionHelper.IsKnownIncompatible(prop))
            return $"default! /* ⚠ INCOMPATIBLE: {prop.SourcePropertyType} → {prop.PropertyType} (PRAG0304) */";

        // Auto-default for nullable to non-nullable primitives
        if (prop is { SourceIsNullable: true, IsNullable: false })
        {
            var autoDefault = GetAutoDefault(prop.SourcePropertyType, prop.PropertyType, prop.SourceIsEnum, isProjection: false);
            if (autoDefault is not null)
                return autoDefault.Replace("{expr}", prop.SourceExpression ?? "default");
        }
        // Simple mapping
        return GuardUnloadedNavigation(prop)
               ?? prop.SourceExpression
               ?? $"default! /* ⚠ UNMAPPED: {prop.PropertyName} — no matching source property found */";
    }

    /// <summary>
    ///     A flattened path, guarded so an unloaded navigation says what to do about itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[MapProperty("Property.Name")]</c> walks <c>entity.Property.Name</c> in memory. EF
    ///         declares a reference navigation non-nullable and leaves it null until something loads it,
    ///         so the type system cannot see the hole and the resolver's <c>?.</c> — which it inserts
    ///         only for a <i>declared</i> nullable — never fires. What arrived instead was a bare
    ///         <c>NullReferenceException</c> from inside a generated file, naming nothing.
    ///     </para>
    ///     <para>
    ///         Throwing rather than defaulting is the deliberate half. Substituting the property's
    ///         default would turn a missing <c>Include</c> into an empty string on the wire, which is
    ///         the kind of wrong that never gets reported. Most callers do not reach this at all now:
    ///         a mutation that declares its response DTO gets the navigation loaded for it.
    ///     </para>
    ///     <para>
    ///         Projection is untouched — <c>GenerateProjectionExpression</c> is a different method, and
    ///         EF turns the same path into a join that yields null without complaint.
    ///     </para>
    /// </remarks>
    private string? GuardUnloadedNavigation(PropertyMappingModel prop)
    {
        if (prop.SourceExpression is not { } expression || prop.SourceIsNullable)
            return null;

        // A value that lives in the entity's own row is not a navigation: it arrives with the row, so
        // there is nothing to guard, and the message would tell the reader to Include something EF does
        // not have. On a struct — Money — `is null` does not even compile.
        if (prop.SourceIsSameRowValue)
            return null;

        // A concatenation is not a path: "entity.FirstName + \" \" + entity.LastName" has dots in it
        // and no navigation anywhere. Splitting it would produce an expression that does not parse — the
        // same exclusion ComputeRequiredNavigations makes when it works the paths out.
        if (prop.Resolution == MappingResolution.Concatenation || !IsSimplePropertyPath(expression))
            return null;

        // "entity.Property.Name" → guard "entity.Property". One dot is "entity.X", not a navigation.
        var lastDot = expression.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        var navigation = expression.Substring(0, lastDot);
        if (!navigation.Contains('.') || navigation.Contains("?."))
            return null;

        var path = navigation.StartsWith("entity.", StringComparison.Ordinal)
            ? navigation.Substring("entity.".Length)
            : navigation;

        var message =
            $"{_model.TypeName} maps {path}.{expression.Substring(lastDot + 1)}, but {path} is not "
            + $"loaded on this {_model.SourceTypeName}. Load it — [EagerLoad(\\\"{path}\\\")] on the "
            + $"mutation, or .Include on the query — or project with {_model.TypeName}.Projection, "
            + "which resolves it in the database.";

        return $"{navigation} is null ? throw new global::System.InvalidOperationException("
               + $"\"{message}\") : {expression}";
    }

    /// <summary>
    ///     Whether the expression is nothing but identifiers joined by dots — a property path and not
    ///     a composite the generator built.
    /// </summary>
    private static bool IsSimplePropertyPath(string expression)
    {
        foreach (var c in expression)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                return false;
        }

        return expression.Length > 0;
    }

    /// <summary>
    ///     Prefixes a display-string type with <c>global::</c> (ToDisplayString is namespace-qualified,
    ///     or bare for the global namespace — both are valid global:: targets).
    /// </summary>
    private static string Globalize(string type)
        => type.StartsWith("global::", StringComparison.Ordinal) ? type : $"global::{type}";

    private string GenerateNestedDtoExpression(PropertyMappingModel prop)
    {
        // Note: We don't add ? before 'is { }' - the pattern already handles null
        // entity.Contact is { } __nested means: if Contact is not null, assign to __nested
        // NestedDtoType already uses FullyQualifiedFormat (includes global::)
        var visited = _model.HasCircularReferences ? ", visited" : "";

        // A DTO over the same row maps the row.
        if (prop.IsSameRow)
            return $"{prop.NestedDtoType}.FromEntity(entity{visited})";

        // Declared non-nullable: an unloaded navigation is said, not assigned as null — the
        // same explanation a flattened path gives (GuardUnloadedNavigation).
        var otherwise = prop.IsDeclaredNonNull ? UnloadedNestedDto(prop) : "default";

        return
            $"{prop.SourceExpression} is {{ }} __nested_{prop.PropertyName} ? {prop.NestedDtoType}.FromEntity(__nested_{prop.PropertyName}{visited}) : {otherwise}";
    }

    /// <summary>The throw for a non-nullable nested DTO whose navigation was not loaded.</summary>
    private string UnloadedNestedDto(PropertyMappingModel prop)
    {
        var path = prop.SourceExpression is { } expression && expression.StartsWith("entity.", StringComparison.Ordinal)
            ? expression.Substring("entity.".Length)
            : prop.PropertyName;

        var message =
            $"{_model.TypeName}.{prop.PropertyName} is declared non-nullable, but {path} is null on this "
            + $"{_model.SourceTypeName}. If it is not loaded, load it — [EagerLoad(\\\"{path}\\\")] on the "
            + $"mutation, or .Include on the query — or project with {_model.TypeName}.Projection; if it can "
            + "be absent, declare the property nullable.";

        return $"throw new global::System.InvalidOperationException(\"{message}\")";
    }

    private string GenerateDtoCollectionExpression(PropertyMappingModel prop)
    {
        // ElementDtoType already uses FullyQualifiedFormat (includes global::)
        var selectExpr = _model.HasCircularReferences
            ? $"{prop.ElementDtoType}.FromEntity(x, visited)"
            : $"{prop.ElementDtoType}.FromEntity(x)";

        // Use LINQ Select for all collection types — safe regardless of source collection type
        // (ConvertAll is List<T>-only and we don't know the source collection type at SG time)
        var toCollection = GetToCollectionMethod(prop.CollectionKind);
        return $"{prop.SourceExpression}?.Select(x => {selectExpr}){toCollection} ?? []";
    }

    private static string GenerateSimpleCollectionExpression(PropertyMappingModel prop)
    {
        var toCollection = GetToCollectionMethod(prop.CollectionKind);
        return $"{prop.SourceExpression}?{toCollection} ?? []";
    }

    private static string GenerateDictionaryExpression(PropertyMappingModel prop)
    {
        return
            $"{prop.SourceExpression} is {{ }} __dict_{prop.PropertyName} ? new Dictionary<{prop.DictionaryKeyType}, {prop.DictionaryValueType}>(__dict_{prop.PropertyName}) : new()";
    }

    private string GenerateDictionaryWithDtoExpression(PropertyMappingModel prop)
    {
        // Dictionary with DTO values - map each value via FromEntity
        var fromEntityCall = _model.HasCircularReferences
            ? $"{prop.DictionaryValueDtoType}.FromEntity(kvp.Value, visited)"
            : $"{prop.DictionaryValueDtoType}.FromEntity(kvp.Value)";

        return
            $"{prop.SourceExpression} is {{ }} __dict_{prop.PropertyName} ? __dict_{prop.PropertyName}.ToDictionary(kvp => kvp.Key, kvp => {fromEntityCall}) : new()";
    }

    private static string GetToCollectionMethod(CollectionKind kind)
    {
        return kind switch
        {
            CollectionKind.List => ".ToList()",
            CollectionKind.Array => ".ToArray()",
            CollectionKind.HashSet => ".ToHashSet()",
            CollectionKind.ImmutableArray => ".ToImmutableArray()",
            CollectionKind.ImmutableList => ".ToImmutableList()",
            CollectionKind.IList or CollectionKind.ICollection or CollectionKind.IEnumerable
                or CollectionKind.IReadOnlyList or CollectionKind.IReadOnlyCollection => ".ToList()",
            _ => ".ToList()"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Projection Expression Generation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     The projection of <paramref name="prop" />, with a projectable member at the end of a path
    ///     through a navigation computed instead of read.
    /// </summary>
    /// <remarks>
    ///     Replaced after the fact, in the finished expression: the null checks and the default the
    ///     projection writes around the read are the template's, and rewriting the read before them would
    ///     have meant teaching each of them about bodies. Only the whole member access is replaced — a
    ///     longer name that starts the same way is left alone.
    /// </remarks>
    private string GenerateProjectionExpression(PropertyMappingModel prop)
    {
        var expression = GenerateProjectionExpressionCore(prop);
        if (prop.ProjectableRead is not { } read || prop.ProjectableReadBody is not { } body)
            return expression;

        return System.Text.RegularExpressions.Regex.Replace(
            expression,
            System.Text.RegularExpressions.Regex.Escape(read) + @"(?![\w])",
            _ => $"({body})");
    }

    private string GenerateProjectionExpressionCore(PropertyMappingModel prop)
    {
        // Convert ?. to . for Expression Trees - EF Core handles null navigation in SQL.
        // ⚠️ The projection's own form of the expression when there is one: a join containing an enum
        // cannot be the string the in-memory mapping uses, because SQL concatenates the number the
        // column holds rather than the member's name. Composed with the other in one place, so the two
        // cannot drift; null means they are the same string.
        var expr = ToExpressionTreeSafe(prop.ProjectionSourceExpression ?? prop.SourceExpression);

        // Nested DTO projection - inline the property mappings
        if (prop.IsNestedDto && !string.IsNullOrEmpty(prop.NestedDtoType))
            return GenerateInlinedNestedProjection(prop, expr);

        // Collection projection - inline element mappings
        if (prop.CollectionKind != CollectionKind.None && prop.IsElementDto)
            return GenerateInlinedCollectionProjection(prop, expr);

        // A LocalizedString read as a string. The implicit conversion, in an expression tree, is read by
        // EF Core as a cast to the column's provider type — the JSON of every translation — and never
        // runs. .Value is evaluated on the client after the converter, in the request's
        // culture: the last step of the top-level projection, where that is allowed.
        if (expr is not null && LocalizedStringType.Matches(prop.SourcePropertyType)
            && prop.PropertyType.TrimEnd('?') is "string" or "System.String")
            return prop.SourceIsNullable ? $"({expr} == null ? null : {expr}.Value)" : $"{expr}.Value";

        // A converter, computed on the client after the read (IsComputedAfterTheRead), through the static
        // method that calls the cached instance FromEntity calls — see RenderConvertersAfterTheRead.
        if (prop.HasConverter)
            return $"{ConvertAfterTheReadName(prop)}({expr})";

        // A formatted nullable value: the in-memory `?.ToString(…)` cannot be written in an expression
        // tree, so the null is tested and the value unwrapped — the same answer, null for null.
        if (prop is { RequiresConversion: true, SourceExpression: not null, SourceIsNullable: true }
            && !string.IsNullOrEmpty(prop.Format))
            return $"{expr} == null ? null : " + TypeConversionHelper.GenerateConversionExpression(
                $"{expr}.Value",
                prop.PropertyType,
                prop.Conversion,
                sourceIsNullable: false,
                prop.Format);

        // Automatic type conversion (enum to/from string, numeric widening, etc.) — SQL-translatable, or a
        // Format computed on the client after the read.
        if (prop is { RequiresConversion: true, SourceExpression: not null })
            return TypeConversionHelper.GenerateConversionExpression(
                expr!,
                prop.PropertyType,
                prop.Conversion,
                prop.SourceIsNullable,
                prop.Format);

        // Default value with null coalescing
        if (prop is { SourceIsNullable: true, IsNullable: false } && !string.IsNullOrEmpty(prop.DefaultValue))
        {
            var defaultVal = IsStringDefault(prop) ? $"\"{prop.DefaultValue}\"" : prop.DefaultValue!;
            return ProjectionDefault(prop.SourceExpression, expr, defaultVal);
        }

        // Known-incompatible simple types: refused before the auto-default, for the reason spelled out
        // on the runtime branch above.
        if (TypeConversionHelper.IsKnownIncompatible(prop))
            return $"default! /* ⚠ INCOMPATIBLE: {prop.SourcePropertyType} → {prop.PropertyType} (PRAG0304) */";

        // Auto-default for nullable to non-nullable in projection
        if (prop is { SourceIsNullable: true, IsNullable: false })
        {
            var autoDefault = GetAutoDefault(prop.SourcePropertyType, prop.PropertyType, prop.SourceIsEnum, isProjection: true);
            if (autoDefault is not null)
            {
                // autoDefault has the shape "{expr} ?? <default>"; extract the default token so it can be
                // folded into null-conditional chains rather than appended as a trailing ??.
                var token = autoDefault.Substring(autoDefault.IndexOf("?? ", StringComparison.Ordinal) + 3);
                return ProjectionDefault(prop.SourceExpression, expr, token);
            }
        }
        // Simple projection
        return expr ?? $"default! /* ⚠ UNMAPPED: {prop.PropertyName} — no matching source property found */";
    }

    /// <summary>
    ///     Builds a projection default. When the source path is a null-conditional chain (a flattened
    ///     nullable navigation), the trailing form <c>(nav == null ? null : nav.Leaf) ?? d</c> does not
    ///     type-check for a non-nullable value-type leaf (CS0173: no common type between <c>null</c> and
    ///     e.g. <c>int</c>). The default is folded into every null branch instead —
    ///     <c>nav == null ? d : nav.Leaf</c> — which EF Core still translates to SQL. Plain nullable
    ///     sources keep the simpler <c>expr ?? d</c>.
    /// </summary>
    private static string ProjectionDefault(string? rawSource, string? rewrittenExpr, string defaultToken)
        => rawSource is not null && rawSource.Contains("?.")
            ? FoldNullConditionalDefault(rawSource, defaultToken)
            : $"{rewrittenExpr} ?? {defaultToken}";

    private static string FoldNullConditionalDefault(string sourceExpr, string defaultToken)
    {
        var idx = sourceExpr.IndexOf("?.", StringComparison.Ordinal);
        if (idx < 0)
            return sourceExpr; // resolved leaf: raw member access (guaranteed non-null on this branch)

        var nav = sourceExpr.Substring(0, idx);
        var rest = sourceExpr.Substring(idx + 2);
        var inner = FoldNullConditionalDefault($"{nav}.{rest}", defaultToken);
        return $"({nav} == null ? {defaultToken} : {inner})";
    }

    /// <summary>
    ///     Converts null-conditional operators (?.) to ternary null checks for Expression Trees.
    ///     Expression Trees don't support ?. — we generate <c>(nav == null ? null : nav.Member)</c> instead.
    ///     This is safe for both EF Core SQL translation and client-side evaluation.
    /// </summary>
    private static string? ToExpressionTreeSafe(string? expression)
    {
        if (expression is null || !expression.Contains("?.")) return expression;

        // Find first ?. and split: "entity.Customer?.Name" → nav="entity.Customer", rest="Name"
        var idx = expression.IndexOf("?.", StringComparison.Ordinal);
        var nav = expression.Substring(0, idx);
        var rest = expression.Substring(idx + 2);

        // Recursively handle deeper ?. chains (entity.Customer?.Address?.City)
        var inner = ToExpressionTreeSafe($"{nav}.{rest}");

        return $"({nav} == null ? null : {inner})";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Inlined Projection Generation (Recursive for Deep Nesting)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Generates an inlined nested DTO projection expression for EF Core.
    ///     Supports recursive nesting for 3+ level deep hierarchies.
    ///     Example: entity.Address == null ? null : new AddressDto { Street = entity.Address.Street, ... }
    /// </summary>
    private static string GenerateInlinedNestedProjection(PropertyMappingModel prop, string? sourceExpr)
    {
        if (prop.NestedProjectionMappings.Length == 0)
            // Fallback if no mappings available
            return $"{sourceExpr} == null ? default : new {prop.NestedDtoType} {{ }}";

        // A DTO over the same row reads the row, which is there.
        if (prop.IsSameRow)
        {
            var rowInitializers = prop.NestedProjectionMappings
                .Select(m => GenerateNestedPropertyInitializer(m, "entity"));
            return $"new {prop.NestedDtoType} {{ {string.Join(", ", rowInitializers)} }}";
        }

        // Build property initializers with recursive support
        var initializers = prop.NestedProjectionMappings
            .Select(m => GenerateNestedPropertyInitializer(m, $"{sourceExpr}!"))
            .ToList();

        var initializerList = string.Join(", ", initializers);

        // A DTO declared non-nullable is not assigned null: the author says it is there, and for a
        // required relation it is. The null check would be CS8601 in a file nobody can edit.
        if (prop.IsDeclaredNonNull)
            return $"new {prop.NestedDtoType} {{ {initializerList} }}";

        // Generate: entity.Address == null ? null : new AddressDto { ... }
        return $"{sourceExpr} == null ? null : new {prop.NestedDtoType} {{ {initializerList} }}";
    }

    /// <summary>
    ///     Generates an inlined collection projection expression for EF Core.
    ///     Supports recursive nesting for collection elements with nested DTOs/collections.
    ///     Example: entity.Lines.Select(x => new OrderLineDto { Id = x.Id, ... }).ToList()
    /// </summary>
    private static string GenerateInlinedCollectionProjection(PropertyMappingModel prop, string? sourceExpr)
    {
        var toCollection = prop.CollectionKind switch
        {
            CollectionKind.List => ".ToList()",
            CollectionKind.Array => ".ToArray()",
            CollectionKind.HashSet => ".ToHashSet()",
            _ => ".ToList()"
        };

        if (prop.ElementProjectionMappings.Length == 0)
            // Fallback if no mappings available
            return $"{sourceExpr}.Select(x => new {prop.ElementDtoType} {{ }}){toCollection}";

        // Build property initializers using 'x' as the element variable
        var initializers = prop.ElementProjectionMappings
            .Select(m => GenerateNestedPropertyInitializer(m, "x"))
            .ToList();

        var initializerList = string.Join(", ", initializers);

        // Generate: entity.Lines.Select(x => new OrderLineDto { ... }).ToList()
        return $"{sourceExpr}.Select(x => new {prop.ElementDtoType} {{ {initializerList} }}){toCollection}";
    }

    /// <summary>
    ///     Generates a property initializer for nested projection, handling recursive nesting.
    /// </summary>
    /// <param name="mapping">The nested property mapping</param>
    /// <param name="sourcePrefix">The source expression prefix (e.g., "entity!", "x")</param>
    /// <returns>Property initializer expression (e.g., "City = entity!.City")</returns>
    private static string GenerateNestedPropertyInitializer(NestedPropertyMapping mapping, string sourcePrefix)
    {
        // Explicit paths: concatenation (multi) or flattening (dotted) — expression-tree-safe inline.
        if (mapping.SourcePaths.Length > 1)
        {
            var parts = mapping.SourcePaths.AsImmutableArray().Select(p => JoinPart(mapping, sourcePrefix, p));
            return $"{mapping.TargetPropertyName} = {string.Join($" + \"{mapping.Separator}\" + ", parts)}";
        }

        if (mapping.SourcePaths.Length == 1)
            return $"{mapping.TargetPropertyName} = {sourcePrefix}.{mapping.SourcePaths[0]}";

        var sourceExpr = $"{sourcePrefix}.{mapping.SourcePropertyName}";

        // Case 1: Nested DTO (recursive)
        if (mapping.IsNestedDto && !string.IsNullOrEmpty(mapping.NestedDtoType))
            return $"{mapping.TargetPropertyName} = {GenerateRecursiveNestedProjection(mapping, sourceExpr)}";

        // Case 2: Collection of DTOs (recursive)
        if (mapping is { IsCollection: true, ElementMappings.Length: > 0 })
            return $"{mapping.TargetPropertyName} = {GenerateRecursiveCollectionProjection(mapping, sourceExpr)}";

        // A localized name: its value, evaluated in the final projection in the request's culture — the
        // same read the top level makes.
        if (mapping.ReadsLocalizedValue)
            return mapping.SourceIsNullable
                ? $"{mapping.TargetPropertyName} = ({sourceExpr} == null ? null : {sourceExpr}.Value)"
                : $"{mapping.TargetPropertyName} = {sourceExpr}.Value";

        // Case 3: Simple property — or, for a projectable member, its body over the same source: the
        // getter is no column.
        if (mapping.ProjectableBody is { } body)
            return $"{mapping.TargetPropertyName} = ({body.Replace(Core.ProjectableBody.PortableSource, sourcePrefix)})";

        // A nullable column into a non-nullable member: the default the top level gives, rather than an
        // assignment that does not compile once inlined.
        if (mapping is { SourceIsNullable: true, IsNullable: false }
            && GetAutoDefault(mapping.SourcePropertyType, mapping.PropertyType, mapping.SourceIsEnum, isProjection: true)
                is { } autoDefault)
            return $"{mapping.TargetPropertyName} = {autoDefault.Replace("{expr}", sourceExpr)}";

        return $"{mapping.TargetPropertyName} = {sourceExpr}";
    }

    /// <summary>
    ///     Generates a recursive nested DTO projection expression.
    /// </summary>
    private static string GenerateRecursiveNestedProjection(NestedPropertyMapping mapping, string sourceExpr)
    {
        if (mapping.NestedMappings.Length == 0)
            return $"{sourceExpr} == null ? null : new {mapping.NestedDtoType} {{ }}";

        // Build property initializers recursively
        var initializers = mapping.NestedMappings
            .Select(m => GenerateNestedPropertyInitializer(m, $"{sourceExpr}!"))
            .ToList();

        var initializerList = string.Join(", ", initializers);

        // Declared non-nullable: not assigned null, as at the top level.
        return mapping.IsDeclaredNonNull
            ? $"new {mapping.NestedDtoType} {{ {initializerList} }}"
            : $"{sourceExpr} == null ? null : new {mapping.NestedDtoType} {{ {initializerList} }}";
    }

    /// <summary>
    ///     Generates a recursive collection projection expression.
    /// </summary>
    private static string GenerateRecursiveCollectionProjection(NestedPropertyMapping mapping, string sourceExpr)
    {
        var toCollection = mapping.CollectionKind switch
        {
            CollectionKind.List => ".ToList()",
            CollectionKind.Array => ".ToArray()",
            CollectionKind.HashSet => ".ToHashSet()",
            _ => ".ToList()"
        };

        if (mapping.ElementMappings.Length == 0)
            return $"{sourceExpr}.Select(__e => new {mapping.ElementDtoType} {{ }}){toCollection}";

        // Use unique variable name to avoid conflicts in nested lambdas
        var varName = "__e" + sourceExpr.Count(c => c == '.');

        // Build property initializers recursively
        var initializers = mapping.ElementMappings
            .Select(m => GenerateNestedPropertyInitializer(m, varName))
            .ToList();

        var initializerList = string.Join(", ", initializers);

        return $"{sourceExpr}.Select({varName} => new {mapping.ElementDtoType} {{ {initializerList} }}){toCollection}";
    }

    /// <summary>One member of a joined string, as the projection has to express it.</summary>
    /// <remarks>
    ///     ⚠️ An enum is not concatenated. In memory <c>string + enum</c> gives the member's name;
    ///     translated to SQL the same expression gives the number the column holds, so the one
    ///     declaration answered two ways and the projected answer was wrong rather than empty. A chain
    ///     of comparisons becomes a <c>CASE</c>, reads as the name, and does not depend on whether the
    ///     enum is stored as an integer or converted to a string.
    ///     <para>
    ///         The last branch answers for a nullable enum holding null, and for a value outside the
    ///         declared members — an empty cell rather than a number nobody can read.
    ///     </para>
    /// </remarks>
    private static string JoinPart(NestedPropertyMapping mapping, string sourcePrefix, string path)
    {
        var expression = $"{sourcePrefix}.{path}";

        var enumPart = mapping.EnumJoinParts.AsImmutableArray()
            .FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.Ordinal));

        if (enumPart is null || enumPart.Members.Length == 0)
            return expression;

        var branches = enumPart.Members.AsImmutableArray()
            .Select(m => $"{expression} == {enumPart.TypeName}.{m} ? \"{m}\" : ");

        return $"({string.Concat(branches)}\"\")";
    }
}
