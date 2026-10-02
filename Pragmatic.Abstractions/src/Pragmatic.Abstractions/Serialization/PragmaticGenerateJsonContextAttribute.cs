namespace Pragmatic.Serialization;

/// <summary>
///     Opt-in marker: when present on an assembly, the Pragmatic source generator emits a
///     <c>JsonSerializerContext</c> subclass covering that assembly's serializable boundary types
///     (messages, events, job parameters, endpoint/action DTOs, entities), for AOT-safe serialization
///     with the reflection fallback disabled.
/// </summary>
/// <remarks>
///     Equivalent to setting the build property <c>&lt;PragmaticGenerateJsonContext&gt;true&lt;/&gt;</c>.
///     Apply once per boundary assembly, or globally via <c>Directory.Build.props</c>:
///     <code>&lt;ItemGroup&gt;&lt;AssemblyAttribute Include="Pragmatic.Serialization.PragmaticGenerateJsonContext" /&gt;&lt;/ItemGroup&gt;</code>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PragmaticGenerateJsonContextAttribute : Attribute;
