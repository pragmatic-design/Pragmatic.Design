using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Transforms;

/// <summary>
///     Builds the typed test client operations by correlating the app's generated
///     <c>ApiRoutes</c> builders (typed parameters) with its
///     <c>[assembly: PragmaticEndpointContract]</c> attributes (body/response shape).
///     Correlation key: (Boundary, OperationName).
/// </summary>
internal static class ApiClientExtractor
{
    private static readonly SymbolDisplayFormat FullyQualifiedNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static ImmutableArray<ApiClientOperationModel> Extract(Compilation compilation, CancellationToken ct)
    {
        var contracts = CollectContracts(compilation);
        if (contracts.Count == 0)
            return ImmutableArray<ApiClientOperationModel>.Empty;

        var operations = ImmutableArray.CreateBuilder<ApiClientOperationModel>();

        foreach (var routesType in FindApiRoutesTypes(compilation, ct))
        {
            var routesFqn = routesType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            foreach (var boundaryClass in routesType.GetTypeMembers())
            {
                var boundary = boundaryClass.Name;

                foreach (var method in boundaryClass.GetMembers().OfType<IMethodSymbol>()
                             .Where(m => m is { IsStatic: true, MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public }))
                {
                    if (!contracts.TryGetValue((boundary, method.Name), out var contract))
                        continue;

                    if (contract.IsStreaming)
                        continue; // SSE clients read raw streams — out of the typed client's scope

                    var parameters = method.Parameters.Select(p => new ApiClientParameterModel
                    {
                        TypeName = p.Type.ToDisplayString(FullyQualifiedNullable),
                        Name = p.Name,
                        IsOptional = p.HasExplicitDefaultValue
                    }).ToImmutableArray();

                    operations.Add(new ApiClientOperationModel
                    {
                        Boundary = boundary,
                        Name = method.Name,
                        HttpMethod = contract.HttpMethod,
                        RouteBuilder = $"{routesFqn}.{boundary}.{method.Name}",
                        Parameters = parameters,
                        HasBody = contract.HasBody,
                        ResponseType = contract.IsVoid ? null : contract.ResponseType
                    });
                }
            }
        }

        return operations.ToImmutable();
    }

    private sealed record ContractInfo(
        string HttpMethod, bool IsVoid, string? ResponseType, bool HasBody, bool IsStreaming);

    private static Dictionary<(string Boundary, string Name), ContractInfo> CollectContracts(Compilation compilation)
    {
        var contracts = new Dictionary<(string, string), ContractInfo>();

        foreach (var assembly in EnumerateAppAssemblies(compilation))
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "PragmaticEndpointContractAttribute")
                continue;

            string? boundary = null, operationName = null, httpMethod = "GET", responseType = null;
            var isVoid = false;
            var hasBody = false;
            var isStreaming = false;

            foreach (var named in attr.NamedArguments)
                switch (named.Key)
                {
                    case "Boundary":
                        boundary = named.Value.Value?.ToString();
                        break;
                    case "OperationName":
                        operationName = named.Value.Value?.ToString();
                        break;
                    case "HttpMethod":
                        httpMethod = named.Value.Value?.ToString() ?? "GET";
                        break;
                    case "IsVoid":
                        isVoid = named.Value.Value is true;
                        break;
                    case "IsStreaming":
                        isStreaming = named.Value.Value is true;
                        break;
                    case "ResponseType" when named.Value.Value is INamedTypeSymbol responseSymbol:
                        responseType = responseSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        break;
                    case "HasBody":
                        hasBody = named.Value.Value is true;
                        break;
                }

            if (boundary is not null && operationName is not null)
                contracts[(boundary, operationName)] =
                    new ContractInfo(httpMethod, isVoid, responseType, hasBody, isStreaming);
        }

        return contracts;
    }

    private static IEnumerable<INamedTypeSymbol> FindApiRoutesTypes(Compilation compilation, CancellationToken ct)
    {
        foreach (var assembly in EnumerateAppAssemblies(compilation))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var type in FindApiRoutesInNamespace(assembly.GlobalNamespace, ct))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> FindApiRoutesInNamespace(INamespaceSymbol ns, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var type in ns.GetTypeMembers("ApiRoutes"))
            yield return type;

        foreach (var child in ns.GetNamespaceMembers())
        foreach (var type in FindApiRoutesInNamespace(child, ct))
            yield return type;
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAppAssemblies(Compilation compilation)
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

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);
}
