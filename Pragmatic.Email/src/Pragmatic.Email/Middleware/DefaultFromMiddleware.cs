using Microsoft.Extensions.Options;
using Pragmatic.Email.Configuration;

namespace Pragmatic.Email.Middleware;

/// <summary>
///     Applies <see cref="EmailOptions.DefaultFrom"/> to messages that carry no sender address.
/// </summary>
/// <remarks>
///     Runs first (Order = -1000) so that everything downstream — signing in particular — sees the
///     final From value. This middleware is the only reader of <c>DefaultFrom</c>: without it, setting
///     the option would have no effect whatsoever.
/// </remarks>
internal sealed class DefaultFromMiddleware(IOptions<EmailOptions> options) : IEmailMiddleware
{
    public int Order => -1000;

    public Task<EmailMessage> ProcessAsync(
        EmailMessage message,
        Func<EmailMessage, Task<EmailMessage>> next,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(message.From.Address))
            return next(message);

        if (options.Value.DefaultFrom is not { } defaultFrom)
            return next(message);

        return next(message with { From = defaultFrom });
    }
}
