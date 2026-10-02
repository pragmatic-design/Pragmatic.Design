using Pragmatic.Patch.Attributes;

namespace Pragmatic.Patch.Samples;

/// <summary>
///     Patch DTO for <see cref="Product" />.
///     The source generator creates Optional properties for Name, Description, Price, Stock.
///     Id, CreatedAt, CreatedBy are excluded automatically.
/// </summary>
[GeneratePatch<Product>]
public partial record PatchProduct;
