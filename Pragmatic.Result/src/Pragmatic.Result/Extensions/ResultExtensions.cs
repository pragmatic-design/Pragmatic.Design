namespace Pragmatic.Result.Extensions;

/// <summary>
///     Extension methods for Result types.
/// </summary>
/// <remarks>
///     <para>
///         These extensions provide utilities for working with Result types including:
///         bridging exception-throwing code, combining results, side effects, validation,
///         error recovery, and value extraction.
///     </para>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>ResultExtensions.Combine.cs - Combine operations for tuple results</description>
///             </item>
///             <item>
///                 <description>ResultExtensions.Collect.cs - CollectAll for error aggregation</description>
///             </item>
///             <item>
///                 <description>ResultExtensions.SideEffects.cs - Tap, OnSuccess, OnFailure operations</description>
///             </item>
///             <item>
///                 <description>ResultExtensions.Validation.cs - Ensure operations</description>
///             </item>
///             <item>
///                 <description>ResultExtensions.Recovery.cs - OrElse, Recover operations</description>
///             </item>
///             <item>
///                 <description>ResultExtensions.Value.cs - GetValueOrDefault, GetValueOrThrow operations</description>
///             </item>
///             <item>
///                 <description>ResultExtensions.Partition.cs - Partition operations</description>
///             </item>
///             <item>
///                 <description>ResultExtensions.Widen.cs - AsIError widening to Result&lt;T, IError&gt;</description>
///             </item>
///         </list>
///     </para>
///     <para>
///         <b>Note:</b> the <c>Result.Try</c> and <c>Result.FromNullable</c> factory methods live on the
///         <see cref="Result" /> static class (see <c>Result.Factories.cs</c>), not here.
///     </para>
/// </remarks>
public static partial class ResultExtensions
{
    // This file serves as the main entry point for the partial class.
    // See the other partial files for the actual implementations.
}