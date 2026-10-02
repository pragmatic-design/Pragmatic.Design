using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Extensions;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class ServiceCollectionDecorateExtensionsTests
{
    private interface IGreeter
    {
        string Greet();
    }

    private sealed class PlainGreeter : IGreeter
    {
        public string Greet() => "hi";
    }

    private sealed class LoudGreeter(IGreeter inner) : IGreeter
    {
        public string Greet() => inner.Greet().ToUpperInvariant();
    }

    [Fact]
    public void Decorate_TypeRegistration_WrapsInner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreeter, PlainGreeter>();

        services.Decorate<IGreeter, LoudGreeter>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGreeter>().Greet().Should().Be("HI");
    }

    [Fact]
    public void Decorate_KeyedRegistration_PreservesKeyAndWraps()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IGreeter, PlainGreeter>("loud");

        services.Decorate<IGreeter, LoudGreeter>();

        using var provider = services.BuildServiceProvider();
        // The decorated service must still resolve BY ITS KEY — replacing the keyed
        // registration with a non-keyed one would break every keyed resolution.
        provider.GetRequiredKeyedService<IGreeter>("loud").Greet().Should().Be("HI");
    }

    [Fact]
    public void Decorate_KeyedInstanceRegistration_UsesKeyedInstance()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IGreeter>("k", new PlainGreeter());

        services.Decorate<IGreeter, LoudGreeter>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<IGreeter>("k").Greet().Should().Be("HI");
    }

    [Fact]
    public void DecorateWithFactory_KeyedRegistration_PreservesKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IGreeter, PlainGreeter>("k");

        services.Decorate<IGreeter>((inner, _) => new LoudGreeter(inner));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<IGreeter>("k").Greet().Should().Be("HI");
    }

    [Fact]
    public void Decorate_NoRegistration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.Decorate<IGreeter, LoudGreeter>();

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    ///     The unkeyed registration is wrapped; the keyed one is left as it was — the shape of a keyed
    ///     service built on the unkeyed one, which would otherwise go through the decorator twice.
    /// </summary>
    [Fact]
    public void DecorateUnkeyed_WrapsTheUnkeyedRegistration_AndLeavesTheKeyedOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreeter, PlainGreeter>();
        services.AddKeyedSingleton<IGreeter, PlainGreeter>("k");

        services.DecorateUnkeyed<IGreeter>((inner, _) => new LoudGreeter(inner));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGreeter>().Greet().Should().Be("HI");
        provider.GetRequiredKeyedService<IGreeter>("k").Greet().Should().Be("hi",
            "the control: Decorate would have wrapped this one too");
    }

    [Fact]
    public void DecorateUnkeyed_WithOnlyAKeyedRegistration_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IGreeter, PlainGreeter>("k");

        var act = () => services.DecorateUnkeyed<IGreeter>((inner, _) => new LoudGreeter(inner));

        act.Should().Throw<InvalidOperationException>(
            "nothing unkeyed to decorate is the same mistake as nothing at all");
    }
}
