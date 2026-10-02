using Pragmatic.Composition.Attributes;

namespace Conformance.Split;

/// <summary>
///     The third module the host includes, and the only one with two boundaries inside.
/// </summary>
/// <remarks>
///     <para>
///         One <c>[Module]</c> per assembly remains the rule — two is <c>PRAG0628</c> — and it is a
///         different thing from two <b>boundaries</b>: the module is the unit the host includes and maps
///         to a database, the boundary is the unit that owns entities and commits. This assembly has one
///         module and two boundaries, the setup in which <c>[Owns&lt;T&gt;]</c> has something to decide.
///     </para>
///     <para>
///         ⚠️ And the module is <b>not</b> named like its boundaries, on purpose: the host links the
///         include to the assembly, not to a name. Linking by name — <c>SplitModule</c> → «Split» looked
///         up among the names derived from the boundaries — would find neither here, so no
///         <c>DbContext</c> would be registered and no route mapped.
///     </para>
/// </remarks>
[Module(Name = "Conformance.Split", Version = "1.0.0",
    Description = "An assembly with two boundaries: the cell in which [Owns<T>] decides something")]
public sealed class SplitModule;
