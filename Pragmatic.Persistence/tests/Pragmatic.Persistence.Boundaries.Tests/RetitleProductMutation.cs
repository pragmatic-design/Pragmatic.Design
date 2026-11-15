using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Authoring;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renames a product and declares <see cref="ProductRenamed" />: invoked by <see cref="RelabelProductAction" />, it is a nested
///     operation whose event the owner of the commit delivers (#800).
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[Raises<ProductRenamed>]
public partial class RetitleProductMutation : Mutation<Product>
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }
}
