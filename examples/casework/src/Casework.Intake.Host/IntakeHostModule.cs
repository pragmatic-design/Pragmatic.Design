using Casework.Intake;
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;

namespace Casework.Intake.Host;

/// <summary>
///     The topology of this process: one module, one database, with routing and request cultures.
/// </summary>
/// <remarks>
///     One <c>[Include]</c> and not two: Verify is another process, and a host that included it would
///     turn this example back into a monolith with extra ceremony. What crosses between them is a
///     message.
/// </remarks>
[Module]
[Include<IntakeModule, IntakeDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class IntakeHostModule;
