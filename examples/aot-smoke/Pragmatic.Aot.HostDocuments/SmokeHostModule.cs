using Pragmatic.Aot.Host.Notes;

// ReSharper disable once CheckNamespace
namespace Pragmatic.Aot.Host;

/// <summary>The topology: the Notes module on its database, as an application's host declares it.</summary>
[Module]
[Include<NotesModule, NotesDatabase>]
public sealed class SmokeHostModule;
