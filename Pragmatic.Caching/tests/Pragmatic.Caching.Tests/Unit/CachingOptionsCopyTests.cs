using System.Reflection;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Caching.Extensions;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     The builder overload copies <see cref="CachingOptions"/> property by property into the
///     registered options.
/// </summary>
/// <remarks>
///     A hand-written copy is correct until somebody adds a property and does not extend it, and the
///     failure is silent: the option exists, the user sets it, and it never arrives. This repository
///     has already fixed that exact shape once — <c>ProviderConfiguration.Clone()</c> "was silently
///     incomplete". Rather than trusting the copy to stay complete, this walks every public
///     read-write property, sets it to something other than its default, and checks it survived.
/// </remarks>
public sealed class CachingOptionsCopyTests
{
    [Fact]
    public void EveryOption_SurvivesTheBuilderOverload()
    {
        var properties = typeof(CachingOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();

        properties.Should().NotBeEmpty("otherwise this test asserts nothing at all");

        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(cache => cache.WithDefaultOptions(o =>
        {
            foreach (var property in properties)
                property.SetValue(o, NonDefaultFor(property));
        }));

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<CachingOptions>>().Value;

        foreach (var property in properties)
        {
            property.GetValue(resolved).Should().Be(NonDefaultFor(property),
                $"'{property.Name}' is set through the builder and must reach the registered options — "
                + "if this fails, the copy in AddPragmaticCaching(Action<CachingBuilder>) is missing it");
        }
    }

    private static object NonDefaultFor(PropertyInfo property) => property.PropertyType switch
    {
        var t when t == typeof(bool) => false,          // every flag defaults to true
        var t when t == typeof(TimeSpan) => TimeSpan.FromMinutes(37),
        var t when t == typeof(string) => "copied",
        var t => throw new NotSupportedException(
            $"CachingOptions.{property.Name} is a {t.Name}; add a non-default value for it here so the "
            + "copy stays covered.")
    };
}
