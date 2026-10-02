namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     How a request value becomes a parameter of the declared type.
/// </summary>
/// <remarks>
///     Decided at compile time, from the symbol, so the generated code calls one concrete
///     <c>RequestBinder</c> overload and nothing inspects a <see cref="System.Type" /> at run time.
///     ASP.NET's own binder does this by reflection, which is what makes it unusable under AOT.
/// </remarks>
internal enum BindKind
{
    /// <summary>
    ///     Nobody has classified this parameter yet.
    /// </summary>
    /// <remarks>
    ///     Explicit rather than implied by the default of the first member: when <c>String</c> was the
    ///     zero value, every parameter a model builder left alone looked like a string, and a route
    ///     <c>Guid</c> was silently bound as text — caught only because the generated code then failed
    ///     to compile.
    /// </remarks>
    Unset = 0,

    /// <summary>No conversion: the raw value is the value.</summary>
    String,

    /// <summary>Parsed by name or numeric value. Enums are not <c>IParsable</c>, hence their own kind.</summary>
    Enum,

    /// <summary><c>IParsable&lt;T&gt;</c> — numbers, dates, <c>Guid</c>, <c>bool</c>.</summary>
    Parsable,

    /// <summary>Nothing simple: bound some other way, or not bindable from a single string at all.</summary>
    Complex,
}
