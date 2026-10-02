using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Result.Models;
using Pragmatic.SourceGenerator.Features.Result.Templates;
using Pragmatic.SourceGenerator.Features.Result.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Result;

internal static class ResultFeature
{
    private const string IErrorFullName = "Pragmatic.Result.IError";
    private const string ErrorBaseFullName = "Pragmatic.Result.Error";

    /// <summary>
    ///     Base property names to skip when extracting custom properties for WriteExtensions.
    ///     These are defined on IError and Error base record.
    /// </summary>
    private static readonly HashSet<string> BasePropertyNames = new(StringComparer.Ordinal)
    {
        "Code", "StatusCode", "Title", "Description", "MessageKey", "Parameters",
        "IsTransient", "RetryAfter", "TitleKey", "DescriptionKey", "EqualityContract"
    };

    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // Find all partial class/record declarations that implement IError.
        // Collect and deduplicate by FQN to handle partial types split across multiple files.
        var errorModels = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsErrorCandidate(node),
                static (ctx, _) => TransformToModel(ctx))
            .Where(static model => model is not null)
            .Collect()
            .SelectMany(static (models, _) =>
                models.Distinct(ErrorModelComparer.Instance).ToImmutableArray());

        // Generate WriteExtensions overrides for errors with custom properties
        context.RegisterSourceOutputSafe(
            errorModels.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasResult)
                    return;
                var model = pair.Left;
                // WriteExtensions is emitted INTO the user's type, so this output still needs partial.
                if (model is null || !model.HasCustomProperties || !model.IsPartial)
                    return;

                var template = new ErrorExtensionsTemplate(model);
                var artifact = template.RenderOutput();
                ctx.AddSource(artifact);
            });

        RegisterConverters(context, features);

        // One module initializer per concrete error type, filling the two registries that the runtime
        // documents the generator as filling and that nothing actually filled:
        //
        //   ErrorTypeRegistry  — the discriminator↔type map. Only the framework's own errors were in it
        //                        (from the converters' static constructors), so a user error came back
        //                        as SerializedError and a multi-error result rejected it.
        //   ErrorSchemaRegistry — OpenAPI schema metadata. ErrorSchemaEnricher preferred it over
        //                        reflection, and the reflective branch was the only one that ever ran.
        context.RegisterSourceOutputSafe(
            errorModels.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasResult)
                    return;
                var model = pair.Left;
                if (model is null || !model.NeedsRegistration)
                    return;

                var template = new ErrorRegistrationTemplate(model);
                var artifact = template.RenderOutput();
                ctx.AddSource(artifact);
            });
    }

    /// <summary>
    ///     Any type declaration, partial or not.
    /// </summary>
    /// <remarks>
    ///     It does not require <c>partial</c>, which is a requirement of WriteExtensions only — that
    ///     output writes a member into the user's type. The schema registration writes a separate static
    ///     class, so a sealed non-partial error is registerable, and excluding it here would silently
    ///     drop its OpenAPI enrichment, with no reflective fallback to catch it.
    ///     <c>TypeDeclarationSyntax</c> rather than <c>ClassDeclarationSyntax</c>: errors are records.
    /// </remarks>
    /// <summary>
    ///     Typed converters for the result types the assembly declares with
    ///     <c>[assembly: JsonResultContract&lt;…&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     A declaration is needed because nothing in the framework puts a result on the wire AS a
    ///     result — endpoints unwrap it, and the remote invoker sends its own envelope — so there is no
    ///     closure for the generator to enumerate. Only the application knows which results it
    ///     serializes, and this is where it says so.
    /// </remarks>
    private static void RegisterConverters(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // Backtick arity: the metadata name of a generic attribute. Without it the provider never fires
        // and every declaration is ignored in silence.
        const string AttributeName = "Pragmatic.Result.Serialization.JsonResultContractAttribute`1";

        var generatedNamespace = context.CompilationProvider
            .Select(static (compilation, _) => $"{compilation.AssemblyName ?? "Global"}.Generated");

        var contracts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                // Assembly-level attributes hang off the compilation unit.
                static (node, _) => node is CompilationUnitSyntax,
                static (ctx, _) => ReadContracts(ctx))
            .SelectMany(static (models, _) => models)
            .Collect()
            .Select(static (models, _) =>
                new EquatableArray<ResultContractModel>(models.Distinct().ToImmutableArray()));

        context.RegisterSourceOutputSafe(
            contracts.Combine(generatedNamespace).Combine(features),
            static (ctx, pair) =>
            {
                var ((models, generatedNs), detected) = pair;
                if (!detected.HasResult || models.Length == 0)
                    return;

                foreach (var model in models)
                {
                    if (!model.IsGenerated)
                        continue;

                    var converter = new MultiErrorResultConverterTemplate(model, generatedNs);
                    ctx.AddSource(converter.RenderOutput());
                }

                var registration = new ResultConvertersRegistrationTemplate(models, generatedNs);
                ctx.AddSource(registration.RenderOutput());
            });
    }

    private static ImmutableArray<ResultContractModel> ReadContracts(GeneratorAttributeSyntaxContext context)
    {
        var builder = ImmutableArray.CreateBuilder<ResultContractModel>();

        foreach (var attribute in context.Attributes)
        {
            if (attribute.AttributeClass is not { TypeArguments.Length: 1 } attributeClass)
                continue;

            var model = ResultContractTransform.From(attributeClass.TypeArguments[0] as INamedTypeSymbol);
            if (model is not null)
                builder.Add(model);
        }

        return builder.ToImmutable();
    }

    private static bool IsErrorCandidate(SyntaxNode node) => node is TypeDeclarationSyntax;

    private static ErrorModel? TransformToModel(GeneratorSyntaxContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var symbol = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, typeDecl) as INamedTypeSymbol;

        if (symbol is null)
            return null;
        if (!ImplementsIError(symbol))
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        var accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant();
        var isStruct = symbol.TypeKind == TypeKind.Struct;
        var isRecord = symbol.IsRecord;
        var isPartial = typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        var customProperties = ExtractCustomProperties(symbol);
        return new ErrorModel(ns, symbol.Name, accessibility, isStruct, isRecord,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            isPartial, symbol.IsAbstract, IsNameableFromNamespaceScope(symbol),
            HasParameterlessConstructor(symbol),
            ConstantOf(symbol, "Code", context) as string,
            ConstantOf(symbol, "StatusCode", context) as int?,
            ConstantOf(symbol, "Title", context) as string,
            customProperties);
    }

    /// <summary>
    ///     Extracts public instance properties declared on this error type (not inherited from Error base).
    ///     Skips base property names and indexers.
    /// </summary>
    private static ImmutableArray<ErrorPropertyModel> ExtractCustomProperties(INamedTypeSymbol symbol)
    {
        // Only generate WriteExtensions for types that extend Error base record
        if (!ExtendsErrorBase(symbol))
            return ImmutableArray<ErrorPropertyModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<ErrorPropertyModel>();

        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.IsStatic || prop.IsIndexer || prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (prop.GetMethod is null)
                continue;
            if (BasePropertyNames.Contains(prop.Name))
                continue;
            // Skip properties declared in base types — only emit for properties declared on this type
            if (!SymbolEqualityComparer.Default.Equals(prop.ContainingType, symbol))
                continue;
            // Opt-out: [JsonIgnore] properties are kept off the wire — excluded from WriteExtensions
            // (and, symmetrically, from the OpenAPI schema in ErrorSchemaEnricher).
            if (HasJsonIgnore(prop))
                continue;

            var typeName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                             || prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var isValueType = prop.Type.IsValueType;
            var camelCase = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);

            builder.Add(new ErrorPropertyModel(prop.Name, typeName, camelCase, isNullable, isValueType,
                MapJsonSchemaType(prop.Type), EnumValuesOf(prop.Type)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Mirrors ErrorSchemaEnricher.MapClrTypeToJsonSchemaType, at compile time.
    /// </summary>
    /// <remarks>
    ///     The two must agree: whichever produces the descriptor, the schema must come out the same.
    ///     Anything unrecognised maps to "string", as the runtime version does.
    /// </remarks>
    private static string MapJsonSchemaType(ITypeSymbol type)
    {
        var unwrapped = type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                        && type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
            ? nullable.TypeArguments[0]
            : type;

        if (unwrapped.TypeKind == TypeKind.Enum)
            return "string";

        switch (unwrapped.SpecialType)
        {
            case SpecialType.System_Boolean:
                return "boolean";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return "integer";
            case SpecialType.System_Decimal:
            case SpecialType.System_Double:
            case SpecialType.System_Single:
                return "number";
        }

        return "string";
    }

    /// <summary>The enum's member names, or empty for a non-enum.</summary>
    private static ImmutableArray<string> EnumValuesOf(ITypeSymbol type)
    {
        var unwrapped = type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                        && type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
            ? nullable.TypeArguments[0]
            : type;

        if (unwrapped.TypeKind != TypeKind.Enum)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var member in unwrapped.GetMembers())
            if (member is IFieldSymbol { IsStatic: true, HasConstantValue: true } field)
                builder.Add(field.Name);

        return builder.ToImmutable();
    }

    /// <summary>
    ///     True when the type can be constructed as <c>new TheError()</c> from generated code.
    /// </summary>
    private static bool HasParameterlessConstructor(INamedTypeSymbol symbol)
    {
        if (symbol.IsAbstract || symbol.IsStatic)
            return false;

        // `new TheError()` does not compile when a required member is unset (CS9035). No probe is
        // emitted for those; their Code/StatusCode/Title come from the constant bodies, or from the
        // same fallback the reflective enricher used when it could not construct one either.
        if (HasRequiredMember(symbol))
            return false;

        foreach (var ctor in symbol.InstanceConstructors)
        {
            if (ctor.Parameters.Length != 0)
                continue;
            // The generated registration lives in the error's own namespace but a different type, so
            // private/protected is out of reach; internal is fine, same assembly.
            if (ctor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The compile-time value of an overridden property, when its body is a constant expression.
    /// </summary>
    /// <remarks>
    ///     Errors overwhelmingly declare these as literals —
    ///     <c>public override string Code =&gt; "ROOM_UNAVAILABLE";</c> — which is why the schema
    ///     registration usually needs no instance at all. Walks the inheritance chain, since an error
    ///     may inherit its status code from an intermediate base.
    /// </remarks>
    private static object? ConstantOf(INamedTypeSymbol symbol, string propertyName, GeneratorSyntaxContext context)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is not IPropertySymbol property)
                    continue;

                foreach (var reference in property.DeclaringSyntaxReferences)
                {
                    // An expression body is the only shape worth reading: a get-accessor with a block
                    // could be anything, and guessing at it would put a wrong value in the document.
                    if (reference.GetSyntax() is not PropertyDeclarationSyntax { ExpressionBody.Expression: { } expression })
                        continue;

                    // A base in another project is metadata to the build, with no syntax to read. An IDE
                    // hands the generator that project as a compilation, whose symbols keep their syntax
                    // in a tree that is not this compilation's: binding it throws and discards the
                    // generator's whole output. Read only what the build can read.
                    var compilation = context.SemanticModel.Compilation;
                    if (!compilation.ContainsSyntaxTree(expression.SyntaxTree))
                        continue;

                    var model = compilation.GetSemanticModel(expression.SyntaxTree);
                    var constant = model.GetConstantValue(expression);
                    if (constant.HasValue)
                        return constant.Value;
                }

                // Declared here but not constant — stop, do not inherit a different type's value.
                return null;
            }
        }

        return null;
    }

    /// <summary>
    ///     True when a class sitting at namespace scope in the same assembly can write the type's name.
    /// </summary>
    /// <remarks>
    ///     The registration class is a sibling in a separate file, so anything the compiler will not let
    ///     it name is out: a private or protected nested type, and a `file` type. Naming one anyway is a
    ///     build error in the consuming assembly, not a warning. Both cases were found by building —
    ///     a `private sealed record` nested in a test class, then a `file sealed record`.
    /// </remarks>
    private static bool IsNameableFromNamespaceScope(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            // A `file` type is scoped to the file that declares it, and its DeclaredAccessibility reads
            // as internal — so accessibility alone says yes while the compiler says no. Test files use
            // them for throwaway error types.
            if (current.IsFileLocal)
                return false;

            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                return false;
        }

        return true;
    }

    /// <summary>True when the type or any base declares a required member.</summary>
    private static bool HasRequiredMember(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers())
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                    return true;

        return false;
    }

    private const string JsonIgnoreFullName = "System.Text.Json.Serialization.JsonIgnoreAttribute";

    /// <summary>
    ///     True when the property is annotated with <c>[JsonIgnore]</c> (System.Text.Json) — the
    ///     opt-out for keeping a custom error property off the ProblemDetails/OpenAPI wire.
    /// </summary>
    private static bool HasJsonIgnore(IPropertySymbol prop)
    {
        foreach (var attribute in prop.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == JsonIgnoreFullName)
                return true;
        return false;
    }

    private static bool ExtendsErrorBase(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == ErrorBaseFullName)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool ImplementsIError(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
            if (iface.ToDisplayString() == IErrorFullName)
                return true;
        return false;
    }

    /// <summary>
    ///     Deduplicates ErrorModel by FullTypeName to handle partial types split across multiple files.
    /// </summary>
    private sealed class ErrorModelComparer : IEqualityComparer<ErrorModel?>
    {
        public static readonly ErrorModelComparer Instance = new();

        public bool Equals(ErrorModel? x, ErrorModel? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.FullTypeName == y.FullTypeName;
        }

        public int GetHashCode(ErrorModel? obj)
        {
            return obj?.FullTypeName.GetHashCode() ?? 0;
        }
    }
}
