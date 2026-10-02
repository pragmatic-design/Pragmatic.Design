namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     Source fragments shared by the privacy generator tests.
/// </summary>
/// <remarks>
///     Attribute types are stubbed in the source so <c>FeatureDetector</c> triggers without the runtime
///     package, matching how the other feature tests work.
/// </remarks>
internal static class PrivacyTestSources
{
    /// <summary>The attribute and enum surface the privacy feature reads and emits against.</summary>
    public const string Stubs = """
        namespace Pragmatic.Composition.Metadata
        {
            // Only the member the privacy feature emits. The real enum carries every category; stubbing
            // it whole here would just be a second copy to keep in step.
            public enum MetadataCategory { PersonalData = 23 }
        }
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute
            {
                public PragmaticMetadataAttribute(
                    Pragmatic.Composition.Metadata.MetadataCategory category, string version, string payload) { }
            }
        }
        namespace Pragmatic.Privacy
        {
            public enum DataCategory { Identity, Contact, Financial, Location, Behavioural, Special }
            public enum ErasureStrategy { Null, Delete, Anonymize, Pseudonymize, Retain, DestroyKey }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class DataSubjectAttribute : System.Attribute
            {
                public DataSubjectAttribute(string identifierProperty) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class LinksToSubjectAttribute : System.Attribute
            {
                public LinksToSubjectAttribute(string pathProperty) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class NotPersonalDataAttribute : System.Attribute
            {
                public NotPersonalDataAttribute(string reason) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class PersonalDataAttribute : System.Attribute
            {
                public PersonalDataAttribute(DataCategory category) { }
                public ErasureStrategy Erasure { get; set; }
                public string? Reason { get; set; }
                public bool Encrypted { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RecordAccessAttribute : System.Attribute { }
        }
        """;

    /// <summary>
    ///     What makes a type an <em>operation</em>: the action markers and the endpoint attribute.
    /// </summary>
    /// <remarks>
    ///     Shared because two suites need it for unrelated reasons — deriving an action's entities, and
    ///     reporting <c>[RecordAccess]</c> with no trail to write to — and a second copy of a stub block
    ///     drifts from the first the moment either changes.
    /// </remarks>
    public const string OperationStubs = """
        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class DomainActionAttribute : System.Attribute { }
        }
        namespace Pragmatic.Actions.Abstractions
        {
            public abstract class VoidDomainAction { }
        }
        namespace Pragmatic.Actions.Invoker
        {
            public interface IMutationInvoker<TMutation, TEntity> { }
        }
        namespace Pragmatic.Persistence.Repository
        {
            public interface IRepository<TEntity> { }
        }
        namespace Pragmatic.Endpoints
        {
            public enum HttpVerb { Get, Post, Put, Patch, Delete }
        }
        namespace Pragmatic.Endpoints.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EndpointAttribute : System.Attribute
            {
                public EndpointAttribute(global::Pragmatic.Endpoints.HttpVerb method, string route) { }
            }
        }
        """;

    /// <summary>
    ///     What the generated <em>adapters</em> bind against: the data-subject runtime contracts, the EF
    ///     Core marker the privacy feature gates them on, and a boundary attribute.
    /// </summary>
    /// <remarks>
    ///     Members are stubbed only where a generated call site names them. The adapters are asserted on
    ///     as text: a stub <c>DbContext</c> has no <c>Set&lt;T&gt;</c> and no <c>SaveChangesAsync</c>, so
    ///     the compilation they produce cannot be expected to succeed. What the real EF Core does to
    ///     them is the wiring suite's job (<c>Pragmatic.Privacy.Tests</c>), which compiles against the
    ///     actual packages and runs the result.
    /// </remarks>
    public const string AdapterStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BelongsToAttribute<TBoundary> : System.Attribute { }
        }
        namespace Pragmatic.Privacy
        {
            public interface IPersonalDataSource { }
            public interface IErasureStep { }
            public interface IProcessingActivitySource { }
            public interface ISubjectRegistry { }
        }
        """;

    /// <summary>
    ///     <c>[Entity]</c> on its own, without the EF Core marker.
    /// </summary>
    /// <remarks>
    ///     Deliberately not <c>PragmaticDbContextAttribute</c>: every persistence output is gated on
    ///     <c>HasPersistenceEFCore</c>, so stubbing only the entity marker keeps the persistence pipeline
    ///     silent while still telling the privacy feature that this type is an entity — which is what
    ///     decides whether a generated <c>Set{Property}</c> will exist.
    /// </remarks>
    public const string EntityAttributeStub = """
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }
        """;
}
