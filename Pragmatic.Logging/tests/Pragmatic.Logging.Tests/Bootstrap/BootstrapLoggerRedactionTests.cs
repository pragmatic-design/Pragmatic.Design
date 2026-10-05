using Microsoft.Extensions.Logging;
using Pragmatic.Logging.AspNetCore;
using Pragmatic.Redaction;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Bootstrap;

/// <summary>
///     What the bootstrap logger does with a value whose type declared members must not be logged.
/// </summary>
/// <remarks>
///     <para>
///         Against a fresh bootstrap factory, built the way <see cref="BootstrapLogger" /> builds its own,
///         rather than through the static entry point, whose state other tests move to full logging.
///     </para>
///     <para>
///         The bootstrap logger used to have a <c>Log</c> of its own that rendered the message with the
///         caller's formatter, so a redactor on its provider would not have reached the message. It now
///         writes through the provider's own logger, the same path every Pragmatic logger takes.
///     </para>
/// </remarks>
[Collection(ConsoleOutputCollection.Name)]
public class BootstrapLoggerRedactionTests
{
    private sealed record Credentials(string User, string Password);

    private sealed class Map : IRedactionMap
    {
        public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
        {
            members = type == typeof(Credentials)
                ? [new RedactedMember(nameof(Credentials.Password), RedactionReason.NotLogged)]
                : [];
            return members.Count > 0;
        }
    }

    /// <summary>
    ///     The fact the bootstrap logger's remarks rest on: its provider is built with no redactor.
    /// </summary>
    /// <remarks>
    ///     If this goes red, someone gave the bootstrap provider a redactor; the remark on
    ///     <c>BootstrapLogger.CreateBootstrapLoggerFactory</c> that says values are written in clear before
    ///     the transition is then out of date.
    /// </remarks>
    [Fact]
    public void TheBootstrapProvider_HasNoDeclaredRedactor()
    {
        using var factory = BootstrapLogger.CreateBootstrapLoggerFactory();

        factory.Provider.DeclaredRedactor.Should().BeNull();
    }

    /// <summary>
    ///     A redactor on the bootstrap provider reaches the message, because the bootstrap logger takes the
    ///     same path as every other Pragmatic logger.
    /// </summary>
    [Fact]
    public void ARedactorOnTheBootstrapProvider_MasksTheMessage()
    {
        using var factory = BootstrapLogger.CreateBootstrapLoggerFactory();
        factory.Provider.DeclaredRedactor = new DeclaredRedactor([new Map()]);
        var logger = factory.CreateLogger("Bootstrap.Test");

        using var consoleCapture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(consoleCapture);

        try
        {
            logger.LogInformation("Signed in with {Credentials}", new Credentials("jane", "hunter2"));

            var output = consoleCapture.ToString();
            output.Should().NotContain("hunter2");
            output.Should().Contain(PersonalDataPatterns.Mask).And.Contain("jane");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
