using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     Maps a stable string discriminator to a concrete <see cref="IError"/> CLR type so that
///     <see cref="ResultJsonConverter{TValue,TError}"/> and <see cref="VoidResultJsonConverter{TError}"/>
///     can DESERIALIZE an error even when the declared error type is the <see cref="IError"/> interface
///     or the abstract <see cref="Error"/> base — cases <see cref="System.Text.Json"/> cannot
///     instantiate on its own.
/// </summary>
/// <remarks>
///     <para>
///         <b>Discriminator choice — CLR type name.</b> The discriminator is the concrete error type's
///         <see cref="Type.FullName"/> (e.g. <c>Pragmatic.Result.Http.NotFoundError</c>). This was chosen
///         over the error's <see cref="IError.Code"/> because <c>Code</c> is NOT guaranteed unique across
///         error types: a domain error and a framework error can legitimately share the same code (for
///         example a user-defined <c>NOT_FOUND</c> error alongside <see cref="Http.NotFoundError"/>).
///         <c>Code</c> is also an instance property, so it cannot be read without first constructing the
///         very instance we are trying to resolve. The CLR type name is unique, stable per type, and
///         resolvable without an instance — the robust key for a discriminator.
///     </para>
///     <para>
///         <b>Future SG wiring.</b> The source generator is the intended owner of registration: it can
///         emit a <c>[ModuleInitializer]</c> that calls <see cref="Register{TError}()"/> for every error
///         type in the compilation, exactly as it already populates <see cref="ErrorSchemaRegistry"/>.
///         Until that wiring exists, the built-in framework errors are registered eagerly via
///         <see cref="RegisterDefaults"/> (invoked from the converters' static constructors), and
///         application code may register its own error types explicitly.
///     </para>
///     <para>
///         <b>Graceful fallback.</b> When a discriminator is missing or unregistered on READ, the
///         converters fall back to <see cref="SerializedError"/> — a concrete carrier that preserves the
///         code, status, title, and any extension properties — rather than throwing. Unregistered error
///         types therefore round-trip to <see cref="SerializedError"/>, not to their original CLR type.
///     </para>
/// </remarks>
public static class ErrorTypeRegistry
{
    /// <summary>
    ///     The JSON property name carrying the type discriminator on a serialized error.
    /// </summary>
    public const string DiscriminatorProperty = "$errorType";

    private static readonly ConcurrentDictionary<string, Type> Registry = new(StringComparer.Ordinal);

    /// <summary>
    ///     Registers a concrete error type so it can be resolved during deserialization.
    /// </summary>
    /// <typeparam name="TError">The concrete error type. Must be instantiable by <see cref="System.Text.Json"/>.</typeparam>
    public static void Register<TError>() where TError : IError
        => Registry[GetDiscriminator(typeof(TError))] = typeof(TError);

    /// <summary>
    ///     Registers a concrete error type under an explicit discriminator. Use when you need a stable
    ///     discriminator that survives type renames or relocation across namespaces/assemblies.
    /// </summary>
    /// <typeparam name="TError">The concrete error type.</typeparam>
    /// <param name="discriminator">The stable discriminator value to persist in JSON.</param>
    public static void Register<TError>(string discriminator) where TError : IError
    {
        ArgumentException.ThrowIfNullOrEmpty(discriminator);
        Registry[discriminator] = typeof(TError);
    }

    /// <summary>
    ///     Returns the discriminator value for an error type — its <see cref="Type.FullName"/>, or
    ///     the simple type name as a fallback for types without a full name.
    /// </summary>
    /// <param name="errorType">The error type.</param>
    /// <returns>The discriminator string written to JSON for instances of this type.</returns>
    public static string GetDiscriminator(Type errorType)
    {
        ArgumentNullException.ThrowIfNull(errorType);
        return errorType.FullName ?? errorType.Name;
    }

    /// <summary>
    ///     Tries to resolve a registered concrete error type from a discriminator.
    /// </summary>
    /// <param name="discriminator">The discriminator read from JSON.</param>
    /// <param name="errorType">The resolved concrete type, if registered.</param>
    /// <returns><see langword="true"/> if a type was found; otherwise <see langword="false"/>.</returns>
    public static bool TryResolve(string? discriminator, [NotNullWhen(true)] out Type? errorType)
    {
        if (!string.IsNullOrEmpty(discriminator))
            return Registry.TryGetValue(discriminator!, out errorType);

        errorType = null;
        return false;
    }

    /// <summary>
    ///     Registers the built-in framework error types defined in Pragmatic.Result so they round-trip
    ///     out of the box. Idempotent; safe to call multiple times.
    /// </summary>
    /// <remarks>
    ///     Invoked from the converters' static constructors. This is a stop-gap until the source
    ///     generator emits a module initializer that registers every error type in the compilation.
    /// </remarks>
    public static void RegisterDefaults()
    {
        Register<AggregateError>();
        Register<Http.BadRequestError>();
        Register<Http.BusinessRuleError>();
        Register<Http.ConflictError>();
        Register<Http.DependencyError>();
        Register<Http.ForbiddenError>();
        Register<Http.InternalServerError>();
        Register<Http.NotFoundError>();
        Register<Http.UnauthorizedError>();
        Register<SerializedError>();
    }

    /// <summary>
    ///     Clears all registered error type mappings.
    /// </summary>
    /// <remarks>
    ///     <b>Test-only.</b> Calling this from production code wipes every registration the application
    ///     made at startup, breaking polymorphic error deserialization. Hidden from IntelliSense via
    ///     <see cref="EditorBrowsableAttribute"/> to discourage accidental use outside tests.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Clear() => Registry.Clear();
}
