using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Filters;

/// <summary>What one filter of a read does in one execution.</summary>
public enum DocFilterState : byte
{
    /// <summary>The filter's condition is in the read.</summary>
    Active = 0,

    /// <summary>The filter is lifted: its condition is left out.</summary>
    Lifted = 1,

    /// <summary>No document of the entity is visible (an anonymous caller where a permission filter applies and fails closed).</summary>
    Closed = 2,
}
