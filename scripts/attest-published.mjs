/**
 * Fetches every package of a release as nuget.org serves it and checks it is the package the release
 * built, with nuget.org's repository signature added and nothing else changed. The files it writes are
 * what `attest-published.yml` attests.
 *
 * Why: the release attests the packages it builds, before the push. nuget.org then adds
 * `.signature.p7s` inside each .nupkg, so the file anyone downloads has another digest, and the release's
 * attestation cannot be found for it. Attesting the served file closes that gap, and comparing it to the
 * build first means the attestation vouches for a file that is the build plus the signature.
 *
 *   node scripts/attest-published.mjs --built <dir of .nupkg> --out <dir> [--timeout-minutes 60]
 *
 * Exit 1 on any difference, or when a package is still not served at the deadline.
 */
import { createHash } from 'node:crypto';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { zipEntries } from './lib/zip-entries.mjs';

export const SIGNATURE = '.signature.p7s';

/** The id and version a package declares, from its .nuspec. */
export function packageIdentity(entries) {
  const nuspecName = [...entries.keys()].find((n) => !n.includes('/') && n.endsWith('.nuspec'));
  if (!nuspecName) throw new Error('no .nuspec at the root of the package');
  const nuspec = entries.get(nuspecName).toString('utf8');
  const id = /<id>([^<]+)<\/id>/.exec(nuspec)?.[1];
  const version = /<version>([^<]+)<\/version>/.exec(nuspec)?.[1];
  if (!id || !version) throw new Error(`${nuspecName} names no id or version`);
  return { id, version };
}

export function flatContainerUrl({ id, version }) {
  const i = id.toLowerCase();
  const v = version.toLowerCase();
  return `https://api.nuget.org/v3-flatcontainer/${i}/${v}/${i}.${v}.nupkg`;
}

/**
 * Every way the served package differs from the built one, other than nuget.org's signature.
 * @param {Map<string, Buffer>} built
 * @param {Map<string, Buffer>} served
 * @returns {string[]}
 */
export function differences(built, served) {
  const digest = (b) => createHash('sha256').update(b).digest('hex');
  const problems = [];
  for (const [name, content] of built) {
    if (!served.has(name)) problems.push(`${name} is missing from the served package`);
    else if (digest(served.get(name)) !== digest(content)) problems.push(`${name} differs from the build`);
  }
  for (const name of served.keys()) {
    if (!built.has(name) && name !== SIGNATURE) problems.push(`${name} is in the served package, not in the build`);
  }
  return problems;
}

function argument(args, name, fallback) {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : fallback;
}

async function main(args) {
  const builtDir = argument(args, '--built');
  const outDir = argument(args, '--out');
  const deadline = Date.now() + Number(argument(args, '--timeout-minutes', '60')) * 60_000;
  if (!builtDir || !outDir) {
    console.error('usage: node scripts/attest-published.mjs --built <dir> --out <dir> [--timeout-minutes N]');
    process.exit(2);
  }
  mkdirSync(outDir, { recursive: true });

  const pending = readdirSync(builtDir)
    .filter((f) => f.endsWith('.nupkg') && !f.endsWith('.snupkg'))
    .map((file) => {
      const entries = zipEntries(readFileSync(join(builtDir, file)));
      return { file, entries, identity: packageIdentity(entries) };
    });
  const total = pending.length;
  if (total === 0) {
    console.error(`no .nupkg in ${builtDir}`);
    process.exit(1);
  }
  let signed = 0;

  // nuget.org says nothing when a package has been validated, signed and published: the only way to
  // learn it is to ask. Each round asks for every package still missing, then waits before the next.
  for (;;) {
    for (const pkg of [...pending]) {
      const response = await fetch(flatContainerUrl(pkg.identity));
      if (response.status === 404) continue;
      if (!response.ok) {
        console.log(`  ${pkg.identity.id}: HTTP ${response.status}, asking again next round`);
        continue;
      }
      const bytes = Buffer.from(await response.arrayBuffer());
      const served = zipEntries(bytes);
      const problems = differences(pkg.entries, served);
      if (problems.length > 0) {
        console.error(`FAIL ${pkg.identity.id} ${pkg.identity.version} as served by nuget.org is not the build:`);
        for (const p of problems) console.error(`  ${p}`);
        process.exit(1);
      }
      if (served.has(SIGNATURE)) signed++;
      writeFileSync(join(outDir, pkg.file), bytes);
      pending.splice(pending.indexOf(pkg), 1);
    }
    if (pending.length === 0) break;
    if (Date.now() > deadline) {
      console.error(`FAIL ${pending.length} of ${total} packages are still not served by nuget.org:`);
      for (const pkg of pending) console.error(`  ${pkg.identity.id} ${pkg.identity.version}`);
      process.exit(1);
    }
    console.log(`  ${total - pending.length}/${total} served; waiting for ${pending.length}`);
    await new Promise((r) => setTimeout(r, 30_000));
  }

  console.log(`${total} packages served by nuget.org, each the build plus nothing but nuget.org's signature `
    + `(${signed} signed) → ${outDir}`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main(process.argv.slice(2));
}
