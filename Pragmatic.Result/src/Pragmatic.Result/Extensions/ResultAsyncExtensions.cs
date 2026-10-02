namespace Pragmatic.Result.Extensions;

/// <summary>
///     Async extension methods for Result types.
/// </summary>
/// <remarks>
///     <para>
///         These extensions enable fluent async pipelines with Result types.
///         All methods accept CancellationToken for proper async cancellation support.
///     </para>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>ResultAsyncExtensions.Map.cs - Map operations</description>
///             </item>
///             <item>
///                 <description>ResultAsyncExtensions.Bind.cs - Bind operations</description>
///             </item>
///             <item>
///                 <description>ResultAsyncExtensions.Match.cs - Match and Then operations</description>
///             </item>
///             <item>
///                 <description>ResultAsyncExtensions.SideEffects.cs - Tap, OnSuccess, and OnFailure operations</description>
///             </item>
///             <item>
///                 <description>ResultAsyncExtensions.Validation.cs - Ensure operations</description>
///             </item>
///             <item>
///                 <description>ResultAsyncExtensions.Recovery.cs - OrElse operations</description>
///             </item>
///             <item>
///                 <description>ResultAsyncExtensions.VoidResult.cs - VoidResult-specific operations</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
public static partial class ResultAsyncExtensions
{
    // This file serves as the main entry point for the partial class.
    // See the other partial files for the actual implementations.
}