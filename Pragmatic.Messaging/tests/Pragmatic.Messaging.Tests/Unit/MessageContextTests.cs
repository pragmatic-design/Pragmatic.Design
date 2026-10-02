using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

public class MessageContextTests
{
    [Fact]
    public void New_ShouldGenerateUniqueMessageId()
    {
        var ctx1 = MessageContext.New();
        var ctx2 = MessageContext.New();

        ctx1.MessageId.Should().NotBeNullOrEmpty();
        ctx2.MessageId.Should().NotBeNullOrEmpty();
        ctx1.MessageId.Should().NotBe(ctx2.MessageId);
    }

    [Fact]
    public void New_ShouldSetOptionalProperties()
    {
        var ctx = MessageContext.New(
            correlationId: "corr-1",
            tenantId: "tenant-1",
            userId: "user-1");

        ctx.CorrelationId.Should().Be("corr-1");
        ctx.TenantId.Should().Be("tenant-1");
        ctx.UserId.Should().Be("user-1");
        ctx.RetryCount.Should().Be(0);
    }

    [Fact]
    public void New_WithoutParameters_ShouldHaveNullOptionals()
    {
        var ctx = MessageContext.New();

        ctx.CorrelationId.Should().BeNull();
        ctx.TenantId.Should().BeNull();
        ctx.UserId.Should().BeNull();
        ctx.Headers.Should().BeNull();
        ctx.RetryCount.Should().Be(0);
    }

    [Fact]
    public void ForRetry_ShouldIncrementRetryCount()
    {
        var ctx = MessageContext.New(correlationId: "corr-1");
        var retry1 = ctx.ForRetry();
        var retry2 = retry1.ForRetry();

        retry1.RetryCount.Should().Be(1);
        retry2.RetryCount.Should().Be(2);
        retry1.CorrelationId.Should().Be("corr-1");
        retry2.MessageId.Should().Be(ctx.MessageId);
    }

    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        var headers = new Dictionary<string, string> { ["key"] = "value" };
        var ctx = new MessageContext(
            MessageId: "msg-1",
            CorrelationId: "corr-1",
            TenantId: "t-1",
            UserId: "u-1",
            Headers: headers,
            RetryCount: 3);

        ctx.MessageId.Should().Be("msg-1");
        ctx.CorrelationId.Should().Be("corr-1");
        ctx.TenantId.Should().Be("t-1");
        ctx.UserId.Should().Be("u-1");
        ctx.Headers.Should().ContainKey("key");
        ctx.RetryCount.Should().Be(3);
    }
}
