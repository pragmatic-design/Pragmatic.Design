namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Indicates that a parameter should be bound from a request header.
///     Pragmatic alternative to Microsoft.AspNetCore.Mvc.FromHeaderAttribute,
///     so domain modules don't need an ASP.NET Core FrameworkReference.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FromHeaderAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the header name.
    ///     If not specified, the property/parameter name is used.
    /// </summary>
    public string? Name { get; set; }
}
