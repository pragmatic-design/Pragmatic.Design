using Pragmatic.Result;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     A committed step that could not be undone.
/// </summary>
/// <param name="ActionName">The action whose work is still committed.</param>
/// <param name="Error">Why the undo failed.</param>
public sealed record CompensationFailure(string ActionName, IError Error);
