namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Embedded HTML admin panel for maintenance mode.
///     Vanilla JS + SSE — zero dependencies, ~200 lines.
/// </summary>
public static class MaintenancePanel
{
    /// <summary>The self-contained HTML document (vanilla JS + SSE) served at the admin panel route.</summary>
    public const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Pragmatic — Maintenance</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body { font-family: system-ui, -apple-system, sans-serif; background: #0f1117; color: #e1e4e8; padding: 2rem; }
                .container { max-width: 800px; margin: 0 auto; }
                h1 { font-size: 1.5rem; margin-bottom: 1.5rem; display: flex; align-items: center; gap: 0.5rem; }
                .status-badge { display: inline-block; padding: 0.25rem 0.75rem; border-radius: 1rem; font-size: 0.875rem; font-weight: 600; }
                .status-active { background: #d4380044; color: #ff6b6b; border: 1px solid #d4380066; }
                .status-ok { background: #52c41a44; color: #73d13d; border: 1px solid #52c41a66; }
                .card { background: #161b22; border: 1px solid #30363d; border-radius: 0.5rem; padding: 1.25rem; margin-bottom: 1rem; }
                .card h2 { font-size: 1rem; color: #8b949e; margin-bottom: 0.75rem; text-transform: uppercase; letter-spacing: 0.05em; }
                .info-grid { display: grid; grid-template-columns: 120px 1fr; gap: 0.5rem; font-size: 0.9rem; }
                .info-grid dt { color: #8b949e; }
                .info-grid dd { color: #e1e4e8; }
                .progress-bar { height: 6px; background: #21262d; border-radius: 3px; overflow: hidden; margin-top: 0.5rem; }
                .progress-fill { height: 100%; background: linear-gradient(90deg, #1f6feb, #58a6ff); border-radius: 3px; transition: width 0.3s ease; }
                .log { max-height: 400px; overflow-y: auto; font-family: 'JetBrains Mono', 'Fira Code', monospace; font-size: 0.8rem; line-height: 1.6; }
                .log-entry { padding: 0.25rem 0; border-bottom: 1px solid #21262d; }
                .log-entry.error { color: #ff6b6b; }
                .log-entry .time { color: #484f58; margin-right: 0.5rem; }
                .log-entry .db { color: #58a6ff; margin-right: 0.5rem; }
                .actions { display: flex; gap: 0.75rem; margin-top: 1rem; }
                .btn { padding: 0.5rem 1rem; border: 1px solid #30363d; border-radius: 0.375rem; background: #21262d; color: #e1e4e8; cursor: pointer; font-size: 0.875rem; transition: all 0.15s; }
                .btn:hover { background: #30363d; }
                .btn-danger { border-color: #d4380066; color: #ff6b6b; }
                .btn-danger:hover { background: #d4380033; }
                .connected { color: #73d13d; } .disconnected { color: #ff6b6b; }
            </style>
        </head>
        <body>
            <div class="container">
                <h1>Pragmatic Maintenance <span id="statusBadge" class="status-badge status-active">ACTIVE</span></h1>

                <div class="card">
                    <h2>Status</h2>
                    <dl class="info-grid">
                        <dt>Reason</dt><dd id="reason">—</dd>
                        <dt>Since</dt><dd id="since">—</dd>
                        <dt>ETA</dt><dd id="eta">—</dd>
                        <dt>Stream</dt><dd id="sseStatus" class="disconnected">Connecting...</dd>
                    </dl>
                    <div class="progress-bar"><div id="progressFill" class="progress-fill" style="width:0%"></div></div>
                </div>

                <div class="card">
                    <h2>Migration Log</h2>
                    <div id="log" class="log"></div>
                </div>

                <div class="actions">
                    <button class="btn" onclick="refreshStatus()">Refresh</button>
                    <button class="btn btn-danger" onclick="restartApp()">Restart Application</button>
                </div>
            </div>

            <script>
                const basePath = window.location.pathname.replace(/\/panel$/, '');
                const apiKey = new URLSearchParams(window.location.search).get('key') || '';

                function headers() {
                    const h = { 'Accept': 'application/json' };
                    if (apiKey) h['X-Maintenance-Key'] = apiKey;
                    return h;
                }

                async function refreshStatus() {
                    try {
                        const res = await fetch(basePath, { headers: headers() });
                        const data = await res.json();
                        document.getElementById('reason').textContent = data.reason || '—';
                        document.getElementById('since').textContent = data.activatedAt ? new Date(data.activatedAt).toLocaleString() : '—';
                        document.getElementById('eta').textContent = data.estimatedEnd ? new Date(data.estimatedEnd).toLocaleString() : '—';

                        const badge = document.getElementById('statusBadge');
                        if (data.isActive) {
                            badge.textContent = 'ACTIVE'; badge.className = 'status-badge status-active';
                        } else {
                            badge.textContent = 'OK'; badge.className = 'status-badge status-ok';
                        }

                        if (data.progress) {
                            data.progress.forEach(addLogEntry);
                        }
                    } catch (e) { console.error('Status fetch failed', e); }
                }

                function addLogEntry(evt) {
                    const log = document.getElementById('log');
                    const el = document.createElement('div');
                    el.className = 'log-entry' + (evt.isError ? ' error' : '');

                    // Use textContent / createTextNode to avoid XSS from SSE data
                    const timeSpan = document.createElement('span');
                    timeSpan.className = 'time';
                    timeSpan.textContent = evt.timestamp ? new Date(evt.timestamp).toLocaleTimeString() : '';

                    const dbSpan = document.createElement('span');
                    dbSpan.className = 'db';
                    dbSpan.textContent = evt.databaseName ? `[${evt.databaseName}]` : '';

                    const msgNode = document.createTextNode(evt.message || '');

                    el.appendChild(timeSpan);
                    el.appendChild(dbSpan);
                    el.appendChild(msgNode);
                    log.appendChild(el);
                    log.scrollTop = log.scrollHeight;

                    if (evt.progressPercent != null) {
                        document.getElementById('progressFill').style.width = evt.progressPercent + '%';
                    }
                }

                function connectSSE() {
                    const url = basePath + '/stream' + (apiKey ? '?key=' + encodeURIComponent(apiKey) : '');
                    const es = new EventSource(url);
                    const sseEl = document.getElementById('sseStatus');

                    es.onopen = () => { sseEl.textContent = 'Connected'; sseEl.className = 'connected'; };
                    es.onerror = () => { sseEl.textContent = 'Disconnected'; sseEl.className = 'disconnected'; };

                    es.addEventListener('progress', e => addLogEntry(JSON.parse(e.data)));
                    es.addEventListener('error', e => addLogEntry(JSON.parse(e.data)));
                    es.addEventListener('complete', e => {
                        addLogEntry(JSON.parse(e.data));
                        refreshStatus();
                    });
                }

                async function restartApp() {
                    if (!confirm('Restart the application?')) return;
                    try {
                        await fetch(basePath + '/restart', { method: 'POST', headers: headers() });
                    } catch (e) { /* expected — app is stopping */ }
                }

                refreshStatus();
                connectSSE();
            </script>
        </body>
        </html>
        """;
}
