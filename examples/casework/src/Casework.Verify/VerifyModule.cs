namespace Casework.Verify;

/// <summary>
///     The module its host includes. It knows nothing of Intake's case beyond the identifier the request
///     carried, and nothing at all of the saga waiting for its answer.
/// </summary>
[Module(Name = "Casework.Verify", Version = "1.0.0",
    Description = "Verifications and their outcomes, answered when they are answered")]
public sealed class VerifyModule;
