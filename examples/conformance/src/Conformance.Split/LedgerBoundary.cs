using Conformance.Split.Entities;
using Pragmatic.Actions.Attributes;

namespace Conformance.Split;

/// <summary>
///     The first of this assembly's two boundaries, and it says what is its own.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Owns&lt;Ledger&gt;]</c> is not documentation: it is what the generator derives from which
///         <c>DbContext</c> the table ends up in and which facade the operation writing that entity ends
///         up on. With a single boundary it would not be needed — that one owns everything the assembly
///         declares — and that is exactly why no other example in the repository writes it.
///     </para>
///     <para>
///         ⚠️ And <b>all</b> of them are written: an entity nobody claims is <c>PRAG0629</c>, one claimed
///         by two is <c>PRAG0630</c>. They are errors, not warnings, because what they produce is an
///         absence — a table in no context — and an absence goes unnoticed when reading.
///     </para>
/// </remarks>
[Boundary]
[Owns<Ledger>]
public partial class LedgerBoundary;
