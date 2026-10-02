namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Registry entry for a saga type, registered in DI by the SG-generated
///     <c>AddPragmaticSagas()</c>. The delegate closes over the typed
///     <see cref="ISagaRepository{TSaga}"/> so ops surfaces can enumerate active instances
///     of every saga without reflection.
/// </summary>
public sealed record SagaDescriptor(
    Type SagaType,
    string Name,
    string StateTypeName,
    Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<SagaInstanceInfo>>> GetActiveAsync);
