namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Indicates that a parameter should be bound from the query string.
///     Pragmatic alternative to Microsoft.AspNetCore.Mvc.FromQueryAttribute,
///     so domain modules don't need an ASP.NET Core FrameworkReference.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FromQueryAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the query parameter name.
    ///     If not specified, the property/parameter name is used.
    /// </summary>
    public string? Name { get; set; }
}
