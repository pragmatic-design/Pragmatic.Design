namespace Pragmatic.Audit;

/// <summary>Broad classification of an audit entry, for querying and for retention.</summary>
public enum AuditCategory
{
    /// <summary>Authentication, authorization, and other security-relevant events.</summary>
    Security = 0,

    /// <summary>Changes to business data.</summary>
    Data = 1,

    /// <summary>Changes to configuration.</summary>
    Configuration = 2,

    /// <summary>Message handling outcomes.</summary>
    Message = 3,

    /// <summary>Data-subject rights, consent, retention, key destruction.</summary>
    Privacy = 4
}
