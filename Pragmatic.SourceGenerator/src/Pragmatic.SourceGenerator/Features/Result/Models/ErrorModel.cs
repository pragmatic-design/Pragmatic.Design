using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Result.Models;

/// <summary>
///     Model representing an error type for WriteExtensions generation.
/// </summary>
internal sealed record ErrorModel(
    string Namespace,
    string TypeName,
    string Accessibility,
    bool IsStruct,
    bool IsRecord,
    string FullyQualifiedName,
    bool IsPartial,
    bool IsAbstract,
    bool IsNameableFromNamespaceScope,
    bool HasParameterlessConstructor,
    string? ConstantCode,
    int? ConstantStatusCode,
    string? ConstantTitle,
    EquatableArray<ErrorPropertyModel> CustomProperties)
{
    /// <summary>
    ///     Gets the full type name including namespace.
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>
    ///     Whether this error has custom properties that need a WriteExtensions override.
    /// </summary>
    public bool HasCustomProperties => CustomProperties.Length > 0;

    /// <summary>
    ///     Whether this error can have its OpenAPI schema metadata registered at module load.
    /// </summary>
    /// <remarks>
    ///     Every error with custom properties qualifies. Code, StatusCode and Title are instance
    ///     properties, but in practice they are constant expression bodies —
    ///     <c>public override string Code =&gt; "ROOM_UNAVAILABLE";</c> — so the generator reads the
    ///     literal and constructs nothing.
    ///     <para>
    ///         When one of them is computed, an instance is built in plain typed C# if the type has a
    ///         parameterless constructor. When it has neither — a record with <c>required</c> members,
    ///         or a positional one — the values fall back to the type name, 400, and the type name
    ///         spaced out.
    ///     </para>
    /// </remarks>
    public bool CanRegisterSchema => HasCustomProperties;

    /// <summary>Whether the registration has to construct an instance to read Code/StatusCode/Title.</summary>
    public bool NeedsProbe =>
        (ConstantCode is null || ConstantStatusCode is null || ConstantTitle is null)
        && HasParameterlessConstructor;

    /// <summary>
    ///     Whether this type needs a module initializer at all.
    /// </summary>
    /// <remarks>
    ///     Every concrete error is registered with <c>ErrorTypeRegistry</c>, which is what lets its
    ///     discriminator resolve back to the type on the way in. Registering only the framework's own
    ///     errors would let a user error round-trip into <c>SerializedError</c>, and a multi-error result
    ///     would then reject it as "not one of the declared error types".
    ///     <para>
    ///         A type the registration class cannot name is skipped: a <c>private</c> record nested in a
    ///         test class is an error type by the type system and unreachable from a sibling class, and
    ///         emitting a reference to it fails the consuming assembly's build.
    ///     </para>
    /// </remarks>
    public bool NeedsRegistration => !IsAbstract && IsNameableFromNamespaceScope;

    /// <summary>
    ///     The type's name including any outer types, joined with '_'.
    /// </summary>
    /// <remarks>
    ///     Used for the registration class and its hint name. The simple name is not enough: a public
    ///     error nested in two different outer types would produce the same hint twice, and Roslyn
    ///     answers a duplicate hint by discarding the ENTIRE generator's output with a CS8785 that is
    ///     only a warning — a build that passes with all the generated code missing.
    /// </remarks>
    public string UniqueName
    {
        get
        {
            var withoutGlobal = FullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
                ? FullyQualifiedName.Substring("global::".Length)
                : FullyQualifiedName;

            var withoutNamespace = string.IsNullOrEmpty(Namespace)
                ? withoutGlobal
                : withoutGlobal.Substring(Namespace.Length + 1);

            return withoutNamespace.Replace(".", "_");
        }
    }
}

/// <summary>
///     Represents a custom property on an error type for WriteExtensions generation.
/// </summary>
internal sealed record ErrorPropertyModel(
    string Name,
    string TypeName,
    string CamelCaseName,
    bool IsNullable,
    bool IsValueType,
    string JsonSchemaType,
    EquatableArray<string> EnumValues);
