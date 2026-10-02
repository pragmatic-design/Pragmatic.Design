using Microsoft.CodeAnalysis.Testing;
using Xunit;
using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.SourceGenerator.Analyzers.NotPartialClassAnalyzer,
    Pragmatic.SourceGenerator.CodeFixers.MakeClassPartialCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.SourceGenerator.CodeFixers.Tests;

public class MakeClassPartialCodeFixTests
{
    // Stub attributes matching the Pragmatic namespace patterns the analyzer detects

    private const string ActionStub = @"
namespace Pragmatic.Actions.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class MutationAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class BoundaryAttribute : System.Attribute { }
}
";

    private const string QueryStub = @"
namespace Pragmatic.Persistence.Query.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class QueryAttribute<TEntity> : System.Attribute { }
}
";

    private const string EndpointStub = @"
namespace Pragmatic.Endpoints
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EndpointAttribute : System.Attribute { }
}
";

    private const string PersistenceStub = @"
namespace Pragmatic.Persistence.Entity
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EntityAttribute : System.Attribute { }


}
";

    private const string PersistenceEfCoreStub = @"
namespace Pragmatic.Persistence.EFCore
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class PragmaticDbContextAttribute : System.Attribute
    {
        public PragmaticDbContextAttribute(string boundary) { }
    }
}
";

    private const string MessagingStub = @"
namespace Pragmatic.Messaging
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class MessageHandlerAttribute : System.Attribute { }
}
";

    private const string JobsStub = @"
namespace Pragmatic.Jobs
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class JobAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class RecurringJobAttribute : System.Attribute { }
}
";

    private const string OwnershipStub = @"
namespace Pragmatic.Persistence.Entity
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class HasOwnerAttribute : System.Attribute { }
}
";

    private const string MappingStub = @"
namespace Pragmatic.Mapping.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class MapFromAttribute<TSource> : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class MapToAttribute<TTarget> : System.Attribute { }
}
";

    private const string CachingStub = @"
namespace Pragmatic.Caching.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class CacheableAttribute : System.Attribute { }
}
";

    private const string PatchStub = @"
namespace Pragmatic.Patch.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class GeneratePatchAttribute<TEntity> : System.Attribute { }
}
";

    private const string ConfigurationStub = @"
namespace Pragmatic.Configuration
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class ConfigurationAttribute : System.Attribute { }
}
";

    // Validation has no marker attribute on the type: the properties carry the rules, and every rule
    // derives from Pragmatic.Validation.Attributes.ValidationAttribute.
    private const string ValidationStub = @"
