using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic;
using Pragmatic.Storage;
using Pragmatic.Storage.Azure;
using Pragmatic.Storage.Azure.Tests;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<BlobClient>]
[assembly: GenerateMock<BlobContainerClient>]
[assembly: GenerateMock<BlobServiceClient>]
