# Examples: pragmatic-use-temporal

Copied from `examples`, which compiles in the repository and is exercised by
`examples/time-off/tests/TimeOff.IntegrationTests`, `examples/showcase/tests/Showcase.IntegrationTests` and `examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`time-off/src/TimeOff.Leave/Infrastructure/Calendar/ItalianPublicHolidays.cs`](time-off/src/TimeOff.Leave/Infrastructure/Calendar/ItalianPublicHolidays.cs) | An `IHolidayProvider`: the national holidays computed for any year, Easter included |
| [`time-off/src/TimeOff.Host/Program.cs`](time-off/src/TimeOff.Host/Program.cs) | Wiring it: `UseTemporal(t => t.UseHolidayProvider<ItalianPublicHolidays>())` |
| [`time-off/src/TimeOff.Leave/LeaveRequests/Actions/SubmitLeaveRequestAction.cs`](time-off/src/TimeOff.Leave/LeaveRequests/Actions/SubmitLeaveRequestAction.cs) | Business days with `ITemporalCalculator`: `CountBusinessDays` with its end excluded, `IsBusinessDay` with a country, the company's closures taken off |
| [`time-off/src/TimeOff.Leave/LeaveRequests/Mutations/ApproveLeaveRequestMutation.cs`](time-off/src/TimeOff.Leave/LeaveRequests/Mutations/ApproveLeaveRequestMutation.cs) | `[FromClock]`: the decision's instant written by the invoker from the application's clock |
| [`showcase/src/Showcase.Booking/Reservations/Mutations/CheckInGuestMutation.cs`](showcase/src/Showcase.Booking/Reservations/Mutations/CheckInGuestMutation.cs) | `[FromBusinessTimezone]` on a wall time the front desk types, read in the zone the host declared |
| [`invoicing/tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs`](invoicing/tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs) | A `TestClock` registered as `IClock`, so the test chooses the day |
