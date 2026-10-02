namespace Showcase.Catalog.Properties.Endpoints;

/// <summary>
/// Endpoint group for property management.
/// Demonstrates: [EndpointGroup] with tag and API version.
/// </summary>
[EndpointGroup("/api/properties", Tag = "Properties")]
[ApiVersion("1.0")]
public sealed class PropertiesGroup;
