using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     Ready when <paramref name="message" /> has been logged <paramref name="times" /> times; failed at once, with the exit code and the last
///     lines of the log, when the container has exited first. A wait on the log alone runs to its timeout on a server that exited, and reports a
///     timeout instead of the server's reason (#456). Testcontainers refreshes the container before each check, so <see cref="IContainer.State" />
///     is current.
/// </summary>
/// <remarks>Linked into every suite that starts MongoDB: one wait, so no fixture is left on the one that hides the reason.</remarks>
public sealed class LoggedUnlessExited(string message, int times = 1) : IWaitUntil
{
    public async Task<bool> UntilAsync(IContainer container)
    {
        var (stdout, stderr) = await container.GetLogsAsync(timestampsEnabled: false);
        if (Count(stdout, message) >= times)
            return true;
        if (container.State != TestcontainersStates.Exited)
            return false;

        var exitCode = await container.GetExitCodeAsync();
        var tail = string.Join(" | ", (stdout + stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(6));
        throw new InvalidOperationException($"The container {container.Image.FullName} exited with code {exitCode} before it was ready. Log: {tail}");
    }

    private static int Count(string log, string message)
    {
        var count = 0;
        for (var at = log.IndexOf(message, StringComparison.Ordinal); at >= 0; at = log.IndexOf(message, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
