using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>A support case, numbered by a counter when it is saved (<c>[GeneratedValue]</c> with <c>{SEQ}</c>, #562). <c>GeneratedValueTests</c> saves two.</summary>
[Entity]
public partial class SupportCase : IEntity
{
    [GeneratedValue("CASE-{SEQ:4}")]
    public string Number { get; private set; } = "";

    public string Subject { get; set; } = "";
}
