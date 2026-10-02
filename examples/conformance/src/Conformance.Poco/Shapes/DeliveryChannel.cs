namespace Conformance.Poco.Shapes;

/// <summary>
///     How a delivery leaves.
/// </summary>
/// <remarks>
///     ⚠️ <c>Unknown = 0</c> is deliberate and is what makes
///     <c>[MapEnum(OnUnknown = UnknownEnumValue.Default)]</c> declarable: without a member meaning «I do
///     not know», the enum's default would be a real value, and a name nobody recognizes would land on
///     it pretending to be data.
/// </remarks>
public enum DeliveryChannel
{
    Unknown = 0,
    Post,
    Courier,
    Pickup
}
