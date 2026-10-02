namespace Pragmatic.Integration.Tests.Infrastructure;

/// <summary>
///     Simple entity for integration testing.
/// </summary>
public sealed class TestOrder
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
