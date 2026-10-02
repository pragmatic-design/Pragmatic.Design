// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Naming utilities for source generation.
///     Handles suffix deduplication to avoid names like "FooMutationMutationInvoker"
///     when the user already named their class "FooMutation".
/// </summary>
internal static class NamingHelper
{
    /// <summary>
    ///     Appends a suffix to a name, avoiding duplication when the name already
    ///     ends with part of the suffix. For compound suffixes like "MutationInvoker",
    ///     if the name ends with "Mutation", only "Invoker" is appended.
    /// </summary>
    /// <example>
    ///     AppendSuffix("CancelReservation", "MutationInvoker") → "CancelReservationMutationInvoker"
    ///     AppendSuffix("CancelReservationMutation", "MutationInvoker") → "CancelReservationMutationInvoker"
    ///     AppendSuffix("GetOrder", "Invoker") → "GetOrderInvoker"
    ///     AppendSuffix("GetOrderInvoker", "Invoker") → "GetOrderInvoker"
    ///     AppendSuffix("Order", "Repository") → "OrderRepository"
    ///     AppendSuffix("OrderRepository", "Repository") → "OrderRepository"
    /// </example>
    internal static string AppendSuffix(string name, string suffix)
    {
        if (name.EndsWith(suffix, System.StringComparison.Ordinal))
            return name;

        // Try progressively shorter prefixes of the suffix at word boundaries
        for (var i = suffix.Length - 1; i > 0; i--)
        {
            if (!char.IsUpper(suffix[i]))
                continue;

            var prefix = suffix.Substring(0, i);
            if (name.EndsWith(prefix, System.StringComparison.Ordinal))
                return name + suffix.Substring(i);
        }

        return name + suffix;
    }
}
