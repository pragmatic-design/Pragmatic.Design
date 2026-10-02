using Conformance.Poco.Shapes;
using Pragmatic.Mapping;
using Pragmatic.Mapping.Attributes;

namespace Conformance.Poco.Dtos;

/// <summary>
///     The delivery on the wire: the channel is a <b>string</b>, and the target is an enum.
/// </summary>
/// <remarks>
///     <para>
///         Enums are mapped <b>by name</b>, deliberately. What <c>[MapEnum(OnUnknown = …)]</c> decides is
///         what happens to a name that matches no member: without it, an exception — and on a write path
///         an exception is a <b>500</b> for a request that was simply wrong.
///     </para>
///     <para>
///         Here it lands on <c>DeliveryChannel.Unknown</c>, which exists for this. It is the cell of an
///         old client that sends a channel that no longer exists.
///     </para>
/// </remarks>
[MapFrom<Delivery>]
[MapTo<Delivery>]
public partial record DeliveryDto
{
    public string Reference { get; init; } = "";

    [MapEnum(OnUnknown = UnknownEnumValue.Default)]
    public string Channel { get; init; } = "";
}
