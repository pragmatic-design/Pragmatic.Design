// Pragmatic.Composition.Tests - Assembly scanning subsystem tests.
// Covers IAssemblyScanner, TypeSelector, TypeFilter, LifetimeSelector and RegistrationStrategy.
//
// AssemblyScanner is internal but reachable through the public AddPragmatic scanning entry point;
// these tests drive the public Scan(...) extension that ships on IServiceCollection.

using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Extensions;
using Pragmatic.Composition.Scanning;

namespace Pragmatic.Composition.Tests.Hosting;

// Test fixtures live in the test assembly so they are reliably discoverable by FromAssemblyOf<T>().
public interface IScanService;

public interface IOtherScanService;

public sealed class ScanServiceImpl : IScanService;

public sealed class OtherScanServiceImpl : IOtherScanService;

public sealed class StandaloneScanType;

public interface IWidget;

// Name follows the I{TypeName} convention so AsMatchingInterface (looking for "IWidget") matches.
public sealed class Widget : IWidget, IScanService;

public class AssemblyScanningTests
{
    [Fact]
    public void AddType_AsImplementedInterfaces_RegistersAgainstInterface()
    {
        var services = new ServiceCollection();

        services.Scan(s => s
            .AddType<ScanServiceImpl>()
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Should().Contain(d =>
            d.ServiceType == typeof(IScanService) &&
            d.ImplementationType == typeof(ScanServiceImpl) &&
            d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddType_AsSelf_RegistersConcreteType()
    {
        var services = new ServiceCollection();

        services.Scan(s => s
            .AddType<StandaloneScanType>()
            .AsSelf()
            .WithSingletonLifetime());

        services.Should().Contain(d =>
            d.ServiceType == typeof(StandaloneScanType) &&
            d.ImplementationType == typeof(StandaloneScanType) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddType_As_RegistersAgainstExplicitServiceType()
    {
        var services = new ServiceCollection();

        services.Scan(s => s
            .AddType<ScanServiceImpl>()
            .As<IScanService>()
            .WithTransientLifetime());

        services.Should().Contain(d =>
            d.ServiceType == typeof(IScanService) &&
            d.ImplementationType == typeof(ScanServiceImpl) &&
            d.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void AddTypes_AsImplementedInterfaces_RegistersAll()
    {
        var services = new ServiceCollection();

        services.Scan(s => s
            .AddTypes<ScanServiceImpl, OtherScanServiceImpl>()
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Should().Contain(d => d.ServiceType == typeof(IScanService));
        services.Should().Contain(d => d.ServiceType == typeof(IOtherScanService));
    }

    [Fact]
    public void AddClasses_AssignableTo_RegistersOnlyMatchingTypes()
    {
        var services = new ServiceCollection();

        services.Scan(s => s
            .FromAssemblyOf<AssemblyScanningTests>()
            .AddClasses(f => f.AssignableTo<IScanService>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        // Both ScanServiceImpl and Foo implement IScanService.
        var impls = services.Where(d => d.ServiceType == typeof(IScanService))
            .Select(d => d.ImplementationType)
            .ToList();
        impls.Should().Contain(typeof(ScanServiceImpl));
        impls.Should().Contain(typeof(Widget));
        services.Should().NotContain(d => d.ImplementationType == typeof(OtherScanServiceImpl));
    }

    [Fact]
    public void Strategy_Skip_DoesNotAddWhenAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScanService, ScanServiceImpl>();

        services.Scan(s => s
            .AddType<ScanServiceImpl>()
            .As<IScanService>()
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .WithScopedLifetime());

        services.Count(d => d.ServiceType == typeof(IScanService)).Should().Be(1);
    }

    [Fact]
    public void Strategy_Replace_ReplacesExistingRegistration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScanService, Widget>();

        services.Scan(s => s
            .AddType<ScanServiceImpl>()
            .As<IScanService>()
            .UsingRegistrationStrategy(RegistrationStrategy.Replace)
            .WithScopedLifetime());

        services.Count(d => d.ServiceType == typeof(IScanService)).Should().Be(1);
        services.Single(d => d.ServiceType == typeof(IScanService)).ImplementationType
            .Should().Be(typeof(ScanServiceImpl));
    }

    [Fact]
    public void Strategy_Throw_ThrowsOnDuplicate()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScanService, Widget>();

        var act = () => services.Scan(s => s
            .AddType<ScanServiceImpl>()
            .As<IScanService>()
            .UsingRegistrationStrategy(RegistrationStrategy.Throw)
            .WithScopedLifetime());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate registration*");
    }

    [Fact]
    public void Strategy_Append_AddsAlongsideExisting()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScanService, Widget>();

        services.Scan(s => s
            .AddType<ScanServiceImpl>()
            .As<IScanService>()
            .UsingRegistrationStrategy(RegistrationStrategy.Append)
            .WithScopedLifetime());

        services.Count(d => d.ServiceType == typeof(IScanService)).Should().Be(2);
    }

    [Fact]
    public void AddType_AsMatchingInterface_RegistersConventionMatchedInterface()
    {
        var services = new ServiceCollection();

        // Widget → I{TypeName} = IWidget convention match, so it registers against IWidget
        // (and NOT against IScanService, which is not the convention name).
        services.Scan(s => s
            .AddType<Widget>()
            .AsMatchingInterface()
            .WithScopedLifetime());

        services.Should().Contain(d =>
            d.ServiceType == typeof(IWidget) && d.ImplementationType == typeof(Widget));
        services.Should().NotContain(d => d.ServiceType == typeof(IScanService));
    }
}
