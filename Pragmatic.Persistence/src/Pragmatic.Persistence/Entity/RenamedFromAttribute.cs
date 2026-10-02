namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks a property as renamed from its previous column name.
///     The migration system will generate a RENAME COLUMN instead of DROP + ADD,
///     preserving existing data.
/// </summary>
/// <example>
///     <code>
///     [RenamedFrom("FirstName")]
///     public string GivenName { get; private set; }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RenamedFromAttribute : Attribute
{
    /// <summary>
    ///     Creates a <see cref="RenamedFromAttribute"/>.
    /// </summary>
    /// <param name="previousName">The previous column/property name; must not be null, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="previousName"/> is null, empty or whitespace.</exception>
    public RenamedFromAttribute(string previousName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousName);
        PreviousName = previousName;
    }

    /// <summary>The previous column/property name.</summary>
    public string PreviousName { get; }
}
