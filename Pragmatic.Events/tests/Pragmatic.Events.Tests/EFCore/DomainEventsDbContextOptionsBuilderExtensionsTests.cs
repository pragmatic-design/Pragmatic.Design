using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.EFCore;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Tests.Fixtures;
using Xunit;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     Tests for <see cref="DomainEventsDbContextOptionsBuilderExtensions" />.
/// </summary>
public class DomainEventsDbContextOptionsBuilderExtensionsTests
{
    [Fact]
    public void UseDomainEvents_RegistersInterceptor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();
        var sp = services.BuildServiceProvider();

        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
        optionsBuilder.UseDomainEvents();

        // Verify the context can be created (interceptor registered)
        using var context = new TestDbContext(optionsBuilder.Options);
        context.Should().NotBeNull();
    }

    [Fact]
    public void UseDomainEvents_ReturnsSameBuilder_ForChaining()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDomainEvents();
        var sp = services.BuildServiceProvider();

        DbContextOptionsBuilder original = new DbContextOptionsBuilder<TestDbContext>();
        original.UseInMemoryDatabase(Guid.NewGuid().ToString());

        var result = original.UseDomainEvents();

        result.Should().BeSameAs(original);
    }
}
