using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Documents.Writing;

/// <summary>The trackers registered, by type.</summary>
public static class DocTrackers
{
    private static readonly ConcurrentDictionary<Type, DocTracker> ByType = new();

    /// <summary>Called by the generated change detection at module start.</summary>
    public static void Register<T>() where T : class, IDocumentTracked<T> => ByType.TryAdd(typeof(T), new DocTracker<T>());

    /// <summary>The tracker of <paramref name="type" />; refused when the type is not a document a unit of work tracks.</summary>
    public static DocTracker For(Type type)
        => ByType.TryGetValue(type, out var tracker) ? tracker
            : throw new ArgumentException($"{type.Name} is not tracked as a document: a Pragmatic entity of an assembly the documents generator ran on is (its change detection registers it).", nameof(type));
}
