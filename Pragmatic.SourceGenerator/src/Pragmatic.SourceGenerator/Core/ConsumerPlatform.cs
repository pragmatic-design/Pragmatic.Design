namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     What the consumer's compilation can express: its language version and the platform APIs it can see.
///     Computed once per compilation by <see cref="ConsumerPlatformDetector" />, beside <see cref="DetectedFeatures" />.
/// </summary>
/// <remarks>
///     <para>
///         The generator is one <c>netstandard2.0</c> assembly that runs in the consumer's compiler, whatever the
///         consumer targets. Reading these facts is how it writes the better form for an application on .NET 11
///         and today's form for one on .NET 10 (#834), from the same package.
///     </para>
///     <para>
///         ⚠️ A capability is the API the emitted line calls, found by its presence — not the target framework:
///         a <c>net10.0</c> application that references System.Text.Json 11 has <c>GetTypeInfo&lt;T&gt;()</c> too.
///         A consumer without a capability gets exactly the code it got before the capability existed. Templates
///         take these facts through their models; they never ask the compilation themselves.
///     </para>
///     <para>
///         Like a <see cref="DetectedFeatures" /> flag, a capability is added when a template reads it, not before:
///         each one is a lookup on every <c>Compilation</c> change.
///     </para>
/// </remarks>
internal sealed record ConsumerPlatform
{
    /// <summary>The platform the generator assumes when it knows nothing: the forms every supported consumer accepts.</summary>
    public static readonly ConsumerPlatform Baseline = new();

    /// <summary>
    ///     The consumer's C# language version, as the compiler's <c>LanguageVersion</c> value (1400 for C# 14, 1500 for
    ///     C# 15). An int, because the generator compiles against a Roslyn that has no member for the newer versions.
    /// </summary>
    public int LanguageVersion { get; init; }

    /// <summary>
    ///     <c>JsonSerializerOptions.GetTypeInfo&lt;T&gt;()</c> exists (System.Text.Json 11): typed metadata without a
    ///     <c>typeof</c> and a cast, throwing when the type cannot be resolved.
    /// </summary>
    public bool HasJsonTypeInfoOfT { get; init; }
}
