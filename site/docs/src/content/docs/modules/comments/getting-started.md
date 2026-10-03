---
title: "Getting Started"
description: "- .NET 10+"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Comments/docs/getting-started.md
sidebar:
  order: 2
---
## Prerequisites

- .NET 10+
- A Pragmatic.Design project with `Pragmatic.SourceGenerator` analyzer
- An entity with `[Entity]` and `[Resource("segment")]`

## Step 1: Add the Package

```xml
<PackageReference Include="Pragmatic.Comments" />
```

Or if using project references in a monorepo:
```xml
<ProjectReference Include="..\..\Pragmatic.Comments\src\Pragmatic.Comments\Pragmatic.Comments.csproj" />
```

## Step 2: Add [HasComments] to Your Entity

```csharp
using Pragmatic.Comments;
using Pragmatic.Persistence.Entity;

[Entity]
[Resource("reservations")]
[HasComments]
[BelongsTo<BookingBoundary>]
public partial class Reservation
{
    public string ReservationNumber { get; private set; } = "";
    public Guid GuestId { get; private set; }
    // ...
}
```

Build the project. The SG generates 20+ files automatically.

## Step 3: Verify Generated Output

Check `obj/Debug/net10.0/generated/` for:
- `ReservationComment.Entity.g.cs`: the comment entity
- `Reservation.TraitNavigations.g.cs`: `Comments` collection on parent
- `AddReservationCommentAction.Action.g.cs`: create action
- `EntityConfig.ReservationComment.g.cs`: EF configuration
- `ReservationCommentDto.Dto.g.cs`: read DTO with Projection
- 5 endpoint handlers (POST/GET/PUT/DELETE)

## Step 4: Test via HTTP

```http
### Add a comment
POST /api/booking/reservations/{reservationId}/comments
Content-Type: application/json

{
  "content": "Great hotel, will come back!",
  "replyToId": null
}

### Response: 201 Created
"3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

```http
### List comments (paged)
GET /api/booking/reservations/{reservationId}/comments?page=1&pageSize=20
```

```http
### Update a comment
PUT /api/booking/reservations/{reservationId}/comments/{commentId}
Content-Type: application/json

"Updated content here"
```

```http
### Delete a comment (soft-delete)
DELETE /api/booking/reservations/{reservationId}/comments/{commentId}

### Response: 204 No Content
```

## Step 5: Customize with ICommentPolicy (Optional)

```csharp
public class ReservationCommentPolicy : ICommentPolicy<Guid>
{
    private readonly INotificationService _notifications;

    public ReservationCommentPolicy(INotificationService notifications)
        => _notifications = notifications;

    public Task<bool> CanAddAsync(Guid reservationId, string content,
        string? authorId, CancellationToken ct)
        => Task.FromResult(!string.IsNullOrWhiteSpace(content));

    public async Task OnAddedAsync(Guid reservationId, Guid commentId, CancellationToken ct)
        => await _notifications.SendAsync($"New comment on reservation {reservationId}", ct);
}

// Register in DI (Startup/Program.cs)
services.AddScoped<ICommentPolicy<Guid>, ReservationCommentPolicy>();
```

## Showcase Example

See `examples/showcase/src/Showcase.Booking/Reservations/Reservation.cs` for a real-world usage with:
- `[HasComments]` on `Reservation`
- 6 E2E integration tests in `Showcase.IntegrationTests/Endpoints/CommentEndpointTests.cs`
