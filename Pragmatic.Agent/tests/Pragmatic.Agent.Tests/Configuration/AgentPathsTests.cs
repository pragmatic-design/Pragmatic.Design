using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Configuration;
using Xunit;

namespace Pragmatic.Agent.Tests.Configuration;

/// <summary>
///     Covers <see cref="AgentPaths"/> path resolution and the persistent agent-id lifecycle.
///     Tests that depend on the instance/data-dir env vars save and restore them in a
///     <c>finally</c> block so they remain isolated regardless of ambient environment.
/// </summary>
[Collection(EnvironmentSensitiveCollection.Name)]
public class AgentPathsTests
{
    [Fact]
    public void Ctor_NoInstance_DefaultsToDefault()
    {
        var original = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE", null);

            var paths = new AgentPaths();

            paths.InstanceName.Should().Be("default");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE", original);
        }
    }

    [Fact]
    public void Ctor_ExplicitInstance_WinsOverEnvVar()
    {
        var original = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE", "from-env");

            var paths = new AgentPaths(instanceName: "explicit");

            paths.InstanceName.Should().Be("explicit");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE", original);
        }
    }

    [Fact]
    public void Ctor_EnvInstance_UsedWhenNoExplicit()
    {
        var original = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE", "billing");

            var paths = new AgentPaths();

            paths.InstanceName.Should().Be("billing");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_INSTANCE", original);
        }
    }

    [Fact]
    public void Ctor_DataDirOverride_ComposesKvAndAgentIdPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-paths-{Guid.NewGuid():N}");

        var paths = new AgentPaths(instanceName: "default", dataDirOverride: dir);

        paths.DataDirectory.Should().Be(dir);
        paths.KvFilePath.Should().Be(Path.Combine(dir, "kv.json"));
        paths.AgentIdFilePath.Should().Be(Path.Combine(dir, "agent-id"));
    }

    [Fact]
    public void Ctor_SocketPathOverride_IsHonored()
    {
        var paths = new AgentPaths(socketPathOverride: "/custom/agent.sock");

        paths.SocketPath.Should().Be("/custom/agent.sock");
    }

    [Fact]
    public void EnsureDirectories_CreatesDataDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-paths-{Guid.NewGuid():N}");
        try
        {
            var paths = PathsIn(dir);

            paths.EnsureDirectories();

            Directory.Exists(dir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateAgentId_FirstCall_PersistsIdToFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-paths-{Guid.NewGuid():N}");
        try
        {
            var paths = PathsIn(dir);

            var id = paths.GetOrCreateAgentId();

            id.Should().NotBeNullOrWhiteSpace();
            File.Exists(paths.AgentIdFilePath).Should().BeTrue();
            File.ReadAllText(paths.AgentIdFilePath).Trim().Should().Be(id);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateAgentId_SecondCall_ReturnsSameId()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-paths-{Guid.NewGuid():N}");
        try
        {
            var paths = PathsIn(dir);

            var first = paths.GetOrCreateAgentId();
            var second = paths.GetOrCreateAgentId();

            second.Should().Be(first);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateAgentId_ExistingFile_ReadsPersistedId()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "agent-id"), "agent-preexisting-123");
            var paths = PathsIn(dir);

            paths.GetOrCreateAgentId().Should().Be("agent-preexisting-123");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetOrCreateAgentId_ConcurrentCalls_ProduceSingleId()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-paths-{Guid.NewGuid():N}");
        try
        {
            var paths = PathsIn(dir);

            var ids = await Task.WhenAll(Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() => paths.GetOrCreateAgentId())));

            // The static lock + read-back inside the lock means all callers agree on one id.
            ids.Distinct().Should().HaveCount(1);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ToString_IncludesInstanceAndPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-paths-{Guid.NewGuid():N}");
        var paths = new AgentPaths(instanceName: "diag", dataDirOverride: dir);

        var text = paths.ToString();

        text.Should().Contain("diag");
        text.Should().Contain(paths.KvFilePath);
    }

    /// <summary>
    ///     Data and socket both inside <paramref name="dir" />. EnsureDirectories also creates the
    ///     socket's directory, which on a Linux host outside a container defaults to /var/run/pragmatic:
    ///     a non-root CI runner cannot create it, and these tests failed there while passing on Windows
    ///     (a pipe, no directory) and in Docker (/tmp/pragmatic).
    /// </summary>
    private static AgentPaths PathsIn(string dir)
        => new(dataDirOverride: dir, socketPathOverride: Path.Combine(dir, "agent.sock"));
}
