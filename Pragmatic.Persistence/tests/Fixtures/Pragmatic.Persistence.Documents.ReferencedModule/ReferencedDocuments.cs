using Pragmatic.Persistence.Documents;

namespace Pragmatic.Persistence.ReferencedModule;

/// <summary>
///     The module's documents, written through its own session: ACME with the parts A-1 and A-2, BOLT with B-1, and GONE (delisted, which its
///     rule hides) with G-1. The names carry a prefix of the caller's, so runs on a shared server do not read each other's.
/// </summary>
public static class ReferencedDocuments
{
    public static async Task<(Guid Acme, Guid Bolt, Guid Gone)> SeedAsync(DocSession session, string prefix)
    {
        Supplier Supplier(string name, string status)
        {
            var supplier = new Supplier();
            supplier.Rename(prefix + name);
            supplier.SetStatus(status);
            return supplier;
        }

        var (acme, bolt, gone) = (Supplier("ACME", "LISTED"), Supplier("BOLT", "LISTED"), Supplier("GONE", "DELISTED"));
        await session.InsertManyAsync([acme, bolt, gone]);
        var parts = new[] { ("A-1", acme), ("A-2", acme), ("B-1", bolt), ("G-1", gone) }.Select(p =>
        {
            var part = new Part();
            part.Describe(prefix + p.Item1, p.Item2.Id);
            return part;
        }).ToList();
        await session.InsertManyAsync(parts);
        return (acme.Id, bolt.Id, gone.Id);
    }
}
