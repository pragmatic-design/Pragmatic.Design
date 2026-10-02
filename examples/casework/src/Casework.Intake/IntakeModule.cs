namespace Casework.Intake;

/// <summary>
///     The module its host includes. It depends on nothing, and in particular not on Verify: the two
///     services share events, never types.
/// </summary>
[Module(Name = "Casework.Intake", Version = "1.0.0",
    Description = "Cases, their documents, and the process that carries one to a decision")]
public sealed class IntakeModule;
