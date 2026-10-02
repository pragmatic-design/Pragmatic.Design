using System.Diagnostics;
using System.Text.Json;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     Reading until something holds, with a deadline: how a test waits for what another process does.
/// </summary>
internal static class WarehouseWaits
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Reads until <paramref name="done" /> holds, and fails naming the last reading at the deadline —
    ///     <paramref name="within" /> when the time is itself what the test asserts, <see cref="Deadline" /> otherwise.
    /// </summary>
    public static async Task<T> UntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, string what, TimeSpan? within = null)
    {
        var deadline = within ?? Deadline;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var value = await read().ConfigureAwait(false);
            if (done(value))
                return value;

            if (clock.Elapsed > deadline)
                throw new TimeoutException($"Not reached within {deadline}: {what}. Last reading: {Describe(value)}");

            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    private static string Describe<T>(T value) => value switch
    {
        JsonElement json => json.GetRawText(),
        JsonElement[] rows => $"[{string.Join(", ", rows.Select(r => r.GetRawText()))}]",
        System.Collections.IEnumerable items and not string => $"[{string.Join(", ", items.Cast<object>())}]",
        _ => value?.ToString() ?? "null",
    };
}
