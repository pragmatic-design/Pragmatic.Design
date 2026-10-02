namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Indicates that this DomainAction should skip automatic validation.
/// </summary>
/// <remarks>
///     <para>
///         By default, the validation pipeline automatically validates DomainActions when:
///         <list type="bullet">
///             <item>
///                 The action implements <see cref="Pragmatic.Validation.ISyncValidator" /> (generated from validation
///                 attributes)
///             </item>
///             <item>An <see cref="Pragmatic.Validation.IAsyncValidator{T}" /> is registered for the action type</item>
///         </list>
///     </para>
///     <para>
///         Use this attribute when you want to:
///         <list type="bullet">
///             <item>Perform validation manually inside Execute()</item>
///             <item>Skip validation for performance reasons</item>
///             <item>Use custom validation logic outside the pipeline</item>
///         </list>
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [NoValidation] // Validation handled manually
/// public partial class ImportBulkData : DomainAction&lt;ImportResult&gt;
/// {
///     public required byte[] Data { get; init; }
///
///     public override async Task&lt;Result&lt;ImportResult, IError&gt;&gt; Execute(CancellationToken ct)
///     {
///         // Custom validation logic here
///         if (Data.Length == 0)
///             return ValidationError.For(nameof(Data), "validation.notempty");
///
///         // Process data
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NoValidationAttribute : Attribute
{
}
