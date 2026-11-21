/**
 * Every package ships a build for each target framework an application can have (#931).
 *
 *   node scripts/package-targets.mjs <folder of .nupkg>     exit 1, naming each package, when one is missing a target
 *
 * A package with a `lib/` folder (a runtime library) or a `tools/` folder (a .NET tool) must carry `lib/<tfm>/` or
 * `tools/<tfm>/` for every framework in REQUIRED_TARGETS. A package with neither, an analyzer or a source generator
 * that runs in the compiler, has nothing to check, nor has one whose only build is netstandard. Read from the packed
 * files, not the project files: the package is what a consumer restores, and a project-level <TargetFramework>
 * silently narrows what MSBuild packs.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { zipEntries } from './lib/zip-entries.mjs';

export const REQUIRED_TARGETS = ['net10.0', 'net11.0'];

/**
 * What is wrong with the packages' targets, one line per package. Empty when every package carries every target.
 *
 * @param {Map<string, string[]>} packages package file name → the entry names inside it
 * @param {string[]} [required]
 * @returns {string[]}
 */
export function targetProblems(packages, required = REQUIRED_TARGETS) {
  const problems = [];
  for (const [name, entries] of packages) {
    for (const root of ['lib', 'tools']) {
      const present = new Set(entries.filter((e) => e.startsWith(`${root}/`)).map((e) => e.split('/')[1]));
      if (present.size === 0) continue;
      // A netstandard build (the code fixers ship one) loads on any runtime: nothing to add for it.
      if ([...present].every((tfm) => tfm.startsWith('netstandard'))) continue;
      const missing = required.filter((tfm) => !present.has(tfm));
      if (missing.length > 0) problems.push(`${name}: ${root}/ has ${[...present].sort().join(', ')}, not ${missing.join(', ')}`);
    }
  }
  return problems;
}

/** The problems of every .nupkg (not .symbols.nupkg) in `folder`, read from the files; `checked` is how many. */
export function folderTargetProblems(folder) {
  const files = readdirSync(folder).filter((f) => f.endsWith('.nupkg') && !f.endsWith('.symbols.nupkg'));
  const packages = new Map(files.map((f) => [f, [...zipEntries(readFileSync(join(folder, f))).keys()]]));
  return { checked: files.length, problems: targetProblems(packages) };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const folder = process.argv[2];
  if (!folder) {
    console.error('usage: node scripts/package-targets.mjs <folder of .nupkg>');
    process.exit(2);
  }
  const { checked, problems } = folderTargetProblems(folder);
  if (checked === 0) {
    console.error(`FAIL no .nupkg in ${folder}: nothing was checked`);
    process.exit(1);
  }
  for (const p of problems) console.log(`  ${p}`);
  if (problems.length > 0) {
    console.log(`FAIL ${problems.length} of ${checked} packages miss a target of ${REQUIRED_TARGETS.join(', ')}`);
    process.exit(1);
  }
  console.log(`${checked} packages, every one on ${REQUIRED_TARGETS.join(' and ')} where it ships a build`);
}
