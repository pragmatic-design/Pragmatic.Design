using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Messaging.Diagnostics;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     Host-level completeness check for the domain-event graph. A domain event that is <b>raised</b>
///     (declared via <c>[Raises&lt;T&gt;]</c> on an operation/transition/entity-lifecycle member) but has
///     <b>no handler</b> anywhere in the composed solution is "dangling" — dispatched into the void. This is
///     the framework promotion of the generator's <c>EventGraphChecker</c>: it runs once at the host, where
///     every module assembly is referenced, so it sees the union of raise-sites and handlers across boundaries
///     (a per-assembly SG cannot — handler in module A, event in module B). Reports <c>PRAG0816</c>.
/// </summary>
/// <remarks>
///     Cycle detection (event → handler → operation → event) requires handler-body analysis available only in
///     the owning module's compilation; it is handled by a separate per-module analyzer, not here.
/// </remarks>
internal static class EventGraphValidator
{
    private const string RaisesAttributeName = "RaisesAttribute";
    private const string RaisesAttributeNamespace = "Pragmatic.Authoring";

    /// <summary>One raised event with no handler, and where it is raised from (for the warning).</summary>
    public readonly struct DanglingEvent(string eventDisplayName, string origin, Location location)
    {
        public string EventDisplayName { get; } = eventDisplayName;
        public string Origin { get; } = origin;
        public Location Location { get; } = location;
    }

    public static void Validate(SourceProductionContext context, Compilation compilation)
    {
        foreach (var dangling in ComputeDangling(compilation, context.CancellationToken))
            context.ReportDiagnostic(Diagnostic.Create(
                MessagingDiagnostics.EventWithoutConsumers,
                dangling.Location,
                dangling.EventDisplayName));
    }

    /// <summary>
    ///     Pure graph computation (testable without host-mode plumbing): the set of events declared raised via
    ///     <c>[Raises&lt;T&gt;]</c> across the composed assemblies that no <c>IMessageHandler</c>/<c>IDomainEventHandler</c>
    ///     consumes.
    /// </summary>
    public static IReadOnlyList<DanglingEvent> ComputeDangling(Compilation compilation, CancellationToken cancellationToken)
    {
        // Events with at least one handler anywhere in the composed solution.
        var handled = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        // Raise-sites: event type → (origin description, best source location). First raise-site wins.
        var raised = new Dictionary<INamedTypeSymbol, (string Origin, Location Location)>(SymbolEqualityComparer.Default);

        foreach (var assembly in CandidateAssemblies(compilation))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var type in GetAllNamedTypes(assembly.GlobalNamespace, cancellationToken))
            {
                CollectHandled(type, handled);
                CollectRaised(type, raised);
            }
        }

        if (raised.Count == 0)
            return [];

        var dangling = new List<DanglingEvent>();
        foreach (var entry in raised)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (handled.Contains(entry.Key))
                continue;
            dangling.Add(new DanglingEvent(entry.Key.ToDisplayString(), entry.Value.Origin, entry.Value.Location));
        }

        return dangling;
    }

    /// <summary>Pragmatic module / application assemblies (skip BCL + framework runtime) plus the host itself.</summary>
    private static IEnumerable<IAssemblySymbol> CandidateAssemblies(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;
            if (IsFrameworkAssembly(assembly.Name))
                continue;
            yield return assembly;
        }
    }

    private static void CollectHandled(INamedTypeSymbol type, HashSet<INamedTypeSymbol> handled)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.TypeArguments.Length != 1)
                continue;
            if (!IsHandlerInterface(iface))
                continue;
            if (iface.TypeArguments[0] is INamedTypeSymbol eventType)
                handled.Add(eventType);
        }
    }

    private static bool IsHandlerInterface(INamedTypeSymbol iface) =>
        (iface.Name == "IMessageHandler" && iface.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging") ||
        (iface.Name == "IDomainEventHandler" && iface.ContainingNamespace?.ToDisplayString() == "Pragmatic.Events");

    private static void CollectRaised(
        INamedTypeSymbol type,
        Dictionary<INamedTypeSymbol, (string, Location)> raised)
    {
        // Class-level [Raises<T>] (entity lifecycle events).
        foreach (var attribute in type.GetAttributes())
            AddRaise(attribute, raised, type.Name);

        // Member-level [Raises<T>] (operations / transitions).
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;
            foreach (var attribute in method.GetAttributes())
                AddRaise(attribute, raised, $"{type.Name}.{method.Name}()");
        }
    }

    private static void AddRaise(
        AttributeData attribute,
        Dictionary<INamedTypeSymbol, (string, Location)> raised,
        string origin)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null ||
            attributeClass.Name != RaisesAttributeName ||
            !attributeClass.IsGenericType ||
            attributeClass.TypeArguments.Length != 1 ||
            attributeClass.ContainingNamespace?.ToDisplayString() != RaisesAttributeNamespace)
            return;

        if (attributeClass.TypeArguments[0] is not INamedTypeSymbol eventType)
            return;

        if (!raised.ContainsKey(eventType))
            raised.Add(eventType, (origin, LocationOf(attribute)));
    }

    private static Location LocationOf(AttributeData attribute)
    {
        var reference = attribute.ApplicationSyntaxReference;
        return reference is not null
            ? Location.Create(reference.SyntaxTree, reference.Span)
            : Location.None;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();
            yield return type;
            foreach (var nested in type.GetTypeMembers())
                yield return nested;
        }

        foreach (var nestedNs in ns.GetNamespaceMembers())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var type in GetAllNamedTypes(nestedNs, ct))
                yield return type;
        }
    }

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);
}
