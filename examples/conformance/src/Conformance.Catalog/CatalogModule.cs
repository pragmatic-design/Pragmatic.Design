using Pragmatic.Composition.Attributes;

namespace Conformance.Catalog;

/// <summary>
///     The second module the host includes.
/// </summary>
[Module(Name = "Conformance.Catalog", Version = "1.0.0",
    Description = "The owner of the entity Sales reads across the boundary")]
public sealed class CatalogModule;
