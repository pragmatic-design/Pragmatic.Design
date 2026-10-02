using System.Xml;

namespace Pragmatic.Documents.Ooxml;

/// <summary>Tracks and writes OOXML relationship entries (.rels files).</summary>
public sealed class OoxmlRelationships
{
    private readonly List<(string Id, string Type, string Target, bool External)> _entries = [];
    private int _nextId = 1;

    /// <summary>Add a relationship and return its rId.</summary>
    public string Add(string type, string target, bool external = false)
    {
        var id = $"rId{_nextId++}";
        _entries.Add((id, type, target, external));
        return id;
    }

    /// <summary>Write the relationships XML to the given stream.</summary>
    public void WriteTo(Stream stream)
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("Relationships", Ns.Rel);

        foreach (var (id, type, target, external) in _entries)
        {
            w.WriteStartElement("Relationship", Ns.Rel);
            w.WriteAttributeString("Id", id);
            w.WriteAttributeString("Type", type);
            w.WriteAttributeString("Target", target);
            if (external)
                w.WriteAttributeString("TargetMode", "External");
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }
}
