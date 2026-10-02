using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Core;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     <see cref="ServiceTypeDetector" /> decides whether a member's type is a DI dependency or user
///     data. Four transforms depend on the answer, so a wrong verdict either drops a user property from
///     a generated DTO or pushes a service into the HTTP body — silently, in both directions.
/// </summary>
public class ServiceTypeDetectorTests
{
    private const string Source = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }

        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ServiceAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ServiceAttribute<TInterface> : System.Attribute where TInterface : class { }
        }

        namespace System.Data.Common
        {
            public abstract class DbConnection { }
        }

        namespace System.Net.Http
        {
            public class HttpClient { }
        }

        namespace Npgsql
        {
            // A concrete third-party service: not Microsoft.*, not abstract, reachable only through
            // its framework base type.
            public sealed class NpgsqlConnection : global::System.Data.Common.DbConnection { }
        }

        namespace MyApp
        {
            public interface IPricingService { }
            public interface IClock { }
            public abstract class ReportBase { }

            public class AppDbContext : global::Microsoft.EntityFrameworkCore.DbContext { }

            // The bug: a domain class whose NAME merely contains "DbContext".
            public sealed class InvoiceDbContextLogger { public string Text { get; set; } = ""; }

            public sealed class Address { public string City { get; set; } = ""; }

            [global::Pragmatic.Composition.Attributes.Service]
            public sealed class PricingService : IPricingService { }

            [global::Pragmatic.Composition.Attributes.Service<IClock>]
            public sealed class SystemClock : IClock { }

            public sealed class Holder
            {
                public IPricingService Pricing { get; set; } = null!;
                public ReportBase Report { get; set; } = null!;
                public AppDbContext Db { get; set; } = null!;
                public InvoiceDbContextLogger Logger { get; set; } = null!;
                public Address ShippingAddress { get; set; } = null!;
                public PricingService ConcretePricing { get; set; } = null!;
                public SystemClock ConcreteClock { get; set; } = null!;
                public global::System.Net.Http.HttpClient Http { get; set; } = null!;
                public global::Npgsql.NpgsqlConnection Connection { get; set; } = null!;
                public string Name { get; set; } = "";
                public int Count { get; set; }
                public object Anything { get; set; } = null!;
                public global::System.Action Callback { get; set; } = null!;
            }
        }
        """;

    private static ServiceTypeClassification Classify(string propertyName)
        => ServiceTypeDetector.Classify(
            SymbolCompilationHelper.GetPropertyType(Source, "MyApp.Holder", propertyName));

    [Theory]
    [InlineData("Pricing")]      // interface
    [InlineData("Report")]       // abstract class
    [InlineData("Db")]           // really derives from Microsoft.EntityFrameworkCore.DbContext
    [InlineData("ConcretePricing")] // [Service]
    [InlineData("ConcreteClock")]   // [Service<T>]
    [InlineData("Http")]         // framework namespace
    [InlineData("Connection")]   // concrete third-party service, framework base type
    public void Classify_ServiceShapes_ReturnsService(string propertyName)
        => Classify(propertyName).Should().Be(ServiceTypeClassification.Service);

    [Theory]
    [InlineData("Name")]
    [InlineData("Count")]
    [InlineData("Anything")]
    [InlineData("Callback")]
    public void Classify_ValueShapes_ReturnsData(string propertyName)
        => Classify(propertyName).Should().Be(ServiceTypeClassification.Data);

    // The defect: the old detector matched ToDisplayString().Contains("DbContext"), so a plain domain
    // class named InvoiceDbContextLogger was promoted to a DI dependency — and disappeared from every
    // DTO built from these properties.
    [Fact]
    public void Classify_ConcreteClassWhoseNameContainsDbContext_IsNotAService()
    {
        Classify("Logger").Should().NotBe(ServiceTypeClassification.Service,
            "a substring in the type name says nothing; only the base chain does");
        ServiceTypeDetector.IsServiceType(
                SymbolCompilationHelper.GetPropertyType(Source, "MyApp.Holder", "Logger"))
            .Should().BeFalse();
    }

    // The other half of the defect: a plain concrete class and a third-party concrete service are
    // structurally identical, so the detector says so instead of picking one silently.
    [Fact]
    public void Classify_PlainConcreteClass_IsAmbiguous()
        => Classify("ShippingAddress").Should().Be(ServiceTypeClassification.Ambiguous);

    [Fact]
    public void IsServiceType_AmbiguousType_FallsBackToData()
        => ServiceTypeDetector.IsServiceType(
                SymbolCompilationHelper.GetPropertyType(Source, "MyApp.Holder", "ShippingAddress"))
            .Should().BeFalse("a DTO property is data unless something says otherwise");
}
