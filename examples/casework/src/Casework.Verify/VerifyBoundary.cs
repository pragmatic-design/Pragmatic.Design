using Pragmatic.Messaging.Attributes;

namespace Casework.Verify;

/// <summary>
///     Verify: what was asked, what came back, and when.
/// </summary>
/// <remarks>
///     <c>[EnableOutbox]</c>, for the same reason Intake has it and with the same meaning: this service
///     <b>publishes</b> too — the answer and the message announcing it are one
///     transaction, so an answer recorded and never announced cannot happen. The two services are
///     symmetric in this, which is the point: neither is the "server" of the other.
/// </remarks>
[Boundary]
[EnableOutbox]
public partial class VerifyBoundary;
