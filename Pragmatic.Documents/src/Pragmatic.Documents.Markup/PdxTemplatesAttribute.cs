namespace Pragmatic.Documents.Markup;

/// <summary>
///     Declares that this assembly embeds <c>.pdxdoc</c> / <c>.pdxemail</c> templates, so every host that
///     includes it registers it as a template source for <see cref="IPdxTemplates" />.
/// </summary>
/// <typeparam name="TAnchor">
///     A public type of this assembly — usually its <c>[Module]</c> class. The host names the assembly
///     through it (<c>FromAssemblyOf&lt;TAnchor&gt;()</c>), since a host can only reach another assembly
///     through a type it can see.
/// </typeparam>
/// <example>
///     <code>
/// [assembly: PdxTemplates&lt;BookingModule&gt;]
///     </code>
/// </example>
/// <remarks>
///     The templates themselves stay <c>EmbeddedResource</c> items, found by file name. Without this, a
///     module has no way to bring its templates with it: its startup step is not discovered across
///     assemblies, and every host that includes it would have to write <c>AddPdxTemplates</c> by hand.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PdxTemplatesAttribute<TAnchor> : Attribute
    where TAnchor : class;
