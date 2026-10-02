namespace Pragmatic.Endpoints.ApiExplorer;

/// <summary>
///     One value a generated endpoint binds from the request, as the generator knows it.
/// </summary>
/// <param name="name">The name on the wire: the route key, query key, header name or form field.</param>
/// <param name="source">Where the value is read from.</param>
/// <param name="type">The type the value is bound to.</param>
/// <param name="isRequired">Whether the request is refused when the value is absent.</param>
public sealed class PragmaticParameterDescription(string name, PragmaticParameterSource source, Type type, bool isRequired)
{
    /// <summary>The name on the wire.</summary>
    public string Name { get; } = name;

    /// <summary>Where the value is read from.</summary>
    public PragmaticParameterSource Source { get; } = source;

    /// <summary>The type the value is bound to.</summary>
    public Type Type { get; } = type;

    /// <summary>Whether the request is refused when the value is absent.</summary>
    public bool IsRequired { get; } = isRequired;
}
