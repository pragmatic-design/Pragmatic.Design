namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Defines a Pragmatic module.
/// </summary>
/// <remarks>
///     <para>
///         A module represents a cohesive unit of functionality. It depends on another module with
///         <see cref="IncludeModuleAttribute{TModule}" /> — by type, so the compiler checks the name. The
///         Composition generator uses this information to:
///     </para>
///     <list type="bullet">
///         <item>Build a dependency graph of all modules</item>
///         <item>Generate optimized service registration (avoiding duplicates)</item>
///         <item>Produce a topology report showing module relationships</item>
///     </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the module name.
    ///     Defaults to the assembly name if not specified.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Gets or sets the module version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    ///     Gets or sets a description of the module.
    /// </summary>
    public string? Description { get; set; }
}
