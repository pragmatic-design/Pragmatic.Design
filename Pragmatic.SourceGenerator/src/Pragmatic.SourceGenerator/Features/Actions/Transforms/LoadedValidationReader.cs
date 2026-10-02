using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Recognises <c>ValidateLoaded()</c> and <c>ValidateLoadedAsync(CancellationToken)</c> on an action or a
///     mutation by their signature.
/// </summary>
/// <remarks>
///     By signature and not by an attribute, as <c>Execute</c> and <c>ApplyAsync</c> are: the method is part of
///     the operation's shape. A method with the name and another signature is reported rather than skipped —
///     it reads like a rule and would never run.
/// </remarks>
internal static class LoadedValidationReader
{
    private const string ValidationError = "Pragmatic.Validation.Types.ValidationError";

    public static LoadedValidationModel Read(INamedTypeSymbol operation)
    {
        var sync = false;
        var async = false;
        var misshapen = new List<IMethodSymbol>();

        foreach (var method in operation.GetMembers("ValidateLoaded").OfType<IMethodSymbol>())
        {
            if (method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 0 }
                && method.ReturnType.ToDisplayString() == ValidationError)
                sync = true;
            else
                misshapen.Add(method);
        }

        foreach (var method in operation.GetMembers("ValidateLoadedAsync").OfType<IMethodSymbol>())
        {
            if (method is { IsStatic: false, IsGenericMethod: false, Parameters.Length: 1 }
                && method.Parameters[0].Type.ToDisplayString() == "System.Threading.CancellationToken"
                && method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } task
                && task.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task<TResult>"
                    or "System.Threading.Tasks.ValueTask<TResult>"
                && task.TypeArguments[0].ToDisplayString() == ValidationError)
                async = true;
            else
                misshapen.Add(method);
        }

        if (!sync && !async && misshapen.Count == 0)
            return LoadedValidationModel.None;

        return new LoadedValidationModel
        {
            Sync = sync,
            Async = async,
            Misshapen = misshapen.Select(m => m.Name).Distinct().ToImmutableArray(),
            Location = misshapen.Count > 0 ? LocationInfo.From(misshapen[0].Locations.FirstOrDefault()) : null
        };
    }
}
