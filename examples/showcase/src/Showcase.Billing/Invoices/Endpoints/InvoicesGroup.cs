namespace Showcase.Billing.Endpoints;

/// <summary>
/// Endpoint group for invoice management.
/// Demonstrates: [EndpointGroup] with tag and API version.
/// </summary>
[EndpointGroup("/api/invoices", Tag = "Invoices")]
[ApiVersion("1.0")]
public sealed class InvoicesGroup;
