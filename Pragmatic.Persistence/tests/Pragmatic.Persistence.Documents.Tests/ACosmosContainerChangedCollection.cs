using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>The servers of <see cref="ACosmosContainerChangeTests" /> alone: an emulator the rest of the suite has not run on (#951).</summary>
[CollectionDefinition("a cosmos container changed")]
public sealed class ACosmosContainerChangedCollection : ICollectionFixture<DocumentServers>;
