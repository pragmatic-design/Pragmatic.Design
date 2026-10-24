using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>Where a <see cref="VersionAttribute" /> property is stored.</summary>
public static class DocVersion
{
    /// <summary>The field name of the version: the property's own name is not used, so the session's filters can name it without the model.</summary>
    public const string Field = "_v";
}
