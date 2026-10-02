using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic;
using Pragmatic.Storage;
using Pragmatic.Storage.Sftp;
using Pragmatic.Storage.Sftp.Tests;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using System.IO;
using System.Text.RegularExpressions;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<ISftpClient>]
[assembly: GenerateMock<ISftpClientFactory>]
[assembly: GenerateMock<ISftpFile>]
