namespace Pragmatic.Privacy;

/// <summary>
///     What a data subject is asking for.
/// </summary>
public enum DataSubjectRequestType
{
    /// <summary>A copy of the personal data held about them.</summary>
    Access = 0,

    /// <summary>The same data in a portable, machine-readable form.</summary>
    Portability = 1,

    /// <summary>Erasure of their personal data.</summary>
    Erasure = 2,

    /// <summary>Correction of inaccurate data.</summary>
    Rectification = 3,

    /// <summary>
    ///     Processing suspended without deletion — the data stays, the use stops.
    /// </summary>
    Restriction = 4,

    /// <summary>Objection to a particular processing activity.</summary>
    Objection = 5
}
