using System.Collections.Generic;
using System.Globalization;
﻿// Pragmatic.SourceGenerator - Composition - Diagnostics

using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Composition.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Composition source generator.
///     Range: PRAG1600-1699
/// </summary>
internal static partial class CompositionDiagnostics
{
    private const string Category = "Pragmatic.Composition";

    // ===== Topology Errors (1600-1609) =====

    // NOTE: PRAG1600 (ModuleRequiresMiddleware) was declared here but never reported — there is no
    // middleware-availability check anywhere in the topology validators. Removed: do not reuse the ID.

    public static readonly DiagnosticDescriptor ModuleDependencyNotDeclared = new(
        "PRAG1601",
        "Module dependency not declared",
        "Module '{0}' depends on '{1}' which is not declared",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CircularDependency = new(
        "PRAG1602",
        "Circular dependency detected",
        "Circular dependency detected: {0}",
        Category,
        DiagnosticSeverity.Error,
        true);

    // ===== Reference Warnings (1605-1609) =====

    // NOTE: PRAG1605 ([Service] without Composition reference) and PRAG1606 ([Decorator] requires
    // Composition) were declared here but never reported: when Pragmatic.Composition is not referenced
    // the generator just skips metadata emission (HasComposition == false) without saying anything.
    // Removed. Warning on [Service]/[Decorator] (both live in Pragmatic.Abstractions, so they compile
    // without Composition) is a genuine gap — re-add live descriptors together with the reporting code.

    /// <summary>PRAG1607: two DB-bound modules derive the same simple name — 2-arity binding is ambiguous.</summary>
    // The message intentionally spells out the [Include] arity forms; kept out of the XML doc above.
    public static readonly DiagnosticDescriptor DuplicateModuleName = new(
        "PRAG1607",
        "Duplicate module name",
        "Two database-bound modules resolve to the same name '{0}'. The [Include<TModule, TDb>] (2-arity) database binding is ambiguous — rename one boundary or use the explicit [Include<TModule, TDb, TDbContext>] (3-arity) form.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>PRAG1608: a 2-arity Include's module was not discovered — its DbContext won't register.</summary>
    public static readonly DiagnosticDescriptor IncludeModuleNotDiscovered = new(
        "PRAG1608",
        "Included module not discovered",
        "[Include<{0}, ...>] with a database found no discovered module metadata for '{0}', so its DbContext is NOT registered (it will fail at runtime). Ensure Pragmatic.Persistence.EFCore's source generator runs in that module, or use the explicit [Include<TModule, TDb, TDbContext>] (3-arity) form.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>PRAG1609: a relational database has no ConfigKey — the generated DbContext gets no connection string.</summary>
    public static readonly DiagnosticDescriptor DatabaseConfigKeyMissing = new(
        "PRAG1609",
        "Database connection config key missing",
        "Database '{0}' uses the relational provider '{1}' but no ConfigKey is set, so the generated DbContext has no connection string and will fail at startup. Set [PragmaticDatabase(ConfigKey = \"...\")] on the database type.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // ===== Schema Version (1610-1619) =====

    public static readonly DiagnosticDescriptor IncompatibleSchemaVersion = new(
        "PRAG1610",
        "Incompatible metadata schema version",
        "Incompatible metadata schema version {0} in {1}. Expected major version {2}.",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NewerSchemaVersion = new(
        "PRAG1611",
        "Newer metadata schema version",
        "Metadata schema version {0} in {1} is newer than supported. Consider updating Pragmatic.Composition.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor LegacySchemaVersion = new(
        "PRAG1612",
        "Legacy metadata schema version",
        "Legacy metadata schema version {0} in {1}. Using backward compatibility mode.",
        Category,
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     PRAG1613: a referenced module carries a manifest that is not valid JSON.
    /// </summary>
    /// <remarks>
    ///     Warning, not error: the aggregated manifest feeds tooling and the OpenAPI document, so a
    ///     module that cannot be read is dropped from it while the host itself is composed normally.
    ///     Failing the build here would let a purely informational artifact take down the whole host.
    /// </remarks>
    public static readonly DiagnosticDescriptor ModuleManifestUnreadable = new(
        "PRAG1613",
        "Module manifest is not valid JSON",
        "The manifest embedded in '{0}' is not valid JSON and was left out of the aggregated manifest. "
        + "Rebuild that module with the current generator; the host is otherwise unaffected.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1614: a <c>[Service&lt;T&gt;]</c> whose type argument names a type no generator has
    ///     written yet.
    /// </summary>
    /// <remarks>
    ///     The registration is emitted with the outer type fully qualified and the argument exactly as
    ///     the author typed it — <c>IActionFilter&lt;UploadInvoiceAttachmentAction&gt;</c> — because
    ///     <c>ToDisplayString(FullyQualifiedFormat)</c> on an error symbol gives back the written name.
    ///     It then binds only if the generated registration file happens to carry a matching using, and
    ///     otherwise fails to compile inside code the author cannot edit. A consumer hit this
    ///     registering a filter on an action the attachments trait writes, and fell back to a
    ///     hand-written AddScoped.
    ///     <para>
    ///         Warning with a concrete instruction rather than a guessed namespace: the generator
    ///         cannot know where the type will land, and inventing one would trade a visible failure
    ///         for a wrong binding.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ServiceTypeArgumentNotYetGenerated = new(
        "PRAG1614",
        "Service type argument is not resolvable yet",
        "'{0}' names '{1}', which no generator has written at this point, so the registration is "
        + "emitted with an unqualified name. Write it fully qualified — including its namespace — "
        + "or register the service by hand in a startup step.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // ===== Injection Validation (1620-1629) =====

    // NOTE: PRAG1620 ([Inject] references unregistered service) and PRAG1621 ([Inject] creates circular
    // reference) are retired and not reused. DependencyValidator validates property and method
    // injections through the generic PRAG1641 (dependency not registered) and PRAG1602 (circular
    // dependency), so no [Inject]-specific ID has a reporting site.

    // ===== Startup (1630-1639) =====

    public static readonly DiagnosticDescriptor StartupMustImplementInterface = new(
        "PRAG1630",
        "[StartupStep] must implement IStartupStep",
        "Class '{0}' has [StartupStep] attribute but does not implement IStartupStep",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor StartupMustBeClass = new(
        "PRAG1631",
        "[StartupStep] must be on a class",
        "[StartupStep] attribute can only be applied to classes, not '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1632: [NeedsStep&lt;T&gt;] references a step type that is not available in the compilation.
    /// </summary>
    public static readonly DiagnosticDescriptor NeedsStepTypeNotFound = new(
        "PRAG1632",
        "[NeedsStep<T>] references unavailable step type",
        "Module '{0}' declares [NeedsStep<{1}>] but the type is not available. Add the required package reference.",
        Category,
        DiagnosticSeverity.Error,
        true);

    // ===== Service Registration (1640-1659) =====

    /// <summary>
    ///     PRAG1640: [Service] requires a class type.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceRequiresClass = new(
        "PRAG1640",
        "Service attribute requires class",
        "[Service] can only be applied to class types, not '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1641: Dependency is not registered.
    /// </summary>
    public static readonly DiagnosticDescriptor DependencyNotRegistered = new(
        "PRAG1641",
        "Dependency not registered",
        "Service '{0}' depends on '{1}' which is not registered",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1642: Singleton depends on scoped service.
    /// </summary>
    public static readonly DiagnosticDescriptor LifetimeMismatch = new(
        "PRAG1642",
        "Lifetime mismatch",
        "Singleton '{0}' depends on {1} service '{2}'. This may cause captive dependency issues.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1643: No interface found for service.
    /// </summary>
    public static readonly DiagnosticDescriptor NoInterfaceFound = new(
        "PRAG1643",
        "No interface found",
        "Service '{0}' has no interfaces. Consider using AsSelf=true or implementing an interface.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // NOTE: PRAG1644 (ServiceRegistered, info) was declared here but never reported — a per-registration
    // trace note that nothing emits (and that would be pure noise on a real solution). Removed: do not
    // reuse the ID for anything else.

    /// <summary>
    ///     PRAG1645: Abstract class cannot be a service.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractClassCannotBeService = new(
        "PRAG1645",
        "Abstract class cannot be service",
        "[Service] cannot be applied to abstract class '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1646: Keyed services require .NET 8+.
    /// </summary>
    public static readonly DiagnosticDescriptor KeyedServicesRequireNet8 = new(
        "PRAG1646",
        "Keyed services require .NET 8+",
        "Service '{0}' uses keyed services (Key='{1}') which require .NET 8 or later",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1647: [Inject] members on an open-generic service are ignored (unsupported).
    /// </summary>
    public static readonly DiagnosticDescriptor InjectOnOpenGenericUnsupported = new(
        "PRAG1647",
        "[Inject] is not supported on an open-generic service",
        "Service '{0}' is an open generic; its [Inject] property/method members are ignored — DI resolves open generics by constructor injection only. Use constructor parameters instead.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // ===== Decorator (1660-1669) =====

    /// <summary>
    ///     PRAG1660: Decorator must implement decorated interface.
    /// </summary>
    public static readonly DiagnosticDescriptor DecoratorMustImplementInterface = new(
        "PRAG1660",
        "Decorator must implement interface",
        "[Decorator] '{0}' must implement at least one interface to decorate",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1661: Decorator missing inner service parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor DecoratorMissingInnerService = new(
        "PRAG1661",
        "Decorator missing inner service",
        "[Decorator] '{0}' must have a constructor parameter of the decorated interface type",
        Category,
        DiagnosticSeverity.Error,
        true);

    // ===== Event Handler (1670-1679) =====

    /// <summary>
    ///     PRAG1670: [EventHandler] on class that doesn't implement IDomainEventHandler&lt;T&gt;.
    /// </summary>
    public static readonly DiagnosticDescriptor EventHandlerMissingInterface = new(
        "PRAG1670",
        "EventHandler missing IDomainEventHandler<T>",
        "[EventHandler] on '{0}' but class does not implement IDomainEventHandler<TEvent>",
        Category,
        DiagnosticSeverity.Error,
        true);

    // ===== Database Topology (1650-1659) =====

    /// <summary>
    ///     PRAG1651: A boundary in the module tree has no database configured.
    ///     Use [Include&lt;TModule, TDatabase&gt;] or [Include&lt;TModule, TDatabase, TDbContext&gt;] to assign a database.
    /// </summary>
    public static readonly DiagnosticDescriptor BoundaryWithoutDatabase = new(
        "PRAG1651",
        "Boundary has no database configured",
        "Boundary '{0}' (from module '{1}') is included without a database assignment. " +
        "Use [Include<TModule, TDatabase>] to assign a database.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1652: Two different databases map to the same DbContext class name.
    ///     This would cause DbContext collision. Use distinct DbContext names.
    /// </summary>
    public static readonly DiagnosticDescriptor DbContextNameCollision = new(
        "PRAG1652",
        "DbContext name collision across different databases",
        "DbContext name '{0}' is used for both database '{1}' and '{2}'. " +
        "Use [Include<TModule, TDatabase, TDbContext>] with distinct DbContext types.",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1680: [ExposeEndpoint&lt;T&gt;] references an action not imported via [UsePackage].
    ///     The action should come from a package declared on the same module.
    /// </summary>
    public static readonly DiagnosticDescriptor ExposeEndpointNotFromPackage = new(
        "PRAG1680",
        "ExposeEndpoint references a non-package action",
        "[ExposeEndpoint<{0}>] on module '{1}' references an action not imported via [UsePackage]. " +
        "Use [Endpoint] directly on your own actions instead.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1681: an exposed endpoint on a bodyless verb whose action takes an input nothing can
    ///     bind from the query string.
    /// </summary>
    /// <remarks>
    ///     A GET carries its inputs on the query string, and a query value is text: a scalar, an enum,
    ///     anything <c>IParsable</c>. A nested object is not one of those, and the handler would have
    ///     to invent a shape for it. ⚠️ The handler binds a GET from the route and the query string,
    ///     not from a request body: reading the body whatever the verb would answer 415 and bind nothing.
    /// </remarks>
    public static readonly DiagnosticDescriptor ExposedEndpointInputCannotBeBound = new(
        "PRAG1681",
        "An exposed endpoint's input cannot come from the query string",
        "[ExposeEndpoint<{0}>] on module '{1}' is a {2}, which carries no body, and '{3}' is of type "
        + "'{4}' — a query value is text, so only a scalar, an enum or an IParsable can come from one. "
        + "Expose it on a verb that carries a body, or give the action a scalar input.",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1682: the group named by <c>[ExposeEndpoint&lt;TAction, TGroup&gt;]</c> is not an endpoint
    ///     group the host can map.
    /// </summary>
    /// <remarks>
    ///     The route belongs inside the group — its prefix and its <c>ConfigureGroup</c> options. A type
    ///     that is not an <c>[EndpointGroup("…")]</c>, or one the host cannot resolve, gives it nowhere to
    ///     go, and mapping it on the root instead would be exactly a silent fallback.
    /// </remarks>
    public static readonly DiagnosticDescriptor ExposedEndpointGroupNotFound = new(
        "PRAG1682",
        "An exposed endpoint's group cannot be mapped",
        "[ExposeEndpoint<{0}, {1}>] on module '{2}': '{1}' {3}, so the route has no group to go in. "
        + "Declare it with [EndpointGroup(\"prefix\")], or expose the action without a group.",
        Category,
        DiagnosticSeverity.Error,
        true);

    // ===== Remote Boundary (1685-1689) =====

    /// <summary>
    ///     PRAG1685: [RemoteBoundary&lt;T&gt;] and [Include&lt;T&gt;] on the same module — contradictory.
    /// </summary>
    public static readonly DiagnosticDescriptor RemoteBoundaryOverlapsInclude = new(
        "PRAG1685",
        "RemoteBoundary overlaps with Include",
        "Module '{0}' is declared both as [RemoteBoundary] and [Include]. Remove one — a boundary cannot be both local and remote.",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1686: [RemoteBoundary&lt;T&gt;] module has no discovered actions.
    /// </summary>
    public static readonly DiagnosticDescriptor RemoteBoundaryNoActions = new(
        "PRAG1686",
        "RemoteBoundary module has no actions",
        "[RemoteBoundary<{0}>] declared but module has no discovered actions to invoke remotely.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1687: [RemoteBoundary&lt;T&gt;] base URL not configured (warning).
    /// </summary>
    public static readonly DiagnosticDescriptor RemoteBoundaryNoBaseUrl = new(
        "PRAG1687",
        "RemoteBoundary base URL not configured",
        "[RemoteBoundary<{0}>] has no BaseUrl attribute. Ensure 'Pragmatic:RemoteBoundaries:{0}:BaseUrl' is configured at runtime.",
        Category,
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     PRAG1689: an action of a remote module declares <c>[UndoWith&lt;T&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The compensator runs in the invoker that owns the action. Behind a remote boundary the
    ///         caller holds an HTTP proxy instead, so a failure downstream of the call leaves the
    ///         attribute declared and never executed — the shape that reads as protection and is not.
    ///     </para>
    ///     <para>
    ///         Only the host can see this: the module compiles without knowing it will be deployed
    ///         apart. Undoing across a process boundary is a saga — durable, retried, observable —
    ///         and the framework has one; an attribute is not a substitute for it.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor RemoteBoundaryCompensationUnreachable = new(
        "PRAG1689",
        "Compensation cannot run across a remote boundary",
        "'{0}' is remote in this host, and '{1}' declares a compensator that will never run here: it executes in the process that owns the action, not in the caller. Publish an event or model the undo as a saga.",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>
    ///     PRAG1688: Required configuration key missing from appsettings.json.
    /// </summary>
    public static readonly DiagnosticDescriptor ConfigKeyMissing = new(
        "PRAG1688",
        "Required configuration key missing from appsettings.json",
        "Configuration key '{0}' required by {1} is not present in {2}. Add it to avoid runtime errors.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // ===== Module Dependency Validation (1695-1698) =====

    /// <summary>PRAG1697: two operations published at the same address.</summary>
    /// <remarks>
    ///     <para>
    ///         The check the host can make and a single compilation cannot: operations live in
    ///         different libraries, and only here are they all visible at once.
    ///     </para>
    ///     <para>
    ///         ⚠️ It is deliberately about the <b>route</b>, not the payload. A check on entity, mode
    ///         and shape was built and withdrawn: <c>Confirm</c> and <c>MarkPaymentReceived</c> take
    ///         only an <c>Id</c>, are both updates, answer the same, and are two different commands —
    ///         the payload is the identity of the row and the verb is the name. Two operations at the
    ///         same address are something else: a caller cannot choose between them, and the second
    ///         registration wins by an order nobody wrote down.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DuplicateEndpointRoute = new(
        "PRAG1697",
        "Two operations are published at the same address",
        "'{0}' and '{1}' are both published as {2} {3}. A caller cannot choose between them, and which "
        + "one answers depends on registration order. Give one of the two a route of its own.",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1695: a host references Authorization and not Identity, and does not declare
    ///     <c>[AnonymousHost]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reference is transitive (<c>Pragmatic.Actions</c> depends on
    ///         <c>Pragmatic.Authorization</c>), so any host reaches this whether or not it ever
    ///         authorizes anything; libraries do not, because the validator only runs in Host mode.
    ///     </para>
    ///     <para>
    ///         An error, because the host it describes does not work: the generated endpoint root
    ///         requires authorization by default and nothing in the host can authenticate a request.
    ///         Measured on a host built from the packages: every request answers 500, because
    ///         <c>EndpointMiddleware</c> refuses an endpoint with authorization metadata when no
    ///         authorization middleware is in the pipeline.
    ///         Both legitimate shapes are compile-time facts — Identity is referenced, or the host
    ///         declares <c>[AnonymousHost]</c> — so the check can tell them apart. The runtime option
    ///         <c>RequireAuthorizationByDefault = false</c> is invisible to it, which is why the remedy
    ///         is a declaration and not that option — the option alone would still need <c>NoWarn</c>.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor AuthorizationWithoutIdentity = new(
        "PRAG1695",
        "Authorization referenced without Identity",
        "Pragmatic.Authorization is referenced (Pragmatic.Actions depends on it) but Pragmatic.Identity is not, so "
            + "nothing can authenticate a request, and every endpoint requires authorization by default. "
            + "Add Pragmatic.Identity.AspNetCore, or, for a host that deliberately has no authentication, put "
            + "[AnonymousHost] on the host's [Module] class.",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG1692: this host declares it has no authentication, and one of the routes it discovered
    ///     enforces a permission its author never declared and cannot remove.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[Autocomplete]</c> derives a read permission from the boundary and the entity: the
    ///         author never asked for it, and the attribute has no knob to relax it. Under
    ///         <c>[AnonymousHost]</c> no caller can hold a permission, so that route answers 403 for the
    ///         life of the application — a route generated into an application that cannot use it.
    ///     </para>
    ///     <para>
    ///         ⚠️ The question is asked of the <b>host</b>, not of the module. A boundary library cannot
    ///         know whether the host authenticates — the module declares, the host composes — and the
    ///         ordinary host that does not is already <see cref="AuthorizationWithoutIdentity" />, an
    ///         error. What is left is this case, and it belongs here. PRAG0519 is not assigned and not
    ///         to be reused.
    ///     </para>
    ///     <para>
    ///         A warning and not an error: the application is coherent — an anonymous host is a declared
    ///         shape — and the remedy is a product decision, either dropping <c>[Autocomplete]</c> or
    ///         giving the host an authentication method. Nothing the generator can choose.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor AnonymousHostCannotHoldADerivedPermission = new(
        "PRAG1692",
        "An anonymous host has a route nobody can call",
        "This host declares [AnonymousHost], and the route '{0}' enforces the derived permission '{1}'. "
            + "No caller can hold a permission here, so that route answers 403 to everyone. The "
            + "permission is derived from the boundary and the entity rather than declared, so there is "
            + "no way to relax it: drop [Autocomplete] on that property, or give the host an "
            + "authentication method.",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "A generated route that no caller of this host can ever reach.");

    /// <summary>
    ///     PRAG1698: something in this application takes a value from the clock, and this host cannot
    ///     supply an <c>IClock</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same shape as PRAG1695, one floor down: a capability the application declares and the
    ///         host is the only one able to wire. <c>[FromClock]</c> and <c>IClock</c> live in
    ///         <c>Pragmatic.Abstractions</c>, so any module can declare a binding; <c>SystemClock</c> and
    ///         <c>AddPragmaticTemporal()</c> live in <c>Pragmatic.Temporal</c>, which is <b>not</b> in a
    ///         host's default package set. The generated invoker resolves the clock with
    ///         <c>GetRequiredService</c>, per request.
    ///     </para>
    ///     <para>
    ///         Measured in the Invoicing example: the route that issues an invoice answered
    ///         <b>500</b> — "No service for type 'Pragmatic.Temporal.Clock.IClock' has been registered" —
    ///         on the one operation that needed a date, with nothing at compile time pointing at the
    ///         cause. A warning rather than an error: a host can register a clock of its own, and this
    ///         check cannot see a hand-written <c>services.AddSingleton&lt;IClock&gt;</c>.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ClockBindingWithoutAClock = new(
        "PRAG1698",
        "An operation takes the clock and this host registers none",
        "An operation in this application declares [FromClock], and its generated invoker resolves IClock "
            + "per request — Pragmatic.Temporal is not referenced here, so nothing registers one and that "
            + "route answers 500. Reference Pragmatic.Temporal (the generated host then registers the "
            + "system clock for you), or register an IClock of your own.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     PRAG1699: a referenced assembly declares a message-handler registration, and this host does
    ///     not call it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The host composes the assemblies it <b>discovered</b>, and discovery is by module: an
    ///         assembly with no <c>[Module]</c> is filtered out with everything it declares. A contracts
    ///         project is deliberately not a module — it is what a consumer references, and it must not
    ///         drag the module in — so its generated
    ///         <c>PragmaticMessageHandlerRegistration.AddPragmaticMessageHandlers</c> sits in the
    ///         assembly and nobody calls it.
    ///     </para>
    ///     <para>
    ///         ⚠️ Measured on Casework: the registration carried the message type registry the
    ///         outbox pump resolves types through, and with it uncalled <b>six of the suite's tests
    ///         failed</b> — every cross-service delivery — with nothing at build time pointing anywhere.
    ///         A piece of generated code that exists, compiles, is correct and is connected to nothing
    ///         is the failure this repository has the worst history with, and the first sign of it is a
    ///         message that never arrives.
    ///     </para>
    ///     <para>
    ///         A warning and not an error, and the message says the one line that fixes it: whether
    ///         discovery itself should follow the metadata instead of the module is a product decision
    ///         about every host, deliberately not taken here.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor UncalledMessagingRegistration = new(
        "PRAG1699",
        "A referenced assembly's message handlers are registered by nobody",
        "'{0}' declares a message-handler registration ({1}) and this host does not call it: the host "
            + "composes the assemblies it discovered, and discovery is by module — an assembly with no "
            + "[Module] is skipped with everything it declares. Nothing fails at build time and the "
            + "handlers, the type registry and the transport subscriptions it carries are simply absent "
            + "at run time. Call it once in Program.cs — global::{1}(app.Services) — or give that "
            + "assembly a [Module] if it is one.",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "Generated messaging registration that exists, compiles and is connected to nothing.");

    public static readonly DiagnosticDescriptor IdentityPersistenceWithoutAuthorization = new(
        "PRAG1696",
        "Identity.Persistence referenced without Authorization",
        "Pragmatic.Identity.Persistence is referenced but Pragmatic.Authorization is not. Identity persistence requires authorization infrastructure.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    // ===== Info (1690-1699) =====

    public static readonly DiagnosticDescriptor DiscoveredModules = new(
        "PRAG1690",
        "Discovered Pragmatic modules",
        "Discovered {0} Pragmatic modules from referenced assemblies",
        Category,
        DiagnosticSeverity.Info,
        true);

    public static readonly DiagnosticDescriptor DiscoveredModulesInfo = new(
        "PRAG1691",
        "Module composition info",
        "Composing {0} modules: {1}",
        Category,
        DiagnosticSeverity.Info,
        true);

    public static readonly DiagnosticDescriptor NoServicesDiscovered = new(
        "PRAG1693",
        "No Pragmatic services discovered",
        "No [Service] or [Decorator] registrations found in referenced assemblies — ensure library projects reference Pragmatic.Composition and use [Service] attributes",
        Category,
        DiagnosticSeverity.Info,
        true);

    public static readonly DiagnosticDescriptor NoPipelineStepsDiscovered = new(
        "PRAG1694",
        "No startup steps discovered",
        "No [StartupStep] registrations found in referenced assemblies",
        Category,
        DiagnosticSeverity.Info,
        true);

    // ══════════════════════════════════════════════════════════════════════
    // Package Composition (1050-1059) — using Identity range per design doc
    // ══════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor DuplicateUsePackage = new(
        "PRAG1050",
        "Duplicate UsePackage declaration",
        "[UsePackage<{0}>] is declared more than once on module '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     The descriptor a <c>ValidationError</c> is reported through.
    /// </summary>
    public static DiagnosticDescriptor? SchemaDescriptor(string diagnosticId)
        => diagnosticId switch
        {
            "PRAG1610" => IncompatibleSchemaVersion,
            "PRAG1611" => NewerSchemaVersion,
            "PRAG1612" => LegacySchemaVersion,
            _ => null
        };

    /// <summary>
    ///     The sentence a <c>ValidationError</c> reads as, worded by its descriptor.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The compiler diagnostic and the topology report both word it through the descriptor.
    ///         Two copies of the sentence — one in the descriptor, one in
    ///         <c>ValidationError.Message</c> — can disagree: the report saying which version and which
    ///         assembly while the diagnostic says <c>"{0}"</c> and <c>"{1}"</c> with the braces still in
    ///         them, because a finished sentence passed as the first argument of a format that wants two
    ///         fills nothing.
    ///     </para>
    /// </remarks>
    public static string Render(string diagnosticId, IReadOnlyList<string> messageArgs)
    {
        var descriptor = SchemaDescriptor(diagnosticId);
        if (descriptor is null)
            return diagnosticId;

        var format = descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, format, [.. messageArgs]);
    }

    /// <summary>The severity the descriptor declares, rather than a guess from the id.</summary>
    /// <remarks>
    ///     Reading it off the id — <c>EndsWith("10") ? "ERROR" : "WARNING"</c> — would be right for the
    ///     three ids that exist today and wrong for the next one ending in 10.
    /// </remarks>
    public static DiagnosticSeverity SchemaSeverity(string diagnosticId)
        => SchemaDescriptor(diagnosticId)?.DefaultSeverity ?? DiagnosticSeverity.Warning;
}
