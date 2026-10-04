# Examples: pragmatic-use-notifications

Copied from `examples/showcase/src`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Showcase.Booking/Infrastructure/Notifications/ReservationConfirmedNotificationHandler.cs`](Showcase.Booking/Infrastructure/Notifications/ReservationConfirmedNotificationHandler.cs) | Enqueueing a notification from an event handler: the content from a `.pdxemail` template in the guest's language, addressed to a user |
| [`Showcase.Booking/Infrastructure/Notifications/GuestRecipientResolver.cs`](Showcase.Booking/Infrastructure/Notifications/GuestRecipientResolver.cs) | `IRecipientResolver`: a user id becomes an address and a locale, without leaving the tenant |
| [`Showcase.Booking/BookingBoundary.cs`](Showcase.Booking/BookingBoundary.cs) | `[StoresNotifications]` on the boundary that sends them, so the table is in its database |
| [`Showcase.Host/Program.cs`](Showcase.Host/Program.cs) | `UseNotifications`: the recipient resolver, the SMTP channel, the EF Core store |
