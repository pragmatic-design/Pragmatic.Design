namespace Casework.Intake.Entities;

/// <summary>How the organisation's correspondence settings are read.</summary>
public static partial class CorrespondenceSettingsSpecifications
{
    /// <summary>
    ///     The organisation's row — there is at most one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It looks like a specification that says nothing, and it says the only thing there is to
    ///     say: <b>which organisation</b> is the tenant filter's answer, not this rule's, and a rule that
    ///     repeated it would be a second answer that can disagree. Written out rather than reaching for
    ///     a "find everything" call, so the call site reads as one row rather than as a list somebody
    ///     took the first of.
    /// </remarks>
    public static Specification<CorrespondenceSettings> TheOnlyOne()
        => Spec<CorrespondenceSettings>.Where(settings => settings.SenderAddress != "");
}
