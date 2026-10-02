using Pragmatic.Actions.Invoker;
using Pragmatic.Pipeline;
using TimeOff.Leave.Employees.Actions;

namespace TimeOff.Host.Identity;

/// <summary>
///     Creates the first HR administrator from configuration when the application starts, the way an
///     identity server creates its initial admin: every other account is created by HR, so this one has
///     to come from somewhere else.
/// </summary>
/// <remarks>
///     Runs after the schema: the generated entry point migrates between building the host and
///     starting it, and hosted services start with the host. Once an HR administrator exists it does
///     nothing, so the password in configuration stops mattering after the first start — rotate it
///     then, or remove it.
/// </remarks>
public sealed partial class FirstAdministrator(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<FirstAdministrator> logger) : IHostedService
{
    private const string Section = "TimeOff:FirstAdministrator";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = configuration.GetSection(Section);
        var fullName = settings["FullName"];
        var workEmail = settings["WorkEmail"];
        var password = settings["Password"];

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(workEmail) || string.IsNullOrWhiteSpace(password))
        {
            LogNotConfigured(Section);
            return;
        }

        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var services = scope.ServiceProvider;
        var provision = services.GetRequiredService<IDomainActionInvoker<ProvisionFirstAdministratorAction, bool>>();

        // Nobody is calling: the application itself is. That is what an internal call says, and the
        // operation's permission is not asked of a caller that does not exist.
        using (services.GetRequiredService<ICallContext>().EnterInternalCall())
        {
            var result = await provision.InvokeAsync(
                new ProvisionFirstAdministratorAction { FullName = fullName, WorkEmail = workEmail, Password = password },
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
                throw new InvalidOperationException(
                    $"The first HR administrator could not be created from {Section}: {result.Error.Code}");

            if (result.Value)
                LogCreated(workEmail);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the first HR administrator, {WorkEmail}")]
    private partial void LogCreated(string workEmail);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Section} is not configured: no HR administrator will be created, and nobody can sign in until one exists")]
    private partial void LogNotConfigured(string section);
}
