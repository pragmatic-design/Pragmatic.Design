using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Incrementality regression for the Layer-1 capability features (Mapping, Validation, Caching,
///     Configuration, Jobs, Identity). Each test compiles a minimal source that activates one feature,
///     re-runs the generator with an UNRELATED class added, and asserts that feature's tracked steps
///     stayed cached. Marker types are stubbed in source so FeatureDetector triggers without the
///     runtime packages being referenced.
/// </summary>
public class CapabilityIncrementalityTests
{
    // ── Mapping ──

    private const string MappingSource = """
        namespace Pragmatic.Mapping.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MapFromAttribute<TSource> : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MapToAttribute<TTarget> : System.Attribute { }
        }
        namespace MyApp
        {
            public class User { public string Name { get; set; } = ""; }

            [Pragmatic.Mapping.Attributes.MapFrom<User>]
            public partial class UserDto { public string Name { get; set; } = ""; }

            [Pragmatic.Mapping.Attributes.MapTo<User>]
            public partial class UserInput { public string Name { get; set; } = ""; }
        }
        """;

    [Fact]
    public void Mapping_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            MappingSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.MappingMapFrom,
            TrackingNames.MappingMapTo,
            TrackingNames.MappingAllMappings);
    }

    // ── Validation ──

    private const string ValidationSource = """
        namespace Pragmatic.Validation.Attributes
        {
            public abstract class ValidationAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class RequiredAttribute : ValidationAttribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ValidatorAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Property, AllowMultiple = true)]
            public sealed class AsyncValidateAttribute<T> : System.Attribute { }
        }
        namespace MyApp
        {
            public partial class Customer
            {
                [Pragmatic.Validation.Attributes.Required]
                public string Name { get; set; } = "";
            }

            [Pragmatic.Validation.Attributes.Validator]
            public class CustomerValidator { }

            [Pragmatic.Validation.Attributes.AsyncValidate<Customer>]
            public class UniqueCustomerName { }
        }
        """;

    [Fact]
    public void Validation_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            ValidationSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.ValidationValidatables,
            TrackingNames.ValidationValidators,
            TrackingNames.ValidationAsyncBindings);
    }

    // ── Caching ──

    private const string CachingSource = """
        namespace Pragmatic.Caching.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class CacheableAttribute : System.Attribute
            {
                public string Duration { get; set; } = "5m";
            }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class InvalidatesCacheAttribute : System.Attribute { }
        }
        namespace MyApp
        {
            [Pragmatic.Caching.Attributes.Cacheable(Duration = "10m")]
            public partial class GetProductQuery { public System.Guid ProductId { get; set; } }

            [Pragmatic.Caching.Attributes.InvalidatesCache]
            public partial class UpdateProductCommand { public System.Guid ProductId { get; set; } }
        }
        """;

    [Fact]
    public void Caching_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            CachingSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.CachingCacheables,
            TrackingNames.CachingInvalidators);
    }

    // ── Configuration ──

    private const string ConfigurationSource = """
        namespace Pragmatic.Configuration
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ConfigurationAttribute : System.Attribute
            {
                public string? SectionPath { get; set; }
            }
        }
        namespace MyApp
        {
            [Pragmatic.Configuration.Configuration(SectionPath = "Smtp")]
            public partial class SmtpOptions
            {
                public string Host { get; set; } = "";
                public int Port { get; set; }
            }
        }
        """;

    [Fact]
    public void Configuration_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            ConfigurationSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.ConfigurationConfigurations);
    }

    // ── Jobs ──

    private const string JobsSource = """
        namespace Pragmatic.Jobs.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RecurringJobAttribute : System.Attribute
            {
                public RecurringJobAttribute(string cron) { }
                public string? Id { get; set; }
            }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class JobAttribute : System.Attribute { }
        }
        namespace MyApp
        {
            [Pragmatic.Jobs.Attributes.RecurringJob("0 0 * * *")]
            public partial class DailyReportJob { }

            [Pragmatic.Jobs.Attributes.Job]
            public partial class SendWelcomeEmailJob { }
        }
        """;

    [Fact]
    public void Jobs_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            JobsSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.JobsRecurringJobs,
            TrackingNames.JobsOneOffJobs,
            TrackingNames.JobsAllJobs);
    }

    // ── Identity / Authorization ──

    private const string IdentitySource = """
        [assembly: Pragmatic.Authorization.Permission("billing.invoice.refund", "Refund a paid invoice", Category = "Billing")]
        namespace Pragmatic.Authorization
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PermissionAttribute : System.Attribute
            {
                public PermissionAttribute(string value, string description) { }
                public string? Category { get; set; }
            }
            public interface IRole { }
            public static class PragmaticBuilderAuthorizationExtensions { }
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class RequirePermissionAttribute : System.Attribute
            {
                public RequirePermissionAttribute(string permission) { }
                public string? Description { get; set; }
            }
        }
        namespace Sample.Billing
        {
            using Pragmatic.Authorization;
            using System.Collections.Generic;

            public sealed class BillingClerkRole : IRole
            {
                public static string Name => "billing-clerk";
                public static string? Description => "Handles billing";
                public static IReadOnlyList<string> DefaultPermissions => new[] { "billing.invoice.refund" };
            }

            [RequirePermission("billing.invoice.void", Description = "Void an invoice")]
            public sealed class VoidInvoiceAction { }
        }
        """;

    [Fact]
    public void Identity_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            IdentitySource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.IdentityPermissions,
            TrackingNames.IdentityRoles,
            TrackingNames.IdentityCustomPermissions);
    }
}
