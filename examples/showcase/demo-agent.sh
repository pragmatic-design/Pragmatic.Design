#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════
# Pragmatic Agent + Gateway — Full E2E Demo
# Demonstrates all 8 capabilities in sequence
# ═══════════════════════════════════════════════════════════════════

set -e
BASE="http://localhost:5000"
AGENT="dotnet run --no-build --project ../../Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj --"

echo "═══════════════════════════════════════════════════════════"
echo "  Pragmatic Agent + Gateway — E2E Demo"
echo "═══════════════════════════════════════════════════════════"
echo ""
echo "Prerequisites:"
echo "  1. Agent daemon running:  dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent -- start"
echo "  2. Showcase.Host running: dotnet run --project examples/showcase/src/Showcase.Host"
echo ""
read -p "Press Enter to start the demo..."

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 1. CONFIG PUSH ═══"
echo "Setting config via Agent CLI..."
$AGENT config set WelcomeMessage "Hello from Agent KV!" --socket pragmatic-agent
echo ""
echo "Reading config via HTTP endpoint..."
curl -s "$BASE/api/agent-demo/config/WelcomeMessage" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/config/WelcomeMessage"
echo ""

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 2. TENANT-SCOPED CONFIG ═══"
$AGENT config set MaxBookings 100 --tenant hotel-rome --socket pragmatic-agent
echo ""
curl -s "$BASE/api/agent-demo/config/MaxBookings/tenant/hotel-rome" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/config/MaxBookings/tenant/hotel-rome"
echo ""

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 3. FEATURE FLAGS ═══"
echo "Flag 'fancy-greeting' is initially disabled..."
curl -s "$BASE/api/agent-demo/greeting" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/greeting"
echo ""

echo "Enabling flag via Agent CLI..."
$AGENT flag set fancy-greeting true --socket pragmatic-agent
echo ""

echo "Now the greeting changes:"
curl -s "$BASE/api/agent-demo/greeting" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/greeting"
echo ""

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 4. DYNAMIC TENANT ═══"
echo "Current tenants:"
curl -s "$BASE/api/agent-demo/tenants" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/tenants"
echo ""

echo "Adding new tenant 'hotel-milan' via Agent KV..."
$AGENT config set tenants/hotel-milan '{"TenantId":"hotel-milan","TenantName":"Hotel Milan","State":0,"CreatedAt":"2026-04-09T00:00:00Z"}' --socket pragmatic-agent
echo ""

echo "Tenants after add (no restart!):"
curl -s "$BASE/api/agent-demo/tenants" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/tenants"
echo ""

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 5. HEALTH VIA AGENT ═══"
curl -s "$BASE/api/agent-demo/health" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/health"
echo ""

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 6. SECRET MANAGEMENT ═══"
echo "Storing a secret (encrypted at rest in Agent KV)..."
$AGENT config set secret/db-password "SuperSecret2026!" --socket pragmatic-agent
echo ""

echo "Reading secret (shows last 4 chars only):"
curl -s "$BASE/api/agent-demo/secret/db-password" | python3 -m json.tool 2>/dev/null || curl -s "$BASE/api/agent-demo/secret/db-password"
echo ""

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 7. MAINTENANCE MODE VIA AGENT ═══"
echo "Activating maintenance mode..."
$AGENT config set state/app:showcase-host maintenance --socket pragmatic-agent
echo ""
echo "App should now return 503 (if Gateway is running in front)."
echo "Deactivating..."
$AGENT config set state/app:showcase-host ready --socket pragmatic-agent
echo ""

# ─────────────────────────────────────────────────────────────────
echo ""
echo "═══ 8. AGENT STATUS ═══"
$AGENT status --socket pragmatic-agent
echo ""

echo "═══════════════════════════════════════════════════════════"
echo "  Demo complete! All 8 Agent capabilities demonstrated."
echo "═══════════════════════════════════════════════════════════"
