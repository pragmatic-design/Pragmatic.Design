using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
///     The generator emits an <c>ITenantOptionsBinder&lt;T&gt;</c> for a <c>[Configuration]</c> class only when it
///     can set every property <c>ConfigurationBinder</c> would, and registers it beside the options (#139).
/// </summary>
/// <remarks>
///     A binder that skipped a property would leave it at its default for every tenant, silently; a class it
///     cannot bind whole keeps the reflective path instead.
/// </remarks>
public class ATenantBinderIsGeneratedOnlyForWhatItCanBindTests : ConfigurationGeneratorTestBase
{
    [Fact]
    public void ScalarProperties_EmitABinderAndRegisterIt()
    {
        var result = RunGeneratorToCompile(Source("""
            public string Currency { get; set; } = "";
            public decimal TaxRate { get; set; }
            public int? Limit { get; set; }
            public Mode Mode { get; set; }
            """));

        HasCompilationErrors(result).Should().BeFalse();
        var binder = GetGeneratedSource(result, "BillingOptions.TenantBinder");
        binder.Should().NotBeNull();
        binder!.Should().Contain("ConfigurationValue.Decimal(").And.Contain("ConfigurationValue.Enum<global::TestApp.Mode>(");
        GetGeneratedSource(result, "BillingOptions.Registration").Should()
            .Contain("ITenantOptionsBinder<global::TestApp.BillingOptions>, global::TestApp.BillingOptionsTenantBinder>");
    }

    [Theory]
    [InlineData("public System.Collections.Generic.List<string> Regions { get; set; } = new();", "a collection")]
    [InlineData("public Nested Inner { get; set; } = new();", "a nested section")]
    [InlineData("public string Currency { get; } = \"EUR\";", "a get-only property")]
    [InlineData("public string Currency { get; init; } = \"EUR\";", "an init-only property")]
    [InlineData("public string Currency { get; private set; } = \"EUR\";", "a non-public setter")]
    [InlineData("public char Separator { get; set; }", "a scalar ConfigurationValue does not convert")]
    public void APropertyItCannotSet_KeepsTheReflectivePath(string property, string because)
    {
        var result = RunGeneratorToCompile(Source("public decimal TaxRate { get; set; }\n" + property));

        HasCompilationErrors(result).Should().BeFalse();
        GetGeneratedSource(result, "BillingOptions.TenantBinder").Should().BeNull(because);
        GetGeneratedSource(result, "BillingOptions.Registration").Should().NotContain("ITenantOptionsBinder");
    }

    [Fact]
    public void ADerivedClass_KeepsTheReflectivePath()
    {
        var result = RunGeneratorToCompile("""
            using Pragmatic.Configuration;

            namespace TestApp;

            public class BaseOptions { public string Region { get; set; } = ""; }

            [Configuration]
            public partial class BillingOptions : BaseOptions
            {
                public decimal TaxRate { get; set; }
            }
            """);

        HasCompilationErrors(result).Should().BeFalse();
        GetGeneratedSource(result, "BillingOptions.TenantBinder").Should().BeNull("the inherited property would be skipped");
    }

    [Fact]
    public void AConfigurationKeyName_IsTheKeyTheBinderReads()
    {
        var result = RunGeneratorToCompile(Source("""
            [Microsoft.Extensions.Configuration.ConfigurationKeyName("tax_rate")]
            public decimal TaxRate { get; set; }
            """));

        HasCompilationErrors(result).Should().BeFalse();
        GetGeneratedSource(result, "BillingOptions.TenantBinder").Should()
            .Contain("value(\"tax_rate\")").And.NotContain("value(\"TaxRate\")");
    }

    private static string Source(string members) => $$"""
        using Pragmatic.Configuration;

        namespace TestApp;

        public enum Mode { Off, On }

        public class Nested { public string Name { get; set; } = ""; }

        [Configuration]
        public partial class BillingOptions
        {
            {{members}}
        }
        """;
}
