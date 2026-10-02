using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Privacy;
using Pragmatic.Logging.Privacy.Audit;
using Xunit;

namespace Pragmatic.Logging.Tests.Privacy;

/// <summary>
/// Repro for the prior High finding "Circular DI dependency: SecretDetector depends on
/// PragmaticAuditService which depends back on SecretDetector". A cycle makes the container throw
/// "A circular dependency was detected" at resolution — this test resolves all three types, proving
/// the back-edge is gone.
/// </summary>
public class PrivacySuiteWiringTests
{
    [Fact]
    public void PrivacyProtectionSuite_ResolvesWithoutCircularDependency()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrivacyProtectionSuite();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var resolve = () =>
        {
            var detector = provider.GetRequiredService<SecretDetector>();
            var audit = provider.GetRequiredService<PragmaticAuditService>();
            var redactor = provider.GetRequiredService<PragmaticDataRedactor>();
            return (detector, audit, redactor);
        };

        resolve.Should().NotThrow("the privacy suite must not form a circular dependency");
        var (detector, audit, redactor) = resolve();
        detector.Should().NotBeNull();
        audit.Should().NotBeNull();
        redactor.Should().NotBeNull();
    }
}
