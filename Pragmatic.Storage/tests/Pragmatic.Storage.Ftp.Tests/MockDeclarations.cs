using FluentFTP;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic;
using Pragmatic.Storage;
using Pragmatic.Storage.Ftp;
using Pragmatic.Storage.Ftp.Tests;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IAsyncFtpClient>]
[assembly: GenerateMock<IAsyncFtpClientFactory>]
