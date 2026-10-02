using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic;
using Pragmatic.Storage;
using Pragmatic.Storage.S3;
using Pragmatic.Storage.S3.Tests;
using System.Net;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IAmazonS3>]
