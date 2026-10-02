using Pragmatic.Configuration.Discovery;

namespace Pragmatic.Configuration.Management.Tests.Unit;

/// <summary>A catalogue declaring the <c>App</c> section these tests write to: <c>App:Name</c>.</summary>
internal static class TestCatalog
{
    public static ConfigurationCatalog App()
    {
        var catalog = new ConfigurationCatalog();
        catalog.Contribute([
            new ConfigurationSectionDescriptor
            {
                SectionPath = "App",
                TypeName = "Tests.AppOptions",
                Properties = [new ConfigurationPropertyDescriptor { Name = "Name", TypeName = "string" }],
            },
        ]);
        return catalog;
    }
}
