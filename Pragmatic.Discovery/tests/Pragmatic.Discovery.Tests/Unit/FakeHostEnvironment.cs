using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Minimal <see cref="IHostEnvironment"/> test double with a configurable environment name.
/// </summary>
public sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Pragmatic.Discovery.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } =
        new NullFileProvider();
}
