using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using App;
using Pragmatic.Serialization;

// W3 end-to-end: the [Job] + [MapFrom] below make the Pragmatic SG emit a JsonSerializerContext covering
// their types. The opt-in here is purely the csproj build property (<PublishAot>/<PragmaticGenerateJsonContext>) —
// no assembly attribute needed. We register that GENERATED context into the seam, disable the reflection
// fallback, and round-trip, all inside a Native AOT binary. If the context were wrong, this would fail to
// publish or throw.

var built = new PragmaticJsonOptions()
    .AddContext(Pragmatic.Aot.GeneratedContext.Generated.PragmaticJsonContext.Default)
    .DisableReflectionFallback()
    .Build();

// A copy, because Build() returns options already sealed — and PragmaticJsonOptions exposes no
// converter hook, so outside ASP.NET this is the only way to add one. The converter itself mirrors
// what the generated host entry point registers: without it this smoke would exercise a configuration
// no real application runs, and the enum check below would be measuring nothing.
var options = new JsonSerializerOptions(built);
options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

var original = new JobParams { Name = "widget", Count = 3, Tags = ["a", "b"] };

var typeInfo = (JsonTypeInfo<JobParams>)options.GetTypeInfo(typeof(JobParams));
var json = JsonSerializer.Serialize(original, typeInfo);
var back = JsonSerializer.Deserialize(json, typeInfo);

if (back is null || back.Name != original.Name || back.Count != original.Count || back.Tags.Count != 2)
{
    Console.Error.WriteLine("AOT-GEN-FAIL: job round-trip mismatch");
    return 1;
}

// HTTP surface: a [MapFrom] DTO is an HTTP request/response shape — it must also be covered by the
// generated context and round-trip under Native AOT with the reflection fallback disabled.
var dto = new CustomerDto { Name = "acme", Age = 42 };
var dtoInfo = (JsonTypeInfo<CustomerDto>)options.GetTypeInfo(typeof(CustomerDto));
var dtoJson = JsonSerializer.Serialize(dto, dtoInfo);
var dtoBack = JsonSerializer.Deserialize(dtoJson, dtoInfo);

if (dtoBack is null || dtoBack.Name != dto.Name || dtoBack.Age != dto.Age)
{
    Console.Error.WriteLine("AOT-GEN-FAIL: DTO round-trip mismatch");
    return 1;
}

// init-only DTO: assigned via [UnsafeAccessor] setters under Native AOT.
var profile = new ProfileDto { Name = "carla", Age = 30 };
var profileInfo = (JsonTypeInfo<ProfileDto>)options.GetTypeInfo(typeof(ProfileDto));
var profileBack = JsonSerializer.Deserialize(JsonSerializer.Serialize(profile, profileInfo), profileInfo);
if (profileBack is null || profileBack.Name != "carla" || profileBack.Age != 30)
{
    Console.Error.WriteLine("AOT-GEN-FAIL: init-only DTO round-trip mismatch");
    return 1;
}

// positional record: constructed via an [UnsafeAccessor] ctor under Native AOT.
var line = new OrderLineDto("SKU-1", 5);
var lineInfo = (JsonTypeInfo<OrderLineDto>)options.GetTypeInfo(typeof(OrderLineDto));
var lineJson = JsonSerializer.Serialize(line, lineInfo);
var lineBack = JsonSerializer.Deserialize(lineJson, lineInfo);
if (lineBack is null || lineBack.Sku != "SKU-1" || lineBack.Quantity != 5)
{
    Console.Error.WriteLine("AOT-GEN-FAIL: record round-trip mismatch");
    return 1;
}

// Enums, and the reason this check exists: a generated context that bakes in the numeric enum
// converter sends an enum out as "Shipped" through the reflection path and 2 through this one —
// turning on <PublishAot> would silently change the wire format of every enum in an API. A fixture
// that covers no enum cannot see that, however much of the AOT path it exercises.
var shipment = new ShipmentDto { Reference = "SH-9", Status = ShipmentStatus.Shipped };
var shipmentInfo = (JsonTypeInfo<ShipmentDto>)options.GetTypeInfo(typeof(ShipmentDto));
var shipmentJson = JsonSerializer.Serialize(shipment, shipmentInfo);

