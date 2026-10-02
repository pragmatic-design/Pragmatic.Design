using Xunit;

namespace Showcase.Billing.Host.Tests;

/// <summary>One container and one host for the whole suite.</summary>
[CollectionDefinition(Name)]
public sealed class BillingHostCollection : ICollectionFixture<BillingHostFixture>
{
    public const string Name = "Billing host";
}
