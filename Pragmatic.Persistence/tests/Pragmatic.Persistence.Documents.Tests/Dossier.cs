using Pragmatic.Cryptography;
using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A note kept encrypted under its subject's key (crypto-shredding): a <c>ProtectedValue</c>, stored as the Base64 text of the bytes it was
///     encrypted to; a second one optional, absent until written. <c>ProtectedValueTests</c> writes and reads it.
/// </summary>
[Entity]
public partial class Dossier : IEntity
{
    public string Owner { get; set; } = "";

    public ProtectedValue Note { get; set; }

    public ProtectedValue? Recap { get; set; }
}
