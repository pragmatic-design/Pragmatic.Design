using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Generated;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Unit;

/// <summary>
///     The registry the generator writes for this assembly, on a type name and a payload another service wrote
///     (#158, #312): an outbox row, a scheduled publish, a dead letter replayed. For any pair the outcome is the
///     message, null for a type the registry does not know, or a <see cref="JsonException" />, which every caller
///     turns into a retry or a dead letter.
/// </summary>
public class MessageTypeRegistryPropertyTests
{
    private static readonly PragmaticMessageTypeRegistry Registry = new();

    private static readonly string[] TypeNames =
    [
        typeof(AnOrderWasPlaced).FullName!, typeof(AProvisioningWasAsked).FullName!, typeof(AnImportWasAsked).FullName!,
        "global::" + typeof(AnOrderWasPlaced).FullName, typeof(AnOrderWasPlaced).Name, "System.String", "", " ",
    ];

    private static readonly string[] Keys = ["id", "amount", "placedAt", "quantity", "status", "lines", "reference", "organisation", "x"];

    private static readonly string[] Scalars =
    [
        "0", "-1", "2147483648", "1.5", "1e400", "79228162514264337593543950336", "true", "null", "\"\"", "\"x\"",
        "\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"", "\"2026-10-19T12:00:00Z\"", "\"Placed\"", "\"§\"",
    ];

    private static Gen<string> Value(int depth)
    {
        var scalar = Gen.Elements(Scalars);
        if (depth == 0)
            return scalar;

        var inner = Value(depth - 1);
        return Gen.Frequency(
            (3, scalar),
            (1, inner.ListOf().Select(items => $"[{string.Join(",", items)}]")),
            (3, Gen.Elements(Keys).Zip(inner).Select(pair => $"\"{pair.Item1}\":{pair.Item2}")
                .ListOf().Select(members => $"{{{string.Join(",", members)}}}")));
    }

    // The payload column is text: a string holds no byte that is not UTF-8, so the vocabulary and arbitrary strings
    // are the whole of what a row can carry.
    private static readonly Arbitrary<string> Payloads =
        Gen.Frequency((3, Value(3)), (1, ArbMap.Default.ArbFor<string>().Generator)).ToArbitrary();

    [Fact]
    public void Any_type_name_and_payload_give_the_message_null_or_a_refusal()
        => Prop.ForAll(Gen.Elements(TypeNames).ToArbitrary(), Payloads, (typeName, payload) =>
        {
            try
            {
                var message = Registry.Deserialize(typeName, payload);
                return message is null || message.GetType().FullName == typeName;
            }
            catch (JsonException)
            {
                return true;
            }
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(2000));

    // The control: without it, a registry that answered null to everything would satisfy the property.
    [Fact]
    public void A_message_the_registry_knows_reads_back_as_written()
    {
        var placed = new AnOrderWasPlaced(Guid.NewGuid(), 19.95m, DateTimeOffset.UnixEpoch, 3, OrderStatus.Placed, ["a"]);
        var typeInfo = (JsonTypeInfo<AnOrderWasPlaced>)Serialization.PragmaticJsonOptions.Default.Build()
            .GetTypeInfo(typeof(AnOrderWasPlaced));

        var read = (AnOrderWasPlaced?)Registry.Deserialize(typeof(AnOrderWasPlaced).FullName!, JsonSerializer.Serialize(placed, typeInfo));

        read.Should().NotBeNull();
        (read! with { Lines = placed.Lines }).Should().Be(placed);
        read.Lines.Should().Equal(placed.Lines);
    }
}

/// <summary>A message with members of the kinds a payload can get wrong, for the registry to read.</summary>
public sealed record AnOrderWasPlaced(
    Guid Id, decimal Amount, DateTimeOffset PlacedAt, int Quantity, OrderStatus Status, string[] Lines);

/// <summary>The status <see cref="AnOrderWasPlaced" /> carries.</summary>
public enum OrderStatus
{
    Placed = 1,
    Shipped = 2,
}

/// <summary>Consumes <see cref="AnOrderWasPlaced" />, which is what puts it in the generated registry.</summary>
[MessageHandler]
public sealed partial class TheOrderThatWasPlaced : IMessageHandler<AnOrderWasPlaced>
{
    public Task HandleAsync(AnOrderWasPlaced message, MessageContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
