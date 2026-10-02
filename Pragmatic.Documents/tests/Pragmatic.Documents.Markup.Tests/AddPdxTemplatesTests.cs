using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
///     <c>AddPdxTemplates</c> called once per module: the sources accumulate, in the order of the calls.
/// </summary>
public sealed class AddPdxTemplatesTests
{
    [Fact]
    public async Task TwoCalls_AddTwoSources_AndTheFirstIsAskedFirst()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizer>(
            new StringLocalizer(new InMemoryLocalizationProvider(), new I18NOptions()));

        services.AddPdxTemplates(t => t.From(new OneTemplate("receipt.pdxdoc", "<document><page><text>own</text></page></document>")));
        services.AddPdxTemplates(t => t.FromAssemblyOf<AddPdxTemplatesTests>());

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IPdxTemplates>();

        var own = await templates.DocumentAsync("receipt.pdxdoc", "en", new TemplateDataContext());
        var fromTheAssembly = await templates.DocumentAsync("letterhead.pdxdoc", "en", new TemplateDataContext());

        System.Text.Json.JsonSerializer.Serialize(own.Model).Should().Contain("own");
        fromTheAssembly.Model.Should().NotBeNull("the second call's source answers what the first does not have");
    }

    private sealed class OneTemplate(string name, string markup) : IPdxTemplateSource
    {
        public ValueTask<string?> FindAsync(string wanted, CancellationToken ct = default)
            => ValueTask.FromResult(wanted == name ? markup : null);
    }
}
