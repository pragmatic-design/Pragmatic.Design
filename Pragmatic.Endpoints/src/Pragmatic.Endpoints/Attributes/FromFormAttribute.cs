namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Indicates that a parameter should be bound from form data.
///     Pragmatic alternative to Microsoft.AspNetCore.Mvc.FromFormAttribute,
///     so domain modules don't need an ASP.NET Core FrameworkReference.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FromFormAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the form field name.
    ///     If not specified, the property/parameter name is used.
    /// </summary>
    public string? Name { get; set; }
}
