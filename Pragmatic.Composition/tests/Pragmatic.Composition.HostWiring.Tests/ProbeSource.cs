// Pragmatic.Composition.HostWiring.Tests - Probe source
// The declarations compiled by the two host shapes under comparison. Kept as a string because it is
// input to a Roslyn compilation built by the test, not to this project's own build.

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     The framework declarations exercised by <see cref="HostWiringFixture" />: one per feature that
///     contributes something to the generated host wiring.
/// </summary>
/// <remarks>
///     <para>
///         The same text is compiled twice: once into a library that a bare host then references
///         (the control group), and once directly into the host project. Any difference between the
///         two generated hosts is the defect under measurement — a framework type declared in the
///         host project itself is not wired.
///     </para>
///     <para>
///         Two shapes here are workarounds for defects that are NOT the subject of this suite and
///         must not be "fixed" by editing this file:
///         <list type="bullet">
///             <item>
///                 <description>
///                     <c>[assembly: PragmaticGenerateJsonContext]</c> rather than the
///                     <c>PragmaticGenerateJsonContext</c> build property: an in-memory compilation
///                     has no MSBuild properties, and the attribute is the documented equivalent
///                     opt-in (see <c>SerializationFeature.Register</c>).
///                 </description>
///             </item>
///         </list>
///     </para>
/// </remarks>
internal static class ProbeSource
{
    /// <summary>
    ///     Namespace the probe declarations live in. Identical in both compilations, so lines that
    ///     name it need no normalisation.
    /// </summary>
    public const string DomainNamespace = "Probe.Domain";

    /// <summary>
    ///     The declarations. One per feature whose host wiring is under test.
    /// </summary>
    public const string Declarations = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Compensation;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Authorization;
        using Pragmatic.Caching.Attributes;
        using Pragmatic.Comments;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Configuration;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Jobs;
        using Pragmatic.Jobs.Attributes;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Messaging;
        using Pragmatic.Messaging.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Privacy;
        using Pragmatic.Result;
        using Pragmatic.Serialization;
        using Pragmatic.Temporal.Attributes;
        using Pragmatic.Validation;
        using Pragmatic.Validation.Attributes;
        using Pragmatic.Validation.Types;

        [assembly: PragmaticGenerateJsonContext]

        // Identity / Authorization: a permission.
        [assembly: Permission("probe.admin", "Probe admin", Category = "Probe")]

        namespace Probe.Domain;

        // Composition: boundary + module.
        [Boundary]
        public partial class ProbeBoundary;

        [Module(Name = "Probe.Domain", Version = "1.0.0", Description = "Probe module")]
        public sealed class ProbeModule;

        // Compensation: an action that declares its own undo, and the compensator that performs it.
        // The host builds its registration list from the metadata channel and never calls the module's
        // Add*Actions extension, so this is what proves the declaration reaches a running application.
        public sealed record ProbeIngestResult(int Rows);

        public sealed class ProbeUndo : ICompensates<ProbeIngestResult>
        {
            public Task<VoidResult<IError>> Undo(ProbeIngestResult committed, CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        [DomainAction]
        [UndoWith<ProbeUndo>]
        // The resilience declaration, and it is the only one in the probe: a host wires
        // AddPragmaticResilience because somebody asked for a policy, not because the package is on
        // the compilation.
        [global::Pragmatic.Resilience.Attributes.ResiliencePolicy("probe-external")]
        public partial class ProbeIngestAction : DomainAction<ProbeIngestResult>
        {
            public override Task<Result<ProbeIngestResult, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<ProbeIngestResult, IError>.Success(new ProbeIngestResult(1)));
        }

        // The feature-flag declaration. An interface rather than an attribute, which is the one thing
        // that makes this capability's gate different from its five neighbours: a flag is a named
        // thing an application implements, not a marker it puts on something else.
        public sealed class ProbeFlag : global::Pragmatic.FeatureFlags.IFeatureFlag
        {
            public static string Name => "probe.flag";

            public static string? Description => "Declared so a host has a reason to wire feature flags.";
        }

        // FastEnum.
        [FastEnum]
        public enum ProbeStatus
        {
            Draft,
            Active
        }

        // Persistence entity + Traits ([HasComments]) + Privacy ([DataSubject]/[PersonalData]).
        [Entity]
        [HasComments]
        [DataSubject(nameof(Id))]
        public partial class ProbeEntity : IEntity
        {
            [Required]
            [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize, Reason = "probe")]
            public string Name { get; private set; } = "";

            [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null, Reason = "probe")]
            public string Email { get; private set; } = "";

