using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Dashboard.Dtos;

/// <summary>Active instances of one saga type, served by <c>GET {path}/sagas</c>.</summary>
public sealed record SagaGroupDto(
    string Name,
    string StateType,
    IReadOnlyList<SagaInstanceInfo> Active);
