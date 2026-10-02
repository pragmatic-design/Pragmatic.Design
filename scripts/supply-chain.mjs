/**
 * Supply-chain checks for the gate: a CycloneDX SBOM per shipped package, and a scan for known
 * vulnerabilities in everything we depend on.
 *
 * Why this lives in the gate rather than in a CI step of its own: a check that runs somewhere else
 * drifts from the one developers run, and the first time it disagrees the CI one is assumed broken.
 *
 * Why no new dependency: `dotnet list package` already resolves the full transitive graph and already
 * queries the NuGet vulnerability database. The usual CycloneDX tool would add a build-time dependency
 * to a repo whose entire point is not having them, to produce a document we can emit from data the SDK
 * hands us.
 *
 * THE ONE THING THAT MATTERS HERE: a scan that could not run must never look like a scan that found
 * nothing. Offline, a restore failure, a NuGet outage — each returns zero findings, and zero findings
 * is exactly what a clean repo returns. They are reported apart, and only one of them is a pass.
 */

import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';

const SBOM_DIR = 'artifacts/sbom';
const CACHE_FILE = 'artifacts/supply-chain.cache.json';

/**
 *     How long a clean vulnerability scan may be reused while the dependency graph is unchanged.
 *
 * The SBOM is a pure function of the graph: same input, same document, cacheable without expiry. The
 * scan is not - the advisory database moves under a graph that has not. So the SBOM is keyed on
 * content alone and the scan is keyed on content AND age, and this is the age.
 *
 * ⚠️ Twelve hours is a working day, which is the unit that matters here: a developer who starts in
 * the morning pays one scan and reuses it until they stop, and one who comes back the next day pays
 * another. Shorter and every second gate run of a session pays ~165s for an answer the last
 * dependency change already gave; longer and a session can outrun an advisory published while it was
 * open.
 *
 * ⚠️ It bounds a LOCAL window only. `artifacts/` is gitignored and no workflow caches it, so CI has
 * no cached verdict and always scans. What CI does not do is run on a schedule - `ci.yml` triggers on
 * push to main and on pull requests - so a repository nobody touches is not scanned at all, and no
 * value of this constant changes that.
 */
export const SCAN_MAX_AGE_MS = 12 * 60 * 60 * 1000;

/**
 * Whether a cached clean verdict may stand, and why not when it may not.
 *
 * Separate from the printing and from `dotnet` so it can be measured: the age check was written and
 * never exercised, and a rule nothing runs is one refactor from being gone.
 *
 * ⚠️ The age comparison is written as `!(age < max)` on purpose. A cache from before this check
 * existed carries no `scannedAt`, and a truncated one carries something `Date.parse` reads as `NaN`;
 * both comparisons are false whichever way round they are written, so the form decides whether
 * "unknown age" means rescan or reuse. It means rescan.
 */
export function scanDecision({ sameGraph, scannedAt, now = Date.now(), maxAgeMs = SCAN_MAX_AGE_MS }) {
  if (!sameGraph) return { reuse: false, why: 'the dependency graph moved' };

  const ageMs = scannedAt ? now - Date.parse(scannedAt) : Number.NaN;
  if (!(ageMs < maxAgeMs))
    return { reuse: false, ageMs, why: `the cached verdict is older than ${hours(maxAgeMs)}, or undated` };

  return { reuse: true, ageMs };
}

const hours = (ms) => `${Math.round(ms / (60 * 60 * 1000))}h`;

/**
 * What the gate prints for a clean result — saying which of the two it is.
 *
 * ⚠️ Not `unchanged` for a reused verdict, which describes the graph and reads as a verdict, beside a
 * bare `none` for a fresh one: a reader cannot tell whether the gate has just looked or is
 * repeating itself, which is the same defect one level up from the cache.
 */
export function cleanScanLine(decision) {
  if (!decision.reuse) return { label: 'scanned', detail: 'none' };

  const minutes = Math.round(decision.ageMs / 60000);
  return {
    label: 'reused',
    detail: `none, from a scan ${minutes} min ago on the same dependency graph — rescans after ${hours(SCAN_MAX_AGE_MS)}`,
  };
}

