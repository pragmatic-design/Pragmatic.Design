namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Indicates that a parameter should be bound from the route.
///     Pragmatic alternative to Microsoft.AspNetCore.Mvc.FromRouteAttribute,
///     so domain modules don't need an ASP.NET Core FrameworkReference.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FromRouteAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the route parameter name.
    ///     If not specified, the property/parameter name is used.
    /// </summary>
    public string? Name { get; set; }
}
