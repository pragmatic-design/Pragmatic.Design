using Pragmatic.Result;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     A committed step and the way to undo it, held until the request either finishes or fails.
/// </summary>
/// <param name="ActionName">The action that committed — the name reported when the undo fails.</param>
/// <param name="Undo">
///     Runs the compensator and commits it through the same unit of work that committed the original
///     step, so the undo lands in the boundary that owns the data.
/// </param>
public sealed record CompensationEntry(
    string ActionName,
    Func<CancellationToken, Task<VoidResult<IError>>> Undo);
