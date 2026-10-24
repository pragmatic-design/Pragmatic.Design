namespace Pragmatic.Persistence.Documents.Mongo.Wire;

/// <summary>
///     The result of a <c>…CoreAsync(useAsync: false)</c>: a method that made only synchronous calls has completed before it returned, so its result
///     is read, not waited for. One that has not completed is a defect of the synchronous path (an asynchronous call it should not have made), and
///     is reported as one instead of being blocked on.
/// </summary>
internal static class Synchronous
{
    public static T Result<T>(ValueTask<T> task)
        => task.IsCompleted ? task.GetAwaiter().GetResult() : throw new InvalidOperationException("A synchronous call of the MongoDB driver did not complete synchronously.");

    public static void Result(ValueTask task)
    {
        if (!task.IsCompleted)
            throw new InvalidOperationException("A synchronous call of the MongoDB driver did not complete synchronously.");
        task.GetAwaiter().GetResult();
    }
}
