/**
 * The packages a build wrote, by id.
 *
 * Every project the build files do not recognise as a generator, analyzer, test, benchmark or sample is
 * a runtime library, and a runtime library packs on build: a developer tool added outside those shapes
 * would reach the feed on the next publish-local. The gate reads what its own build packed, and nothing
 * but Pragmatic.* may be there.
 *
 * Only files written since the build started count: the output folder keeps packages from builds long
 * gone.
 */
import { existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

/** `Pragmatic.Result.0.0.0-gate.nupkg` → `Pragmatic.Result`: everything before the first numeric segment. */
export function packageId(fileName) {
  return fileName.replace(/\.nupkg$/, '').replace(/\.\d+\.\d+\.\d+.*$/, '');
}

function nupkgsUnder(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) return nupkgsUnder(full);
    return name.endsWith('.nupkg') ? [full] : [];
  });
}

/** The `.nupkg` files written under `root` at or after `since` (ms since the epoch). */
export function packagesWrittenSince(root, since) {
  return nupkgsUnder(root).filter((file) => statSync(file).mtimeMs >= since);
}

/** The ids of the packages written under `root` at or after `since` (ms since the epoch). */
export function packedSince(root, since) {
  return packagesWrittenSince(root, since).map((file) => packageId(file.split(/[\\/]/).pop()));
}

/** `Pragmatic.Pdf.Native.dll`, `libPragmatic.Pdf.Native.so`, `libPragmatic.Pdf.Native.dylib` → `Pragmatic.Pdf.Native`. */
function loadName(fileName) {
  return fileName.replace(/\.(dll|so|dylib)$/, '').replace(/^lib/, '');
}

/**
 * Whether the native assets of a package can all be found by one P/Invoke name. The loader asks for
 * the same name on every platform — `X.dll` on Windows, `libX.so` / `libX.dylib` elsewhere — so a
 * package whose platforms disagree on the name has at least one platform where the library is never
 * found. The PDF package shipped its Linux asset under cargo's `libpragmatic_pdf_native.so` next to
 * `Pragmatic.Pdf.Native.dll`, and a Linux consumer got IsSupported=false. The tests never
 * saw it: a project reference copies the file under the right name.
 *
 * Returns null when the names agree (or there is nothing to compare), else a description.
 */
export function nativeNameMismatch(entryNames) {
  const byRid = new Map();
  for (const entry of entryNames) {
    const match = /^runtimes\/([^/]+)\/native\/([^/]+)$/.exec(entry);
    if (!match) continue;
    const names = byRid.get(match[1]) ?? [];
    names.push(loadName(match[2]));
    byRid.set(match[1], names);
  }
  if (byRid.size < 2) return null;

  const signatures = [...byRid].map(([rid, names]) => [rid, [...names].sort().join(', ')]);
  const first = signatures[0][1];
  if (signatures.every(([, s]) => s === first)) return null;
  return signatures.map(([rid, s]) => `${rid}: ${s}`).join(' · ');
}
