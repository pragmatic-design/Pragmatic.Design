namespace Pragmatic;

/// <summary>
///     Marks a property as containing sensitive data that should not appear in logs,
///     telemetry, audit trails, or diagnostic output.
/// </summary>
/// <remarks>
///     <para>
///         <b>It is enforced, and not optionally.</b> The source generator emits an
///         <c>IRedactionMap</c> per assembly listing the marked members; at the logging boundary
///         <c>PragmaticLoggerProviderBase</c> applies it to the entry before any provider formats it,
///         so the JSON writers, the console writer and the debug writer all see a value already
///         masked. It is deliberately outside <c>Privacy.EnableRedaction</c>: that flag governs the
///         pattern heuristic, which is worth silencing in development, while a member you marked
///         yourself is a contract with no environment qualifier.
///     </para>
///     <para>
///         <b>What it covers.</b> Any type with a marked property — the generator is driven by the
///         attribute, not by the kind of type carrying it, so a mutation input, a query, a message
///         and an entity are all covered without anyone enumerating them. A positional record works
///         too: <c>[property: NotLogged]</c> on a primary-constructor parameter reaches the property
///         it generates.
///     </para>
///     <para>
///         <b>What it does not do.</b> It does not rewrite your own <c>_logger.Log*</c> calls: a
///         value you interpolate into a message string yourself goes out as you wrote it, and no
///         attribute can intervene. It does not affect a generated <c>ToString()</c>, and it does not
///         flag OpenAPI schemas.
///     </para>
///     <para>
///         Two other mechanisms sit beside it, and neither replaces it.
///         <c>PragmaticDataRedactor</c> matches configured property-name patterns — it catches what
///         nobody annotated, but only where the name or the text has a recognisable shape.
///         <c>[PersonalData]</c> from Pragmatic.Privacy.Abstractions declares personal data with a
///         category and drives erasure, retention and the Article 30 register; it travels this same
///         redaction channel, carrying its reason. Use <c>[PersonalData]</c> when the value belongs
///         to someone, and this when it is a secret with no data subject — an API key has no right
///         of erasure.
///     </para>
///     <example>
///         <code>
///         public sealed record SupplierRateChanged
///         {
///             public required Guid SupplierId { get; init; }
///
///             [NotLogged]
///             public required string NegotiatedRate { get; init; }
///         }
///         </code>
///         Logged as a structured property — <c>logger.LogInformation("rate changed {Event}", e)</c>
///         — the rate comes out masked. Interpolated into the message yourself, it does not.
///     </example>
/// </remarks>
// Property only. A loose parameter has no type identity a type-keyed map can intercept, so
// [NotLogged] on one compiled and did nothing — the same lie in the type system the attribute
// itself was accused of. Supporting it would take a second, invocation-keyed contract, which the
// Pragmatic canon makes unnecessary: pipeline inputs are typed (mutation, query, message), and
// their marked properties are covered.
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotLoggedAttribute : Attribute;
