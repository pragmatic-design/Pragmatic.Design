using Pragmatic.Testing.Assertions;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class InMemoryStoreTests
{
    private readonly InMemoryNotificationStore _store = new();

    private static NotificationRecord CreateRecord(DeliveryStatus status = DeliveryStatus.Pending) => new()
    {
        Audience = NotificationAudience.EndUser,
        RecipientAddress = "user@example.com",
        Channel = NotificationChannel.Email,
        Subject = "Test notification",
        Status = status,
    };

    [Fact]
    public async Task Create_StoresRecord()
    {
        var record = CreateRecord();

        var result = await _store.CreateAsync(record);

        result.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task GetById_ReturnsStoredRecord()
    {
        var record = CreateRecord();
        await _store.CreateAsync(record);

        var result = await _store.GetByIdAsync(record.Id);

        result.Should().NotBeNull();
        result!.Subject.Should().Be("Test notification");
    }

    [Fact]
    public async Task GetById_WithUnknownId_ReturnsNull()
    {
        var result = await _store.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStatus_UpdatesRecord()
    {
        var record = CreateRecord();
        await _store.CreateAsync(record);

        await _store.UpdateStatusAsync(record.Id, DeliveryStatus.Sent, null);

        var updated = await _store.GetByIdAsync(record.Id);
        updated!.Status.Should().Be(DeliveryStatus.Sent);
        updated.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatus_ToFailed_SetsErrorMessage()
    {
        var record = CreateRecord();
        await _store.CreateAsync(record);

        await _store.UpdateStatusAsync(record.Id, DeliveryStatus.Failed, "Connection refused");

        var updated = await _store.GetByIdAsync(record.Id);
        updated!.Status.Should().Be(DeliveryStatus.Failed);
        updated.ErrorMessage.Should().Be("Connection refused");
    }

    [Fact]
    public async Task GetPending_ReturnsOnlyPendingRecords()
    {
        await _store.CreateAsync(CreateRecord(DeliveryStatus.Pending));
        await _store.CreateAsync(CreateRecord(DeliveryStatus.Sent));
        await _store.CreateAsync(CreateRecord(DeliveryStatus.Pending));

        var pending = await _store.GetPendingAsync(10);

        pending.Should().HaveCount(2);
        pending.Should().AllSatisfy(r => r.Status.Should().Be(DeliveryStatus.Pending));
    }

    [Fact]
    public async Task GetPending_RespectsLimit()
    {
        await _store.CreateAsync(CreateRecord());
        await _store.CreateAsync(CreateRecord());
        await _store.CreateAsync(CreateRecord());

        var pending = await _store.GetPendingAsync(2);

        pending.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateStatus_ToDelivered_SetsDeliveredAt()
    {
        var record = CreateRecord();
        await _store.CreateAsync(record);

        await _store.UpdateStatusAsync(record.Id, DeliveryStatus.Delivered, null);

        var updated = await _store.GetByIdAsync(record.Id);
        updated!.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatus_ToRead_SetsReadAt()
    {
        var record = CreateRecord();
        await _store.CreateAsync(record);

        await _store.UpdateStatusAsync(record.Id, DeliveryStatus.Read, null);

        var updated = await _store.GetByIdAsync(record.Id);
        updated!.ReadAt.Should().NotBeNull();
    }
}
