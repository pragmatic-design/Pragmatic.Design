namespace PragmaticEntities.Tests;

/// <summary>A Sql engine row whose key the database assigns, stored in <see cref="TicketDatabase" />: what <see cref="IdentityKeyTests" /> migrates and writes.</summary>
[Pragmatic.Persistence.Sql.Attributes.Entity]
[Pragmatic.Persistence.Sql.Attributes.StoredIn<TicketDatabase>]
public sealed class Ticket
{
    [Pragmatic.Persistence.Sql.Attributes.Identity]
    public long Id { get; set; }

    public string Subject { get; set; } = "";
}
