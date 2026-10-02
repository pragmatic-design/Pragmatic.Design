namespace Pragmatic.Email.Middleware;

/// <summary>
///     Chains <see cref="IEmailMiddleware"/> instances by Order, then returns the processed message.
/// </summary>
internal sealed class EmailPipeline
{
    private readonly IReadOnlyList<IEmailMiddleware> _middleware;

    public EmailPipeline(IEnumerable<IEmailMiddleware> middleware)
    {
        _middleware = middleware.OrderBy(m => m.Order).ToList();
    }

    public Task<EmailMessage> ExecuteAsync(EmailMessage message, CancellationToken ct)
    {
        if (_middleware.Count == 0)
            return Task.FromResult(message);

        return ExecuteStep(0, message, ct);
    }

    private Task<EmailMessage> ExecuteStep(int index, EmailMessage message, CancellationToken ct)
    {
        if (index >= _middleware.Count)
            return Task.FromResult(message);

        var current = _middleware[index];
        return current.ProcessAsync(message, next => ExecuteStep(index + 1, next, ct), ct);
    }
}
