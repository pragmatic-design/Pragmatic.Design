using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Formatting;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Extensions;

/// <summary>
///     <see cref="IGlobalizationContext" /> is the contract application code is meant to depend on — it
///     lives in <c>Pragmatic.Abstractions</c> and <c>I18NContext</c> implements it — but nothing
///     registered it. No consumer could inject it, and the <see cref="GlobalizationFormatter" />
///     constructor that takes one was unreachable: the factory built the formatter from a
///     <c>CultureInfo</c> instead.
/// </summary>
public class GlobalizationContextRegistrationTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddPragmaticInternationalization();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void TheContractInAbstractions_CanBeInjected()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IGlobalizationContext>().Should().NotBeNull(
            "an interface published in Abstractions that no container can resolve is a contract "
            + "nobody can depend on");
    }

    [Fact]
    public void TheContext_IsScoped()
    {
        using var provider = Build();

        // Resolving from the root would throw with ValidateScopes if it were registered scoped and
        // captured, and would succeed if it were a singleton. It must not be a singleton: the context
        // carries the culture of the current request.
        var act = () => provider.GetRequiredService<IGlobalizationContext>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TheFormatter_IsBuiltFromTheContextRatherThanACultureItWasHandedSeparately()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<IGlobalizationContext>();
        var formatter = scope.ServiceProvider.GetRequiredService<GlobalizationFormatter>();

        formatter.Culture.Should().Be(context.Culture);
    }
}
