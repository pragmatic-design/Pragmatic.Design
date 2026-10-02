using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

public class InMemoryDeadLetterStoreTests
{
    [Fact]
    public async Task StoreAsync_ShouldAddMessage()
    {
        var store = new InMemoryDeadLetterStore();
        var message = CreateDeadLetter("TestEvent");

        await store.StoreAsync(message);

        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_ById_ReturnsMessageOrNull()
    {
        var store = new InMemoryDeadLetterStore();
        var message = CreateDeadLetter("TestEvent");
        await store.StoreAsync(message);

        (await store.GetAsync(message.Id)).Should().Be(message);
        (await store.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_RemovesMessage_AndIsIdempotent()
    {
        var store = new InMemoryDeadLetterStore();
        var message = CreateDeadLetter("TestEvent");
        await store.StoreAsync(message);

        await store.RemoveAsync(message.Id);
        await store.RemoveAsync(message.Id); // idempotent

        store.Count.Should().Be(0);
        (await store.GetAsync(message.Id)).Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllStoredMessages()
    {
        var store = new InMemoryDeadLetterStore();
        await store.StoreAsync(CreateDeadLetter("Event1"));
        await store.StoreAsync(CreateDeadLetter("Event2"));
        await store.StoreAsync(CreateDeadLetter("Event3"));

        var all = await store.GetAllAsync();

        all.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAllAsync_WhenEmpty_ShouldReturnEmptyList()
    {
        var store = new InMemoryDeadLetterStore();

        var all = await store.GetAllAsync();

        all.Should().BeEmpty();
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllMessages()
    {
        var store = new InMemoryDeadLetterStore();
        await store.StoreAsync(CreateDeadLetter("Event1"));
        await store.StoreAsync(CreateDeadLetter("Event2"));

        store.Clear();

        store.Count.Should().Be(0);
        var all = await store.GetAllAsync();
        all.Should().BeEmpty();
    }

    [Fact]
    public void Count_ShouldReturnZeroInitially()
    {
        var store = new InMemoryDeadLetterStore();
        store.Count.Should().Be(0);
    }

    private static DeadLetterMessage CreateDeadLetter(string messageType)
        => new(
            MessageType: messageType,
            Payload: "{}",
            Error: "Test error",
            RetryCount: 3,
            Context: MessageContext.New(),
            FailedAt: DateTimeOffset.UtcNow);
}
