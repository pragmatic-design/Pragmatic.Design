using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Detects cascade cycles in the domain-event graph (#6b): <c>event → handler → operation → event</c>. An
///     operation declares the events it raises via <c>[Raises&lt;T&gt;]</c>; a handler consumes an event via
///     <c>IMessageHandler&lt;T&gt;</c>/<c>IDomainEventHandler&lt;T&gt;</c>. When a handler for event E references an
///     operation that raises F, that is an edge E→F; a cycle in this graph risks an unbounded cascade and is
///     reported as <c>PRAG0822</c>.
/// </summary>
/// <remarks>
///     The handler→operation edge is recovered from direct references to the operation type in the handler
///     body (object creation, field/property access, invocation). Calls routed purely through a generated
///     boundary interface (no operation type in the body) are not yet linked — a known limitation.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventCycleAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EventGraphAnalyzerDescriptors.Prag0822);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var operations = new ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var handlerEvents = new ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var references = new ConcurrentBag<(INamedTypeSymbol Container, INamedTypeSymbol Referenced)>();

        context.RegisterSymbolAction(symbolContext =>
        {
            var type = (INamedTypeSymbol)symbolContext.Symbol;

            var raised = RaisedEvents(type);
            if (!raised.IsEmpty)
                operations[type] = raised;

            var handled = HandledEvents(type).ToImmutableArray();
            if (!handled.IsEmpty)
                handlerEvents[type] = handled;
        }, SymbolKind.NamedType);

        // Recover handler→operation references from method-body operations (no GetSemanticModel needed).
        context.RegisterOperationAction(operationContext =>
        {
            var container = operationContext.ContainingSymbol?.ContainingType;
            if (container is null)
                return;

            var referenced = operationContext.Operation switch
            {
                IInvocationOperation invocation => invocation.TargetMethod.ContainingType,
                IObjectCreationOperation creation => creation.Type as INamedTypeSymbol,
                IFieldReferenceOperation field => field.Field.Type as INamedTypeSymbol,
                IPropertyReferenceOperation property => property.Property.Type as INamedTypeSymbol,
                _ => null
            };

            if (referenced is not null)
                references.Add((container, referenced));
        }, OperationKind.Invocation, OperationKind.ObjectCreation, OperationKind.FieldReference, OperationKind.PropertyReference);

        context.RegisterCompilationEndAction(endContext =>
        {
            if (handlerEvents.IsEmpty || operations.IsEmpty)
                return;

            var edges = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
            var eventLocations = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);

            foreach (var (container, referenced) in references)
            {
                if (!handlerEvents.TryGetValue(container, out var handled) ||
                    !operations.TryGetValue(referenced, out var raised))
                    continue;

                foreach (var handledEvent in handled)
                {
                    eventLocations[handledEvent] = handledEvent.Locations.FirstOrDefault() ?? Location.None;
                    foreach (var raisedEvent in raised)
                    {
                        if (!edges.TryGetValue(handledEvent, out var targets))
                            edges[handledEvent] = targets = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                        targets.Add(raisedEvent);
                    }
                }
            }

            var cycle = FindCycle(edges);
            if (cycle is null)
                return;

            var description = string.Join(" → ", cycle.Select(e => e.Name));
            var location = eventLocations.TryGetValue(cycle[0], out var loc) ? loc : Location.None;
            endContext.ReportDiagnostic(Diagnostic.Create(EventGraphAnalyzerDescriptors.Prag0822, location, description));
        });
    }

    private static ImmutableArray<INamedTypeSymbol> RaisedEvents(INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        void Collect(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
                if (attribute.AttributeClass is { Name: "RaisesAttribute", IsGenericType: true } ac &&
                    ac.TypeArguments.Length == 1 &&
                    ac.ContainingNamespace?.ToDisplayString() == "Pragmatic.Authoring" &&
                    ac.TypeArguments[0] is INamedTypeSymbol eventType)
                    builder.Add(eventType);
        }

        Collect(type);
        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
            Collect(member);

        return builder.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol> HandledEvents(INamedTypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.TypeArguments.Length != 1 || iface.TypeArguments[0] is not INamedTypeSymbol eventType)
                continue;

            var isHandler =
                (iface.Name == "IMessageHandler" && iface.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging") ||
                (iface.Name == "IDomainEventHandler" && iface.ContainingNamespace?.ToDisplayString() == "Pragmatic.Events");

            if (isHandler)
                yield return eventType;
        }
    }

    /// <summary>DFS for any cycle in the event→event graph; returns the cycle path (closing node repeated) or null.</summary>
    private static IReadOnlyList<INamedTypeSymbol>? FindCycle(Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> edges)
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var stack = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var path = new List<INamedTypeSymbol>();

        bool Dfs(INamedTypeSymbol node, out IReadOnlyList<INamedTypeSymbol>? cycle)
        {
            cycle = null;
            visited.Add(node);
            stack.Add(node);
            path.Add(node);

            if (edges.TryGetValue(node, out var targets))
                foreach (var next in targets)
                {
                    if (!visited.Contains(next))
                    {
                        if (Dfs(next, out cycle))
                            return true;
                    }
                    else if (stack.Contains(next))
                    {
                        var start = path.IndexOf(next);
                        var result = path.Skip(start).ToList();
                        result.Add(next);
                        cycle = result;
                        return true;
                    }
                }

            path.RemoveAt(path.Count - 1);
            stack.Remove(node);
            return false;
        }

        foreach (var node in edges.Keys)
            if (!visited.Contains(node) && Dfs(node, out var cycle))
                return cycle;

        return null;
    }
}
