# Showcase — Dev Environment

## Ports

| Service | Port | URL |
|---------|------|-----|
| Showcase.Host (HTTP) | 5000 | http://localhost:5000 |
| Scalar API UI (Development) | 5000 | http://localhost:5000/scalar/v1 |
| OpenAPI document (Development) | 5000 | http://localhost:5000/openapi/v1.json |
| Pragmatic Agent | socket | Named pipe `pragmatic-agent` (Windows) or `/var/run/pragmatic/agent.sock` (Linux) |
| Agent Gossip | 9900/UDP | Agent-to-agent cluster communication (private network) |
| Pragmatic Gateway | 8080 | http://localhost:8080 (optional, for API Gateway mode) |
| PostgreSQL | 5433 | localhost:5433 (via docker-compose) |

## Database

PostgreSQL, two databases: `showcase_app` (Accounts, Catalog, Booking) and `showcase_financial`
(Billing). Start it before the host:

```bash
# From examples/showcase/
docker compose up -d

# PostgreSQL runs on port 5433 (mapped from 5432 inside container)
```

The host creates the schema of both at startup (`app.UsePragmaticMigrations()` in `Program.cs`). They
start empty: no demo data is seeded.

### Connection Strings (Showcase.Host/appsettings.json)

```
App:       Host=localhost;Port=5433;Database=showcase_app;Username=pragmatic;Password=Pragmatic@2026!
Financial: Host=localhost;Port=5433;Database=showcase_financial;Username=pragmatic;Password=Pragmatic@2026!
```

## How to Run

### Option A: Development (auto-start Agent)

```bash
# From repo root — Agent daemon starts automatically in Development mode
cd examples/showcase/src/Showcase.Host
dotnet run
# UseAgent() detects Development environment → spawns Agent daemon as child process
# When the app stops, the Agent stops too.
```

### Option B: Manual (Agent + App separately)

```bash
# Terminal 1: Start Agent daemon
cd Pragmatic.Agent/src/Pragmatic.Agent
dotnet run -- start --socket pragmatic-agent

# Terminal 2: Start Showcase
cd examples/showcase/src/Showcase.Host
dotnet run
```

### Option C: Production (Agent as system service)

```bash
# Install Agent once (Linux)
dotnet publish Pragmatic.Agent/src/Pragmatic.Agent -c Release -o /opt/pragmatic-agent
sudo cp pragmatic-agent.service /etc/systemd/system/
sudo systemctl enable pragmatic-agent
sudo systemctl start pragmatic-agent

# Deploy app normally — UseAgent() connects to the running daemon
```

## Docker (PostgreSQL)

```bash
# Preferred: use the included docker-compose.yml
cd examples/showcase
docker compose up -d

# PostgreSQL 17 starts on port 5433 with databases:
#   - showcase_app (Accounts, Booking, Catalog)
#   - showcase_financial (Billing)
# Credentials: pragmatic / Pragmatic@2026!

# Optional: add RabbitMQ for messaging
docker compose -f docker-compose.yml -f docker-compose.messaging.yml up -d
```

## API Testing (curl)

In Development the host takes the caller from headers (`HeaderUserMiddleware`): `X-User-Id`, `X-User-Name`
and `X-User-Permissions` (comma-separated). There is no token to obtain. Every request also names its
tenant in `X-Tenant-Id`. The databases start empty, so a reservation needs a property, a room type and
a guest first. Each command prints the `id` the next one uses.

Routes in the `/api/reservations` group take `?api-version=1.0`; without it the answer is `400`
`ApiVersionUnspecified`. The full list, with every body, is in Scalar (`http://localhost:5000/scalar/v1`)
and in `http://localhost:5000/openapi/v1.json`.

### Health Check
```bash
curl -s http://localhost:5000/health
```

### Create a Property
```bash
curl -s -X POST http://localhost:5000/api/properties \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: hotel-rome" \
  -H "X-User-Id: demo" \
  -H "X-User-Permissions: catalog.property.create" \
  -d '{ "code": "ROME-01", "name": "Hotel Roma", "city": "Rome", "country": "IT", "starRating": 4 }' | jq
```

### Create a Room Type
```bash
curl -s -X POST http://localhost:5000/api/room-types \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: hotel-rome" \
  -H "X-User-Id: demo" \
  -H "X-User-Permissions: catalog.room-type.create" \
  -d '{ "propertyId": "<propertyId>", "name": "Double", "code": "DBL", "baseRate": 120, "currency": "EUR", "maxOccupancy": 2, "totalRooms": 5 }' | jq
```

### Register a Guest
```bash
curl -s -X POST http://localhost:5000/api/guests \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: hotel-rome" \
  -H "X-User-Id: demo" \
  -H "X-User-Permissions: booking.guest.create" \
  -d '{ "firstName": "Ada", "lastName": "Lovelace", "email": "ada@example.com" }' | jq
```

### Search Available Rooms (Anonymous)
```bash
curl -s "http://localhost:5000/api/availability?PropertyId=<propertyId>&CheckIn=2027-07-01&CheckOut=2027-07-05&Guests=2" \
  -H "X-Tenant-Id: hotel-rome" | jq
```

### Create a Reservation
```bash
curl -s -X POST "http://localhost:5000/api/reservations?api-version=1.0" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: hotel-rome" \
  -H "X-User-Id: demo" \
  -H "X-User-Permissions: booking.reservation.create" \
  -d '{ "request": {
    "guestId": "<guestId>",
    "propertyId": "<propertyId>",
    "roomTypeId": "<roomTypeId>",
    "checkIn": "2027-07-01T14:00:00Z",
    "checkOut": "2027-07-05T11:00:00Z",
    "numberOfGuests": 2 } }'
# → 201 and the reservation's id
```

### Confirm the Reservation
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:5000/api/reservations/<id>/confirm?api-version=1.0" \
  -H "X-Tenant-Id: hotel-rome" \
  -H "X-User-Id: demo" \
  -H "X-User-Permissions: booking.reservation.update"
# → 204; without the permission, 403 naming booking.reservation.update.
# Billing creates the invoice from the ReservationConfirmed event, in showcase_financial.
```

### Read it back
```bash
curl -s "http://localhost:5000/api/reservations/<id>?api-version=1.0" \
  -H "X-Tenant-Id: hotel-rome" \
  -H "X-User-Id: demo" \
  -H "X-User-Permissions: booking.reservation.read" | jq
# → "status": "Confirmed"
```

### Check In the Guest
```bash
curl -s -X POST "http://localhost:5000/api/reservations/<id>/check-in?api-version=1.0" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: hotel-rome" \
  -H "X-User-Id: demo" \
  -H "X-User-Permissions: booking.reservation.update" \
  -d '{ "roomNumber": "101" }' | jq
# Before the check-in date the answer is 409: "Early check-in not available".
```

Other operations (importing seasonal rates with `rates.import`, paying an invoice, uploading a photo):
see Scalar for their routes, bodies and permissions.

## Key Headers

| Header | Purpose | Required |
|--------|---------|----------|
| `X-Tenant-Id` | Tenant resolution (multi-tenancy) | Yes |
| `X-User-Id` | The caller, in Development | For endpoints that require a caller |
| `X-User-Permissions` | The caller's permissions, comma-separated, in Development | For permission-protected endpoints |
| `Content-Type` | `application/json` | For POST/PUT requests |