/** The files that decide the dependency graph. Anything else cannot change what these checks answer. */
function dependencyFingerprint() {
  const hash = createHash('sha256');
  const add = (file) => {
    if (!existsSync(file)) return;
    hash.update(file.split(String.fromCharCode(92)).join('/'));
    hash.update(readFileSync(file));
  };

  for (const root of ['Directory.Packages.props', 'Directory.Build.props', 'Directory.Build.targets',
    'NuGet.config', 'nuget.config', 'global.json']) add(root);

  const skip = new Set(['bin', 'obj', 'artifacts', 'node_modules', '.git', '.agentflow', 'TestResults']);
  const walk = (dir) => {
    const found = [];
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (entry.name.startsWith('.') && entry.name !== '.') continue;
      const full = join(dir, entry.name);
      if (entry.isDirectory()) {
        if (skip.has(entry.name)) continue;
        found.push(...walk(full));
      } else if (entry.name.endsWith('.csproj')) found.push(full);
    }
    return found;
  };

  for (const csproj of walk('.').sort()) add(csproj);
  return hash.digest('hex');
}

function readCache() {
  try {
    return JSON.parse(readFileSync(CACHE_FILE, 'utf8'));
  } catch {
    return null;
  }
}

/** The SBOM may be skipped only while the documents it would have written are still on disk. */
function sbomsStillOnDisk() {
  try {
    return statSync(SBOM_DIR).isDirectory() && readdirSync(SBOM_DIR).some((f) => f.endsWith('.cdx.json'));
  } catch {
    return false;
  }
}

function listPackages(extraArgs) {
  const r = spawnSync(
    'dotnet',
    ['list', 'package', '--include-transitive', '--format', 'json', ...extraArgs],
    { encoding: 'utf8', shell: process.platform === 'win32', maxBuffer: 256 * 1024 * 1024 },
  );

  if (r.error) return { ok: false, reason: String(r.error) };
  if (r.status !== 0) return { ok: false, reason: `dotnet list package exited ${r.status}` };

  // The command prints restore diagnostics before the JSON, so parse from the first brace.
  const text = r.stdout ?? '';
  const start = text.indexOf('{');
  if (start < 0) return { ok: false, reason: 'no JSON in output' };

  try {
    return { ok: true, data: JSON.parse(text.slice(start)) };
  } catch (e) {
    return { ok: false, reason: `unparseable JSON: ${e.message}` };
  }
}

/** Every package a project resolves, top-level and transitive, deduplicated by name@version. */
function componentsOf(project) {
  const seen = new Map();
  for (const fw of project.frameworks ?? []) {
    for (const p of [...(fw.topLevelPackages ?? []), ...(fw.transitivePackages ?? [])]) {
      const version = p.resolvedVersion ?? p.requestedVersion;
      if (!p.id || !version) continue;
      seen.set(`${p.id}@${version}`, { name: p.id, version });
    }
  }
  return [...seen.values()].sort((a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version));
}

/**
 * A UUID derived from the component set rather than a random one.
 *
 * CycloneDX wants a serial number per document. Generating it randomly — or stamping a timestamp —
 * makes every regeneration differ from the last, so the one question worth asking of a stored SBOM
 * ("did our dependencies change?") can no longer be answered by comparing two of them.
 */
function serialFrom(components) {
  const h = createHash('sha256').update(components.map((c) => `${c.name}@${c.version}`).join('\n')).digest();
  const b = Buffer.from(h.subarray(0, 16));
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  const hex = b.toString('hex');
  return `urn:uuid:${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function bom(name, components) {
  return {
    bomFormat: 'CycloneDX',
    specVersion: '1.6',
    serialNumber: serialFrom(components),
    version: 1,
    metadata: {
      component: { type: 'application', name, 'bom-ref': `root:${name}` },
      // Deliberately no `timestamp`: see serialFrom.
    },
    components: components.map((c) => ({
      type: 'library',
      name: c.name,
      version: c.version,
      purl: `pkg:nuget/${encodeURIComponent(c.name)}@${encodeURIComponent(c.version)}`,
      'bom-ref': `pkg:nuget/${c.name}@${c.version}`,
    })),
  };
}

function writeBom(path, doc) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${JSON.stringify(doc, null, 2)}\n`, 'utf8');
}

/** Projects under a `src/` folder — what this repo ships. Tests, samples and benchmarks are not. */
const isShipped = (p) => /[\\/]src[\\/]/.test(p);

function generateSboms() {
  const r = listPackages([]);
  if (!r.ok) return { ran: false, reason: r.reason };

  const projects = (r.data.projects ?? []).filter((p) => p.path && isShipped(p.path));
  const aggregate = new Map();
  let written = 0;

  for (const project of projects) {
    const components = componentsOf(project);
    if (!components.length) continue;

    for (const c of components) aggregate.set(`${c.name}@${c.version}`, c);

    const name = relative(process.cwd(), project.path).replace(/\\/g, '/').split('/').pop().replace(/\.csproj$/, '');
    writeBom(join(SBOM_DIR, `${name}.cdx.json`), bom(name, components));
    written += 1;
  }

  const all = [...aggregate.values()].sort((a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version));
  writeBom(join(SBOM_DIR, 'Pragmatic.Design.cdx.json'), bom('Pragmatic.Design', all));

  return { ran: true, packages: written, distinct: all.length };
}

