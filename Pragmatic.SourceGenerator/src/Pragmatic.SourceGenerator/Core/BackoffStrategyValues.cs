namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The numeric values of <c>Pragmatic.Resilience.Attributes.BackoffStrategy</c>, mirrored.
/// </summary>
/// <remarks>
///     <para>
///         The generator compiles to netstandard2.0 and references no runtime assembly, so it cannot
///         name the enum it reads. It reads the value off the attribute's metadata as an integer and
///         emits a cast back to the real type — which means the numbers have to live here too.
///     </para>
///     <para>
///         ⚠️ This is a hand-written copy of a declaration that lives elsewhere, and the compiler
///         cannot check it. If a member is added or renumbered in
///         <c>Pragmatic.Resilience/src/Pragmatic.Resilience/Attributes/BackoffStrategy.cs</c>, it must
///         be changed here in the same commit. <c>Unspecified</c> is what an unwritten argument reads
///         as, and each feature maps it to its own default.
///     </para>
/// </remarks>
internal static class BackoffStrategyValues
{
    /// <summary>Not written on the declaration: the reading feature applies its own default.</summary>
    public const int Unspecified = 0;

    /// <summary>Constant delay.</summary>
    public const int Fixed = 1;

    /// <summary>The base delay doubled for each attempt.</summary>
    public const int Exponential = 2;

    /// <summary>Exponential with random jitter added.</summary>
    public const int ExponentialWithJitter = 3;
}