            public ProbeStatus Status { get; private set; }
        }

        // Persistence [Lookup]: a small, global, read-mostly table served from a preloaded cache. Here
        // for the registration it needs — Add{Prefix}LookupCaches() was generated per assembly and the
        // host called it for nobody.
        [Entity]
        [Lookup]
        public partial class ProbeLookup : IEntity
        {
            [Required]
            public string Code { get; private set; } = "";
        }

        // Mapping DTO (also feeds the Serialization contributions).
        [MapFrom<ProbeEntity>]
        [GenerateProjection]
        public partial class ProbeDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
        }

        // Actions: a mutation, exposed as an endpoint.
        [Mutation(Mode = MutationMode.Create)]
        [Endpoint(HttpVerb.Post, "api/probes")]
        public partial class CreateProbeMutation : Mutation<ProbeEntity>
        {
            public required string Name { get; init; }
            public required string Email { get; init; }
        }

        public sealed class ProbeCacheCategory;

        // Caching: a cacheable query.
        [Query<ProbeEntity, ProbeDto>]
        [Cacheable(Duration = "5m", Tags = ["probes"], Category = typeof(ProbeCacheCategory))]
        [Endpoint(HttpVerb.Get, "api/probes/search")]
        public partial class SearchProbesQuery
        {
            [Filter(Operator = FilterOperator.Contains)]
            public string? Name { get; init; }

            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }

        // Endpoints: a pre-processor, which the generated handler resolves from the request services.
        public sealed class ProbePreProcessor : Pragmatic.Endpoints.Processors.IEndpointPreProcessor
        {
            public ValueTask<Pragmatic.Endpoints.Processors.PreProcessorResult> ProcessAsync(
                Pragmatic.Endpoints.Context.IEndpointContext context, CancellationToken ct = default)
                => new(Pragmatic.Endpoints.Processors.PreProcessorResult.Continue());
        }

        // Endpoints, plus a Temporal behaviour on an input property.
        [Endpoint(HttpVerb.Get, "api/probes/ping")]
        [PreProcessor<ProbePreProcessor>]
        public partial class ProbePingEndpoint : Endpoint<string>
        {
            [FromQuery]
            [ToClientTimezone]
            public DateTime? At { get; set; }

            public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<string>.Success("pong"));
        }

        // Jobs.
        [RecurringJob("0 * * * *", Id = "probe-recurring")]
        public sealed partial class ProbeRecurringJob : IJob
        {
            public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
        }

        // Messaging: a message handler.
        public sealed record ProbeMessage(string Text) : Pragmatic.Events.IDomainEvent
        {
            public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
        }

        [MessageHandler]
        public sealed partial class ProbeMessageHandler : IMessageHandler<ProbeMessage>
        {
            public Task HandleAsync(ProbeMessage message, MessageContext context, CancellationToken ct = default)
                => Task.CompletedTask;
        }

        // Configuration.
        [Configuration]
        public partial class ProbeOptions
        {
            [MaxLength(100)]
            public string AppName { get; set; } = "Probe";
        }

        // Validation: an async validator.
        [Validator]
        public class ProbeValidator : IAsyncValidator<CreateProbeMutation>
        {
            public Task<ValidationError> ValidateAsync(CreateProbeMutation request, CancellationToken ct = default)
                => Task.FromResult(ValidationError.Valid);
        }
        """;

    /// <summary>
    ///     A second module, in a library of its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It exists so the suite can express a <b>standalone</b> host: one whose
    ///         <c>[Include]</c>s do not cover every domain module it references. One module cannot
    ///         express it — <c>IsStandaloneHost</c> compares include count against referenced-module
    ///         count — and neither can two modules in one assembly, because the filter that decides
    ///         what a host wires works on <b>assembly</b> names. Hence a second compilation rather than
    ///         a second namespace.
    ///     </para>
    ///     <para>
    ///         Deliberately small: a module, a boundary, and the two workers whose registration is the
    ///         question — a message handler and a recurring job. No entity, so a host that leaves it out
    ///         owes it no database.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     A package the left-out module imports, emitted as an assembly of its own.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It exists for one reason: a boundary extension registers the invokers of the packages its
    ///     module imports, and the branch that collects those importers reached past the
    ///     <c>[Include&lt;T&gt;]</c> filter. So a host called <c>Add{Module}Boundary</c> for a module it
    ///     does not host, whose package's stores it therefore never registered, and failed container
    ///     validation before serving anything. Without an import here the left-out module
    ///     never took that branch and the hole was invisible to this suite.
    /// </remarks>
    public const string PackageDeclarations = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition;
        using Pragmatic.Result;

        namespace Probe.Package;

        public sealed class ProbePackage : IPackageDefinition
        {
            public static string PackageName => "ProbePackage";
            public static string? RoutePrefix => "probe-package";
            public static string? Description => "The package a left-out module imports";
        }

        [DomainAction]
        public partial class PackageThing : DomainAction<Guid>
        {
            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.Empty));
        }
        """;

    public const string AuxDeclarations = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Jobs;
        using Pragmatic.Jobs.Attributes;
        using Pragmatic.Messaging;
        using Pragmatic.Messaging.Attributes;
        using Pragmatic.Result;

        namespace Probe.Aux;

        [Boundary]
        public partial class ProbeAuxBoundary;

        public sealed record ProbeAuxResult(int Rows);

        [DomainAction]
        public partial class ProbeAuxAction : DomainAction<ProbeAuxResult>
        {
            public override Task<Result<ProbeAuxResult, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<ProbeAuxResult, IError>.Success(new ProbeAuxResult(1)));
        }

        // A group no [Endpoint] uses, so the library's endpoint metadata never lists it — the
        // exposed endpoint below is the only thing that names it, and the host has to find it anyway.
        [EndpointGroup("/probe-grouped")]
        public sealed class ProbeGroupedGroup;

        [DomainAction]
        public partial class ProbeAuxGroupedAction : DomainAction<ProbeAuxResult>
        {
            public override Task<Result<ProbeAuxResult, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<ProbeAuxResult, IError>.Success(new ProbeAuxResult(2)));
        }

        // ⚠️ The import is what makes this module take the "assemblies that use packages" branch of
        // the boundary-registration loop — the one that could reach past the [Include<T>] filter.
        // Without it a host that leaves this module out would never call its boundary extension for
        // any other reason, and a hole there would have no witness here.
        [Module(Name = "Probe.Aux", Version = "1.0.0", Description = "The module a standalone host leaves out")]
        [UsePackage<global::Probe.Package.ProbePackage, ProbeAuxBoundary>]
        [ExposeEndpoint<ProbeAuxAction>(HttpVerb.Post, "aux-ingest")]
        [ExposeEndpoint<ProbeAuxGroupedAction, ProbeGroupedGroup>(HttpVerb.Post, "grouped-ingest")]
        public sealed class ProbeAuxModule;

        public sealed record ProbeAuxMessage(string Text) : Pragmatic.Events.IDomainEvent
        {
            public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
        }

        [MessageHandler]
        public sealed partial class ProbeAuxMessageHandler : IMessageHandler<ProbeAuxMessage>
        {
            public Task HandleAsync(ProbeAuxMessage message, MessageContext context, CancellationToken ct = default)
                => Task.CompletedTask;
        }

        [RecurringJob("0 * * * *", Id = "probe-aux-recurring")]
        public sealed partial class ProbeAuxRecurringJob : IJob
        {
            public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
        }
        """;

    /// <summary>
    ///     A host that references both libraries and includes only one of them.
    /// </summary>
    /// <remarks>
    ///     The shape <c>IsStandaloneHost()</c> recognises, and the one every deployment that splits a
    ///     solution into services has. What it must wire is the module it includes, and only that.
    /// </remarks>
    public const string StandaloneHostEntryPoint = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;

        namespace Probe.StandaloneHost;

        [Module]
        [Include<global::Probe.Domain.ProbeModule>]
        public sealed partial class StandaloneHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    /// <summary>
    ///     The host entry point. Identical in both host compilations, and deliberately empty of
    ///     framework declarations — a host is what <c>CompositionDetector.IsHostProject</c> recognises,
    ///     not what it declares.
    /// </summary>
    /// <summary>
    ///     A host that reaches the probe module over HTTP instead of hosting it.
    /// </summary>
    /// <remarks>
    ///     Deployment topology is the host's to declare, which is why nothing in the module can see
    ///     it — and why a diagnostic about compensation crossing the wire can only be raised here.
    /// </remarks>
    public const string RemoteHostEntryPoint = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;

        namespace Probe.RemoteHost;

        [Module]
        [RemoteBoundary<global::Probe.Domain.ProbeModule>(BaseUrl = "https://probe.example")]
        public sealed partial class RemoteHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    public const string HostEntryPoint = """
        using System.Threading.Tasks;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    /// <summary>
    ///     What <c>&lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt;</c> injects under
    ///     <c>Microsoft.NET.Sdk</c>. MSBuild writes this file; a compilation built by hand does not get
    ///     it, and the generated code (which assumes <c>Guid</c>, <c>ICollection&lt;&gt;</c>,
    ///     <c>Func&lt;,&gt;</c> are in scope) then fails to compile for a reason that has nothing to do
    ///     with what is being measured.
    /// </summary>
    public const string LibraryGlobalUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    /// <summary>
    ///     The same, for <c>Microsoft.NET.Sdk.Web</c> — the SDK a Pragmatic host uses.
    /// </summary>
    public const string HostGlobalUsings = LibraryGlobalUsings + """

        global using global::System.Net.Http.Json;
        global using global::Microsoft.AspNetCore.Builder;
        global using global::Microsoft.AspNetCore.Hosting;
        global using global::Microsoft.AspNetCore.Http;
        global using global::Microsoft.AspNetCore.Routing;
        global using global::Microsoft.Extensions.Configuration;
        global using global::Microsoft.Extensions.DependencyInjection;
        global using global::Microsoft.Extensions.Hosting;
        global using global::Microsoft.Extensions.Logging;
        """;
}