if (!shipmentJson.Contains("\"Shipped\"", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"AOT-GEN-FAIL: enum serialized as a number, not a name: {shipmentJson}");
    return 1;
}

var shipmentBack = JsonSerializer.Deserialize(shipmentJson, shipmentInfo);
if (shipmentBack is null || shipmentBack.Status != ShipmentStatus.Shipped)
{
    Console.Error.WriteLine("AOT-GEN-FAIL: enum round-trip mismatch");
    return 1;
}

// required members. `new T()` does not compile against them (CS9035), so the context constructs the
// type through the [UnsafeAccessor] ctor instead — the same parameterless ctor, without the language
// check the deserializer is about to satisfy anyway. Every generated request body is this shape, so
// without the accessor the context would not compile for any of them.
var ticket = new TicketDto { Code = "T-77", Note = "urgent" };
var ticketInfo = (JsonTypeInfo<TicketDto>)options.GetTypeInfo(typeof(TicketDto));
var ticketJson = JsonSerializer.Serialize(ticket, ticketInfo);
var ticketBack = JsonSerializer.Deserialize(ticketJson, ticketInfo);

if (ticketBack is null || ticketBack.Code != "T-77" || ticketBack.Note != "urgent")
{
    Console.Error.WriteLine("AOT-GEN-FAIL: required-member DTO round-trip mismatch");
    return 1;
}

Console.WriteLine($"AOT-GEN-OK: {json} | {dtoJson} | {lineJson} | {shipmentJson} | {ticketJson}");
return 0;

namespace App
{
    using Pragmatic.Jobs;
    using Pragmatic.Jobs.Attributes;
    using Pragmatic.Mapping.Attributes;

    public sealed class JobParams
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    [Job]
    public partial class SampleJob : IJob<JobParams>
    {
        public Task ExecuteAsync(JobParams parameters, JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    // The HTTP request/response source and its mapping DTO. [MapFrom] marks the DTO as a serialization
    // boundary, so the W3 SG covers it in the generated context.
    public sealed class Customer
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [MapFrom<Customer>]
    public sealed partial class CustomerDto
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    // init-only DTO — the idiomatic modern shape. Covered via [UnsafeAccessor] setters.
    public sealed class Profile
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [MapFrom<Profile>]
    public sealed partial class ProfileDto
    {
        public string Name { get; init; } = "";
        public int Age { get; init; }
    }

    // positional record — no parameterless ctor. A [MapFrom] DTO (the Mapping SG now generates
    // primary-constructor construction), covered by the W3 context via an [UnsafeAccessor] ctor.
    public sealed class OrderLine
    {
        public string Sku { get; set; } = "";
        public int Quantity { get; set; }
    }

    [MapFrom<OrderLine>]
    public sealed partial record OrderLineDto(string Sku, int Quantity);

    // An enum on a covered DTO. The host registers JsonStringEnumConverter, so the generated context
    // must honour it rather than fall back to the numeric one — the two serialization paths have to
    // produce the same bytes, or enabling AOT is a breaking API change.
    public enum ShipmentStatus { Pending = 0, Packed = 1, Shipped = 2 }

    public sealed class Shipment
    {
        public string Reference { get; set; } = "";
        public ShipmentStatus Status { get; set; }
    }

    [MapFrom<Shipment>]
    public sealed partial class ShipmentDto
    {
        public string Reference { get; set; } = "";
        public ShipmentStatus Status { get; set; }
    }

    // A required member on a covered type — what the generator emits for every request body whose
    // action declares one.
    public sealed class Ticket
    {
        public string Code { get; set; } = "";
        public string Note { get; set; } = "";
    }

    [MapFrom<Ticket>]
    public sealed partial class TicketDto
    {
        public required string Code { get; init; }
        public string Note { get; init; } = "";
    }
}
