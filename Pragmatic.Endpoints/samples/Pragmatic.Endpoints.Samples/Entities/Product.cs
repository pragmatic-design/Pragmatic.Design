using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Endpoints.Samples.Entities;

/// <summary>
///     Sample entity demonstrating [Autocomplete] source generation.
///     <para>
///         With the configured RoutePrefix ("/api" in Program.cs) the generator produces:
///     </para>
///     <list type="bullet">
///         <item><description>GET /api/products/autocomplete/name — autocomplete on Name</description></item>
///         <item><description>GET /api/products/autocomplete/category — autocomplete on Category</description></item>
///     </list>
///     <para>
///         <c>[Entity]</c> lets the Persistence SG emit a nested <c>Product.Repository : IRepository&lt;Product&gt;</c>
///         which the autocomplete handlers resolve from DI via <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c>.
///     </para>
/// </summary>
[Entity]
public partial class Product : IEntity
{
    [Autocomplete]
    public string Name { get; set; } = "";

    [Autocomplete]
    public string Category { get; set; } = "";

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}
