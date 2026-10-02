# Common Mistakes

### 1. Missing [Resource] — No Endpoints Generated

**Wrong:**
```csharp
[Entity]
[HasComments]  // SG warning PRAG2601
public partial class Reservation { ... }
```

**Right:**
```csharp
[Entity]
[Resource("reservations")]  // Required for endpoint route resolution
[HasComments]
public partial class Reservation { ... }
```

**Why:** The SG needs the resource segment to derive endpoint routes (`/api/{boundary}/{segment}/{id}/comments`). Without `[Resource]`, entity + config + actions are generated but no HTTP endpoints.

### 2. Missing [Entity] — Nothing Generated

**Wrong:**
```csharp
[HasComments]  // SG error PRAG2600
public partial class Reservation { ... }
```

**Right:**
```csharp
[Entity]
[HasComments]
public partial class Reservation { ... }
```

**Why:** `[HasComments]` requires `[Entity]` to determine the parent's Id type for the FK.

### 3. Forgetting `partial` on the Entity Class

**Wrong:**
```csharp
[Entity]
[HasComments]
public class Reservation { ... }  // Not partial!
```

**Right:**
```csharp
[Entity]
[HasComments]
public partial class Reservation { ... }
```

**Why:** The SG adds a `Comments` navigation property via a partial class file. Without `partial`, the compilation fails.

### 4. Sending JSON Object for Update Instead of Raw String

**Wrong:**
```json
PUT /api/booking/reservations/{id}/comments/{commentId}
{ "content": "Updated" }
```

**Right:**
```json
PUT /api/booking/reservations/{id}/comments/{commentId}
"Updated content here"
```

**Why:** The Update endpoint binds `Content` as `[FromBody] string content` (single scalar parameter, no wrapper DTO). Send the string directly.

### 5. Not Adding Comment Permissions to Auth Headers

**Wrong:**
```csharp
// Test sends POST but gets 403
var response = await client.PostAsync("/api/booking/reservations/{id}/comments", body);
```

**Right:**
```csharp
client.DefaultRequestHeaders.Add("X-User-Permissions",
    "booking.reservation.comments.create");
```

**Why:** Generated endpoints use the permission constants from `{Entity}CommentPermissions`. Ensure the test client has the required permissions.

### 6. Registering ICommentPolicy Without the Right Generic Type

**Wrong:**
```csharp
services.AddScoped<ICommentPolicy<string>, MyPolicy>();  // Wrong TEntityId!
```

**Right:**
```csharp
services.AddScoped<ICommentPolicy<Guid>, MyPolicy>();  // Match entity's TId
```

**Why:** The SG resolves `ICommentPolicy<TEntityId>` where TEntityId matches the parent entity's Id type (usually Guid).

### 7. Expecting Hard Delete

**Wrong:**
```csharp
// After DELETE, expecting the row to be gone from the database
var count = await db.Set<ReservationComment>().CountAsync();  // Still there!
```

**Right:**
```csharp
// Comments are soft-deleted (IsDeleted = true)
// The global query filter hides them from normal queries
var count = await db.Set<ReservationComment>()
    .IgnoreQueryFilters()
    .Where(c => c.IsDeleted)
    .CountAsync();
```

**Why:** `[HasComments]` always uses soft-delete with a global query filter. The row remains in the database with `IsDeleted = true`, `DeletedAt`, `DeletedBy` populated.

### 8. Expecting Edit After Window Expires

**Wrong:**
```csharp
[HasComments(EditWindowMinutes = 30)]
// 31 minutes later...
await actions.UpdateReservationComment(action); // 403 Forbidden
```

**Right:**
```csharp
// Edit within the configured window, or set EditWindowMinutes = -1 for no limit
[HasComments(EditWindowMinutes = -1)]  // Default: no time limit
```

**Why:** When `EditWindowMinutes > 0`, the Update action checks `createdAt + window > now`. After expiry, edits are rejected with ForbiddenError.
