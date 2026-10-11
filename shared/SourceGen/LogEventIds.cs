using System;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>The event id of a call site that does not set one.</summary>
/// <remarks>
///     Derived from the event name with FNV-1a over its UTF-16 code units, so the same name gives the same
///     id on every build, machine and runtime — <c>string.GetHashCode</c> is randomized per process and
///     would give a different id each time the host starts. Two call sites of one type that derive the
///     same id are reported by the analyzer, as two that set the same one are.
///     <para>
///         The result is the one Microsoft's <c>LoggerMessage</c> generator gives the same name, on purpose:
///         the absolute value of the signed hash, and 0 for <c>int.MinValue</c>, which has none
///         (<c>GetNonRandomizedHashCode</c> in dotnet/runtime's <c>LoggerMessageGenerator.Parser.cs</c>). A
///         project that moves its call sites from Microsoft's generator to Pragmatic's keeps the event ids its
///         filters, alerts and queries are written against.
///     </para>
/// </remarks>
internal static class LogEventIds
{
    public static int Derive(string eventName)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in eventName)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            var signed = (int)hash;
            return signed == int.MinValue ? 0 : Math.Abs(signed);
        }
    }
}