function scanVulnerabilities() {
  const r = listPackages(['--vulnerable']);
  if (!r.ok) return { ran: false, reason: r.reason };

  // `frameworks` is present on a project only when that project has findings. Its absence everywhere
  // is the clean result — which is why "the command did not run" has to be a separate answer.
  const findings = [];
  for (const project of r.data.projects ?? []) {
    for (const fw of project.frameworks ?? []) {
      for (const p of [...(fw.topLevelPackages ?? []), ...(fw.transitivePackages ?? [])]) {
        for (const v of p.vulnerabilities ?? []) {
          findings.push({
            project: relative(process.cwd(), project.path).replace(/\\/g, '/'),
            package: `${p.id}@${p.resolvedVersion ?? p.requestedVersion}`,
            severity: v.severity ?? 'unknown',
            advisory: v.advisoryurl ?? '',
          });
        }
      }
    }
  }
  return { ran: true, findings };
}

/**
 * Runs both checks. Returns true only when the scan actually ran and found nothing.
 *
 * @param {{ok:(s:string)=>string, bad:(s:string)=>string, dim:(s:string)=>string}} paint
 */
export function supplyChain(paint) {
  const { ok, bad, dim } = paint;
  let green = true;

  // Two `dotnet list package` passes over the whole solution, the second over the network: measured
  // at ~165s together, which was 41% of the `full` tier and the reason a story cost twenty minutes.
  // Both answer a question about the dependency GRAPH, so an unchanged graph has an unchanged answer.
  //
  // Only a GREEN result is ever cached. A run that found something, or could not run at all, is
  // repeated - reusing "it was fine an hour ago" is how a gate starts answering from memory.
  const fingerprint = dependencyFingerprint();
  const cached = readCache();
  const sameGraph = cached?.green === true && cached.fingerprint === fingerprint;

  process.stdout.write('  SBOM (CycloneDX 1.6) … ');
  let sbom;
  if (sameGraph && sbomsStillOnDisk()) {
    sbom = { ran: true, cached: true, packages: cached.packages, distinct: cached.distinct };
    console.log(`${ok('unchanged')} ${dim(`${sbom.packages} packages, ${sbom.distinct} distinct — same dependency graph, ${SBOM_DIR}/ still current`)}`);
  } else {
    sbom = generateSboms();
    if (sbom.ran) {
      console.log(`${ok('written')} ${dim(`${sbom.packages} packages, ${sbom.distinct} distinct dependencies → ${SBOM_DIR}/`)}`);
    } else {
      console.log(bad(`could not run — ${sbom.reason}`));
      green = false;
    }
  }

  process.stdout.write('  known vulnerabilities … ');

  // The graph decides the SBOM; the graph AND the calendar decide this one.
  const decision = scanDecision({ sameGraph, scannedAt: cached?.scannedAt });
  if (decision.reuse) {
    const line = cleanScanLine(decision);
    console.log(`${ok(line.label)} ${dim(line.detail)}`);
    return green;
  }

  const scan = scanVulnerabilities();
  if (!scan.ran) {
    // Not a pass. A scan that did not run tells us nothing, and the failure mode this guards against
    // is precisely a gate that goes green because the network was down.
    console.log(bad(`could not run — ${scan.reason}`));
    return false;
  }

  if (scan.findings.length === 0) {
    const line = cleanScanLine(decision);
    console.log(`${ok(line.label)} ${dim(line.detail)}`);
    if (green) {
      try {
        mkdirSync(dirname(CACHE_FILE), { recursive: true });
        writeFileSync(CACHE_FILE, `${JSON.stringify({
          fingerprint,
          green: true,
          packages: sbom.packages,
          distinct: sbom.distinct,
          scannedAt: new Date().toISOString(),
        }, null, 2)}
`, 'utf8');
      } catch {
        // A cache that cannot be written costs the next run 165 seconds. It is not a gate failure.
      }
    }
    return green;
  }

  console.log(bad(`${scan.findings.length} found`));
  for (const f of scan.findings) {
    console.log(`    ${bad('✗')} ${f.package} ${dim(`[${f.severity}] ${f.project}`)}`);
    if (f.advisory) console.log(`      ${dim(f.advisory)}`);
  }
  return false;
}
