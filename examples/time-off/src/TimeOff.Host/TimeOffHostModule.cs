using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;
using TimeOff.Leave;

namespace TimeOff.Host;

/// <summary>
///     The topology: the Leave module on the one database, with routing and request cultures.
/// </summary>
[Module]
[Include<LeaveModule, AppDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class TimeOffHostModule;
