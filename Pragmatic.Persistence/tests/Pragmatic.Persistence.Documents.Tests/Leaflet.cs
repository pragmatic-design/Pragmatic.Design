using Pragmatic.Internationalization.Types;
using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A leaflet printed in several languages: its title a <c>LocalizedString</c>, stored as the JSON text EF Core's converter writes; a subtitle
///     optional, absent until written. <c>LocalizedStringTests</c> writes and reads it.
/// </summary>
[Entity]
public partial class Leaflet : IEntity
{
    public string Code { get; set; } = "";

    public LocalizedString Title { get; set; } = new();

    public LocalizedString? Subtitle { get; set; }
}
