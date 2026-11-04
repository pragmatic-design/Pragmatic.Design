using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.ReferencedModule;

/// <summary>
///     The module's rows, written through its own session: ACME with the parts A-1 and A-2, BOLT with B-1, and two suppliers its rule hides,
///     GONE (delisted) with G-1 and EVIL (banned) with E-1.
/// </summary>
public static class ReferencedRows
{
    public static async Task<(Guid Acme, Guid Bolt)> SeedAsync(SqlSession session)
    {
        Supplier Supplier(string name, string status)
        {
            var supplier = new Supplier();
            supplier.Rename(name);
            supplier.SetStatus(status);
            return supplier;
        }

        var (acme, bolt, gone, evil) = (Supplier("ACME", "LISTED"), Supplier("BOLT", "LISTED"), Supplier("GONE", "DELISTED"), Supplier("EVIL", SupplierStatuses.Banned));
        foreach (var supplier in new[] { acme, bolt, gone, evil })
            await session.InsertAsync(supplier);
        foreach (var (code, supplier) in new[] { ("A-1", acme), ("A-2", acme), ("B-1", bolt), ("G-1", gone), ("E-1", evil) })
        {
            var part = new Part();
            part.Describe(code, supplier.Id);
            await session.InsertAsync(part);
        }

        return (acme.Id, bolt.Id);
    }
}
