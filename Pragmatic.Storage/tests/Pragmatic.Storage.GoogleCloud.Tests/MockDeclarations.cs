using Google.Apis.Download;
using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic;
using Pragmatic.Storage;
using Pragmatic.Storage.GoogleCloud;
using Pragmatic.Storage.GoogleCloud.Tests;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<StorageClient>]
