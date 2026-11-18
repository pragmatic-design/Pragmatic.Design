using Pragmatic.Aot.Host.Notes;

namespace Pragmatic.Aot.Host;

/// <summary>The topology: the Notes module on its database, as an application's host declares it.</summary>
[Module]
[Include<NotesModule, NotesDatabase>]
public sealed class SmokeHostModule;
