namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Indicates that a parameter should be bound from the request body.
///     Pragmatic alternative to Microsoft.AspNetCore.Mvc.FromBodyAttribute,
///     so domain modules don't need an ASP.NET Core FrameworkReference.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FromBodyAttribute : Attribute;
