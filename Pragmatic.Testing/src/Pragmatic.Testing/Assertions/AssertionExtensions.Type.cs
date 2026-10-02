using System.Runtime.CompilerServices;

namespace Pragmatic.Testing.Assertions;

/// <summary><c>Should()</c> for a <see cref="Type"/>.</summary>
public static class TypeAssertionExtensions
{
    /// <summary>Assertions about a type.</summary>
    /// <remarks>
    ///     Preferred over the object overload, which a <see cref="Type"/> also matches — a
    ///     <c>Type</c> is an object, and without saying which wins the specific family is
    ///     unreachable.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static TypeAssertions Should(
        this Type? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);
}
