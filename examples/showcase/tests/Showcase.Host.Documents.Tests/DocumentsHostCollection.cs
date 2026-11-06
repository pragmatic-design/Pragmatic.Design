using Xunit;

namespace Showcase.Host.Documents.Tests;

/// <summary>One host and one MongoDB server for every test of the suite.</summary>
[CollectionDefinition(Name)]
public sealed class DocumentsHostCollection : ICollectionFixture<DocumentsHostFixture>
{
    public const string Name = "Showcase host on the documents engine";
}
