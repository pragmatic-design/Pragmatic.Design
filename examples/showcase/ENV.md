# Showcase — Dev Environment

## Ports

| Service | Port | URL |
|---------|------|-----|
| Showcase.Host (HTTPS) | 5001 | https://localhost:5001 |
| Showcase.Host (HTTP) | 5000 | http://localhost:5000 |
| Swagger UI | 5001 | https://localhost:5001/swagger |
| Pragmatic Agent | socket | Named pipe `pragmatic-agent` (Windows) or `/var/run/pragmatic/agent.sock` (Linux) |
| Agent Gossip | 9900/UDP | Agent-to-agent cluster communication (private network) |
| Pragmatic Gateway | 8080 | http://localhost:8080 (optional, for API Gateway mode) |
| PostgreSQL | 5433 | localhost:5433 (via docker-compose) |

## Database

**Current**: EF Core InMemory (no external DB required for basic demo).

**For real persistence**: Start PostgreSQL via Docker Compose, then configure connection:

```bash
# Start PostgreSQL (from examples/showcase/)
docker compose up -d

# PostgreSQL runs on port 5433 (mapped from 5432 inside container)
```

Then switch to PostgreSQL by changing `ShowcaseStartupStep`:

```csharp
services.AddDbContext<ShowcaseDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("Default")));
```

### Connection String (appsettings.json)

```
Host=localhost;Port=5433;Database=showcase_app;Username=pragmatic;Password=pragmatic
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
# Credentials: pragmatic / pragmatic

# Optional: add RabbitMQ for messaging
docker compose -f docker-compose.yml -f docker-compose.messaging.yml up -d
```

## API Testing (curl)

### Health Check
```bash
curl -s http://localhost:5000/health
```

### List Properties (AutoCrud)
```bash
curl -s http://localhost:5000/api/properties | jq
```

### Search Available Rooms (Anonymous)
```bash
curl -s "http://localhost:5000/api/availability?PropertyId=<guid>&CheckIn=2026-06-01&CheckOut=2026-06-05&Guests=2" | jq
```

### Create Reservation (requires tenant header)
```bash
curl -X POST http://localhost:5000/api/reservations \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: hotel-rome" \
  -d '{
    "guestId": "<guid>",
    "propertyId": "<guid>",
    "roomTypeId": "<guid>",
    "checkIn": "2026-07-01T14:00:00Z",
    "checkOut": "2026-07-05T11:00:00Z",
    "numberOfGuests": 2
  }' | jq
```

### Confirm Reservation
```bash
curl -X POST http://localhost:5000/api/reservations/<id>/confirm | jq
```

### Check In Guest (requires permission)
```bash
curl -X POST http://localhost:5000/api/reservations/<id>/check-in \
  -H "Authorization: Bearer <token>" | jq
```

### Import Seasonal Rates (requires rates.import permission)
```bash
curl -X POST http://localhost:5000/api/properties/<propertyId>/rates/import \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '[
    { "roomTypeId": "<guid>", "newBaseRate": 199.99 }
  ]' | jq
```

## Key Headers

| Header | Purpose | Required |
|--------|---------|----------|
| `X-Tenant-Id` | Tenant resolution (multi-tenancy) | Yes (for non-anonymous endpoints) |
| `Authorization` | Bearer JWT token | For permission-protected endpoints |
| `Content-Type` | `application/json` | For POST/PUT requests |
