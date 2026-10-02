namespace Pragmatic.Messaging.Dashboard.Dtos;

/// <summary>Aggregate health snapshot served by <c>GET {path}/status</c>.</summary>
public sealed record DashboardStatusDto(
    string Transport,
    string TransportStatus,
    int DeadLetters,
    int OutboxPending,
    int ActiveSagas);
