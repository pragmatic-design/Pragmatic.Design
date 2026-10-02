namespace Pragmatic.Messaging.Dashboard.Dtos;

/// <summary>Outcome of a mutating dashboard operation (replay, delete).</summary>
public sealed record OperationResultDto(string Message);
