namespace Pragmatic.SourceGenerator.Analyzers.Tests.Suppressors;

/// <summary>
///     Minimal stand-ins for the attributes the suppressors key on, so the tests need no
///     reference to the real Pragmatic runtime assemblies.
/// </summary>
internal static class PragmaticAttributeStubs
{
    public const string Source = """
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }

        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MutationAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class QueryAttribute : System.Attribute { }
        }
        """;

    public const string Path = "Stubs.cs";
}