namespace Pragmatic.Validation.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public abstract class ValidationAttribute : System.Attribute { }

    public sealed class RequiredAttribute : ValidationAttribute { }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public sealed class AsyncValidateAttribute<T> : System.Attribute { }
}
";

    // =========================================================================
    // Persistence queries — PRAG0712, not PRAG0400 (an action's ID)
    // =========================================================================

    [Fact]
    public async Task Query_NotPartial_AddPartial()
    {
        var testCode = QueryStub + @"
[Pragmatic.Persistence.Query.Attributes.Query<object>]
public class {|PRAG0712:GetUsers|}
{
}";

        var fixedCode = QueryStub + @"
[Pragmatic.Persistence.Query.Attributes.Query<object>]
public partial class GetUsers
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Actions — PRAG0400
    // =========================================================================

    [Fact]
    public async Task MutationAction_NotPartial_AddPartial()
    {
        var testCode = ActionStub + @"
[Pragmatic.Actions.Attributes.Mutation]
public class {|PRAG0400:CreateUser|}
{
}";

        var fixedCode = ActionStub + @"
[Pragmatic.Actions.Attributes.Mutation]
public partial class CreateUser
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task BoundaryClass_NotPartial_AddPartial()
    {
        var testCode = ActionStub + @"
[Pragmatic.Actions.Attributes.Boundary]
public class {|PRAG0406:Booking|}
{
}";

        var fixedCode = ActionStub + @"
[Pragmatic.Actions.Attributes.Boundary]
public partial class Booking
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Endpoints — PRAG0500
    // =========================================================================

    [Fact]
    public async Task EndpointClass_NotPartial_AddPartial()
    {
        var testCode = EndpointStub + @"
[Pragmatic.Endpoints.Endpoint]
public class {|PRAG0500:GetUsersEndpoint|}
{
}";

        var fixedCode = EndpointStub + @"
[Pragmatic.Endpoints.Endpoint]
public partial class GetUsersEndpoint
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Persistence — PRAG0600, PRAG0602
    // =========================================================================

    [Fact]
    public async Task Entity_NotPartial_AddPartial()
    {
        var testCode = PersistenceStub + @"
[Pragmatic.Persistence.Entity.Entity]
public class {|PRAG0600:Product|}
{
}";

        var fixedCode = PersistenceStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Product
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    /// <remarks>
    ///     The generator stamps <c>[PragmaticDbContext]</c> on the context it emits, and that one is
    ///     partial by construction. The attribute is public though, so a hand-written class can carry
    ///     it — which is the only way to reach PRAG0602 and the reason the rule is still here.
    /// </remarks>
    [Fact]
    public async Task PragmaticDbContext_NotPartial_AddPartial()
    {
        var testCode = PersistenceEfCoreStub + @"
[Pragmatic.Persistence.EFCore.PragmaticDbContext(""Sales"")]
public class {|PRAG0602:AppDbContext|}
{
}";

        var fixedCode = PersistenceEfCoreStub + @"
[Pragmatic.Persistence.EFCore.PragmaticDbContext(""Sales"")]
public partial class AppDbContext
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Messaging — PRAG0801
    // =========================================================================

    [Fact]
    public async Task MessageHandler_NotPartial_AddPartial()
    {
        var testCode = MessagingStub + @"
[Pragmatic.Messaging.MessageHandler]
public class {|PRAG0801:OrderConfirmedHandler|}
{
}";

        var fixedCode = MessagingStub + @"
[Pragmatic.Messaging.MessageHandler]
public partial class OrderConfirmedHandler
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Jobs — PRAG2502
    // =========================================================================

    [Fact]
    public async Task JobClass_NotPartial_AddPartial()
    {
        var testCode = JobsStub + @"
[Pragmatic.Jobs.Job]
public class {|PRAG2502:CleanupJob|}
{
}";

        var fixedCode = JobsStub + @"
[Pragmatic.Jobs.Job]
public partial class CleanupJob
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task RecurringJobClass_NotPartial_AddPartial()
    {
        var testCode = JobsStub + @"
[Pragmatic.Jobs.RecurringJob]
public class {|PRAG2502:DailyReportJob|}
{
}";

        var fixedCode = JobsStub + @"
[Pragmatic.Jobs.RecurringJob]
public partial class DailyReportJob
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Ownership — PRAG1100
    // =========================================================================

    [Fact]
    public async Task HasOwner_NotPartial_AddPartial()
    {
        var testCode = OwnershipStub + @"
[Pragmatic.Persistence.Entity.HasOwner]
public class {|PRAG1100:Reservation|}
{
}";

        var fixedCode = OwnershipStub + @"
[Pragmatic.Persistence.Entity.HasOwner]
public partial class Reservation
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Mapping — PRAG0300
    // =========================================================================

    [Fact]
    public async Task MapFromDto_NotPartial_AddPartial()
    {
        var testCode = MappingStub + @"
public class User { }

[Pragmatic.Mapping.Attributes.MapFrom<User>]
public class {|PRAG0300:UserDto|}
{
}";

        var fixedCode = MappingStub + @"
public class User { }

[Pragmatic.Mapping.Attributes.MapFrom<User>]
public partial class UserDto
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Caching — PRAG1700
    // =========================================================================

    [Fact]
    public async Task CacheableQuery_NotPartial_AddPartial()
    {
        var testCode = CachingStub + @"
[Pragmatic.Caching.Attributes.Cacheable]
public class {|PRAG1700:GetRoomTypes|}
{
}";

        var fixedCode = CachingStub + @"
[Pragmatic.Caching.Attributes.Cacheable]
public partial class GetRoomTypes
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Patch — PRAG2200
    // =========================================================================

    [Fact]
    public async Task GeneratePatch_NotPartial_AddPartial()
    {
        var testCode = PatchStub + @"
public class Booking { }

[Pragmatic.Patch.Attributes.GeneratePatch<Booking>]
public class {|PRAG2200:BookingPatch|}
{
}";

        var fixedCode = PatchStub + @"
public class Booking { }

[Pragmatic.Patch.Attributes.GeneratePatch<Booking>]
public partial class BookingPatch
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Configuration — PRAG2000
    // =========================================================================

    [Fact]
    public async Task ConfigurationClass_NotPartial_AddPartial()
    {
        var testCode = ConfigurationStub + @"
[Pragmatic.Configuration.Configuration]
public class {|PRAG2000:SmtpOptions|}
{
}";

        var fixedCode = ConfigurationStub + @"
[Pragmatic.Configuration.Configuration]
public partial class SmtpOptions
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    // =========================================================================
    // Validation — PRAG0200 (rules live on the properties, not on the type)
    // =========================================================================

    [Fact]
    public async Task TypeWithValidatedProperty_NotPartial_AddPartial()
    {
        var testCode = ValidationStub + @"
public class {|PRAG0200:CreateUserRequest|}
{
    [Pragmatic.Validation.Attributes.Required]
    public string Name { get; set; } = """";
}";

        var fixedCode = ValidationStub + @"
public partial class CreateUserRequest
{
    [Pragmatic.Validation.Attributes.Required]
    public string Name { get; set; } = """";
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task TypeWithNonRuleValidationNamespaceAttribute_NoDiagnostic()
    {
        // [AsyncValidate<T>] shares the namespace but is not a ValidationAttribute — no partial needed.
        var testCode = ValidationStub + @"
public class CreateUserRequest
{
    [Pragmatic.Validation.Attributes.AsyncValidate<string>]
    public string Name { get; set; } = """";
}";

        await CodeFixVerifier.VerifyAnalyzerAsync(testCode);
    }

    // =========================================================================
    // No false positive — already partial
    // =========================================================================

    [Fact]
    public async Task AlreadyPartialClass_NoDiagnostic()
    {
        var testCode = ActionStub + @"
[Pragmatic.Actions.Attributes.Mutation]
public partial class CreateUser
{
}";

        await CodeFixVerifier.VerifyAnalyzerAsync(testCode);
    }

    // =========================================================================
    // No false positive — non-Pragmatic attribute
    // =========================================================================

    [Fact]
    public async Task NonPragmaticAttribute_NoDiagnostic()
    {
        var testCode = @"
namespace MyApp
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class QueryAttribute : System.Attribute { }
}

[MyApp.Query]
public class GetUsers
{
}";

        await CodeFixVerifier.VerifyAnalyzerAsync(testCode);
    }
}
