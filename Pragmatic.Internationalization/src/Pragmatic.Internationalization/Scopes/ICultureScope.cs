using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Scopes;

/// <summary>
///     Marker interface for strongly-typed culture scopes.
///     Implement this to define named culture scopes (e.g., invoicing, reporting)
///     that can be resolved at compile-time instead of using magic strings.
/// </summary>
/// <remarks>
///     <para>
///         Similar to the IFeatureFlag pattern — each scope is a type with a static name
///         and default culture, enabling type-safe scope management.
///     </para>
///     <para>
///         Usage:
///         <code>
///         public class InvoicingScope : ICultureScope
///         {
///             public static string Name => "invoicing";
///             public static CultureCode DefaultCulture => CultureCode.Italian;
///         }
///
///         // Set scope
///         I18N.SetScope&lt;InvoicingScope&gt;(CultureCode.German);
///
///         // Get scope
///         var culture = I18N.GetScope&lt;InvoicingScope&gt;();
///
///         // Use in LocalizedString
///         var title = product.Name.GetForScope&lt;InvoicingScope&gt;();
///         </code>
///     </para>
/// </remarks>
public interface ICultureScope
{
    /// <summary>
    ///     Gets the unique name of this scope. Used as the key in the scope dictionary.
    /// </summary>
    static abstract string Name { get; }

    /// <summary>
    ///     Gets the default culture for this scope when no explicit culture has been set.
    /// </summary>
    static abstract CultureCode DefaultCulture { get; }
}
