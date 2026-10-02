namespace Pragmatic.Messaging.Dashboard;

/// <summary>
///     Embedded single-file HTML panel served at <c>GET {path}/panel</c>.
///     Vanilla JS over the ops API — no build step, no external assets.
/// </summary>
internal static class MessagingDashboardPanel
{
    /// <summary>The panel page. Relative fetches keep it working under any base path.</summary>
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Pragmatic Messaging</title>
<style>
  :root { color-scheme: dark; }
  body { margin: 0; padding: 1.5rem; background: #0f1115; color: #d7dae0; font: 14px/1.5 system-ui, sans-serif; }
  h1 { font-size: 1.2rem; margin: 0 0 1rem; color: #fff; }
  h2 { font-size: 0.95rem; margin: 1.5rem 0 0.5rem; color: #9aa4b2; text-transform: uppercase; letter-spacing: .05em; }
  .cards { display: flex; gap: 1rem; flex-wrap: wrap; }
  .card { background: #171a21; border: 1px solid #262b36; border-radius: 8px; padding: 0.8rem 1.2rem; min-width: 8rem; }
  .card .v { font-size: 1.6rem; font-weight: 600; color: #fff; }
  .card .l { font-size: 0.75rem; color: #9aa4b2; }
  table { width: 100%; border-collapse: collapse; background: #171a21; border-radius: 8px; overflow: hidden; }
  th, td { text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid #262b36; font-size: 0.85rem; }
  th { color: #9aa4b2; font-weight: 500; }
  tr:last-child td { border-bottom: none; }
  button { background: #2b6cb0; color: #fff; border: 0; border-radius: 5px; padding: 0.3rem 0.7rem; cursor: pointer; font-size: 0.8rem; }
  button.danger { background: #9b2c2c; }
  button:hover { filter: brightness(1.15); }
  .empty { color: #5d6673; font-style: italic; padding: 0.5rem 0.75rem; }
  .err { color: #f56565; }
  #refresh { float: right; background: #262b36; }
</style>
</head>
<body>
<h1>Pragmatic Messaging <button id="refresh" onclick="load()">Refresh</button></h1>
<div class="cards" id="cards"></div>
<h2>Dead letters</h2><div id="dead"></div>
<h2>Active sagas</h2><div id="sagas"></div>
<h2>Outbox (pending)</h2><div id="outbox"></div>
<h2>Audit (latest)</h2><div id="audit"></div>
<script>
const base = location.pathname.replace(/\/panel\/?$/, "");
const key = localStorage.getItem("x-messaging-key");
const headers = key ? { "X-Messaging-Key": key } : {};
// Mutating calls always carry a non-simple header so a cross-site POST is forced through a CORS
// preflight (blocked by same-origin policy) even in loopback (no-key) mode — CSRF defence.
const mutatingHeaders = { ...headers, "X-Messaging-Csrf": "1" };

async function get(path) {
  const res = await fetch(base + path, { headers });
  if (res.status === 401) {
    localStorage.setItem("x-messaging-key", prompt("X-Messaging-Key?") ?? "");
    location.reload();
  }
  if (!res.ok) throw new Error(res.status);
  return res.json();
}

function table(rows, cols, render) {
  if (!rows.length) return '<div class="empty">none</div>';
  const head = "<tr>" + cols.map(c => `<th>${c}</th>`).join("") + "</tr>";
  return `<table>${head}${rows.map(render).join("")}</table>`;
}

// HTML-escape every value interpolated into innerHTML. Fields like error and correlationId are
// externally influenced (derived from inbound message headers / exception text), so without this a
// crafted correlation id or error string is stored XSS that runs with the ops admin's replay/delete
// authority.
function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

async function replay(id) {
  await fetch(`${base}/dead-letters/${id}/replay`, { method: "POST", headers: mutatingHeaders });
  load();
}
async function removeDl(id) {
  await fetch(`${base}/dead-letters/${id}`, { method: "DELETE", headers: mutatingHeaders });
  load();
}

async function load() {
  try {
    const [status, dead, sagas, outbox, audit] = await Promise.all([
      get("/status"), get("/dead-letters"), get("/sagas"), get("/outbox"), get("/audit")]);

    document.getElementById("cards").innerHTML = [
      ["Transport", `${status.transport} (${status.transportStatus})`],
      ["Dead letters", status.deadLetters],
      ["Outbox pending", status.outboxPending],
      ["Active sagas", status.activeSagas],
    ].map(([l, v]) => `<div class="card"><div class="v">${v}</div><div class="l">${l}</div></div>`).join("");

    document.getElementById("dead").innerHTML = table(dead,
      ["Type", "Error", "Retries", "Failed at", ""],
      m => `<tr><td>${esc(m.messageType.split(".").pop())}</td><td class="err">${esc(m.error)}</td><td>${m.retryCount}</td>
        <td>${new Date(m.failedAt).toLocaleString()}</td>
        <td><button onclick="replay('${esc(m.id)}')">Replay</button> <button class="danger" onclick="removeDl('${esc(m.id)}')">Delete</button></td></tr>`);

    document.getElementById("sagas").innerHTML = table(
      sagas.flatMap(g => g.active.map(a => ({ ...a, saga: g.name }))),
      ["Saga", "State", "Correlation", "Started"],
      s => `<tr><td>${esc(s.saga)}</td><td>${esc(s.state)}</td><td>${esc(s.correlationId)}</td>
        <td>${new Date(s.startedAt).toLocaleString()}</td></tr>`);

    document.getElementById("outbox").innerHTML = table(outbox,
      ["Boundary", "Type", "Created", "Next attempt", "Retries", "Error"],
      m => `<tr><td>${esc(m.boundary)}</td><td>${esc(m.messageType.split(".").pop())}</td>
        <td>${new Date(m.createdAt).toLocaleString()}</td>
        <td>${m.nextAttemptAt ? new Date(m.nextAttemptAt).toLocaleString() : "-"}</td>
        <td>${m.retryCount}</td><td class="err">${esc(m.error ?? "")}</td></tr>`);

    document.getElementById("audit").innerHTML = table(audit,
      ["Type", "Message", "Outcome", "When", "Detail"],
      e => `<tr><td>${esc(e.messageType.split(".").pop())}</td><td>${esc(e.messageId ?? "-")}</td>
        <td>${esc(e.outcome)}</td><td>${new Date(e.occurredAt).toLocaleString()}</td>
        <td class="err">${esc(e.detail ?? "")}</td></tr>`);
  } catch (e) {
    document.getElementById("cards").innerHTML = `<div class="card err">Failed to load: ${esc(e)}</div>`;
  }
}
load();
setInterval(load, 5000);
</script>
</body>
</html>
""";
}
