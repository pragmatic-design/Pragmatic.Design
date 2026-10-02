using Pragmatic.Testing.Assertions;
using Pragmatic.MultiTenancy;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class TenantInfoTests
{
    private static TenantInfo Create(string? connectionString) => new()
    {
        TenantId = "t1",
        TenantName = "Tenant One",
        ConnectionString = connectionString,
        State = TenantState.Active,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void ToString_WithConnectionString_MasksTheValue()
    {
        var text = Create("Server=db;Password=hunter2").ToString();

        text.Should().Contain("ConnectionString = ***", "credentials must never reach logs");
        text.Should().NotContain("hunter2");
        text.Should().NotContain("Server=db");
        text.Should().Contain("TenantId = t1");
    }

    [Fact]
    public void ToString_WithoutConnectionString_PrintsNull()
        => Create(null).ToString().Should().Contain("ConnectionString = null");
}
