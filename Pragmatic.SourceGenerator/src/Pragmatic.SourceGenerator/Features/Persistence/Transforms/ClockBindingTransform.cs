using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads the <c>[FromClock]</c> properties of an operation, and what the clock gives each one.
/// </summary>
internal static class ClockBindingTransform
{
    /// <summary>Every <c>[FromClock]</c> property of the operation, with its clock member or its problem.</summary>
    public static ImmutableArray<ClockBindingModel> Extract(INamedTypeSymbol operation)
    {
        var bindings = ImmutableArray.CreateBuilder<ClockBindingModel>();

        foreach (var property in operation.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || !InvokerBinding.IsFromTheClock(property))
                continue;

            var clockMember = property.Type.ToDisplayString() switch
            {
                "System.DateOnly" => "UtcToday",
                "System.DateTimeOffset" => "UtcNow",
                _ => null
            };

            bindings.Add(new ClockBindingModel
            {
                PropertyName = property.Name,
                ClockMember = clockMember,
                Problem = ProblemOf(property, clockMember),
                Location = LocationInfo.From(property.Locations.FirstOrDefault())
            });
        }

        return bindings.ToImmutable();
    }

    private static string? ProblemOf(IPropertySymbol property, string? clockMember)
    {
        if (clockMember is null)
            return $"it is '{property.Type.ToDisplayString()}', and the clock gives a DateOnly (today, UTC) or a DateTimeOffset (now, UTC)";

        // Private and not init: the generated nested invoker reaches it, and nobody else does. Any wider
        // setter is a value a caller writes and the invoker then overwrites — an input that is not one.
        return property.SetMethod is { IsInitOnly: false, DeclaredAccessibility: Accessibility.Private }
            ? null
            : "its caller can set it; declare it '{ get; private set; }', so the generated invoker is the only one to write it";
    }
}
