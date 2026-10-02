using System.Runtime.CompilerServices;

namespace Pragmatic.Testing.Assertions;

/// <summary><c>Should()</c> for <see cref="Guid"/>.</summary>
public static class GuidAssertionExtensions
{
    /// <summary>Assertions for a <see cref="Guid"/>.</summary>
    public static GuidAssertions Should(
        this Guid subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="Guid"/>.</summary>
    public static GuidAssertions Should(
        this Guid? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);
}
