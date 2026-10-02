/**
 * The check that only Pragmatic packages come out of the build, on package folders built here.
 *
 * ⚠️ What this file does and does not cover. It proves the check reads package ids from file names,
 * judges only what the current build wrote (the output folder keeps packages from older builds), and
 * refuses to pass when the build wrote nothing. That the build packs at all is the gate's own run.
 *
 *   node --test scripts/packed-packages.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { nativeNameMismatch, packageId, packedSince } from './lib/packed-packages.mjs';

function folder(files) {
  const root = mkdtempSync(join(tmpdir(), 'packed-'));
  const dir = join(root, 'package', 'debug');
  mkdirSync(dir, { recursive: true });
  for (const [name, when] of Object.entries(files)) {
    writeFileSync(join(dir, name), 'nupkg');
    utimesSync(join(dir, name), when / 1000, when / 1000);
  }
  return root;
}

const buildStart = Date.parse('2026-09-10T17:00:00Z');
const during = buildStart + 60_000;
const before = buildStart - 86_400_000;

test('the id is the file name without the version', () => {
  assert.equal(packageId('Pragmatic.Result.0.0.0-gate.nupkg'), 'Pragmatic.Result');
  assert.equal(packageId('SomeDeveloperTool.1.0.0-alpha.0.4078.nupkg'), 'SomeDeveloperTool');
  assert.equal(packageId('Pragmatic.Result.EFCore.SqlServer.1.0.0-alpha.0.4078.nupkg'), 'Pragmatic.Result.EFCore.SqlServer');
});

test('only packages the current build wrote are judged, and symbol packages are not packages', () => {
  const root = folder({
    'Pragmatic.Result.0.0.0-gate.nupkg': during,
    'Pragmatic.Result.0.0.0-gate.snupkg': during,
    'Conformance.Host.0.0.0-gate.nupkg': before,
    'SomeDeveloperTool.0.0.0-gate.nupkg': during,
  });

  assert.deepEqual(packedSince(root, buildStart).sort(), ['Pragmatic.Result', 'SomeDeveloperTool']);
});

test('a build that wrote no package yields none, so the caller can refuse to call that a pass', () => {
  const root = folder({ 'Pragmatic.Result.0.0.0-gate.nupkg': before });

  assert.deepEqual(packedSince(root, buildStart), []);
});

// The loader looks for one name on every platform: X.dll on Windows, libX.so / libX.dylib elsewhere.
test('native assets that share one name across platforms pass', () => {
  assert.equal(nativeNameMismatch([
    'lib/net10.0/Pragmatic.Imaging.dll',
    'runtimes/win-x64/native/Pragmatic.Imaging.Native.dll',
    'runtimes/linux-x64/native/libPragmatic.Imaging.Native.so',
    'runtimes/osx-arm64/native/libPragmatic.Imaging.Native.dylib',
  ]), null);
});

test('a Linux asset under its cargo name is named, next to the Windows one', () => {
  // A Linux asset under its cargo name: a Linux consumer gets IsSupported=false.
  const mismatch = nativeNameMismatch([
    'runtimes/linux-x64/native/libpragmatic_pdf_native.so',
    'runtimes/win-x64/native/Pragmatic.Pdf.Native.dll',
  ]);

  assert.ok(mismatch?.includes('pragmatic_pdf_native') && mismatch.includes('Pragmatic.Pdf.Native'), mismatch);
});

test('a package with no native asset, or with one platform only, has nothing to compare', () => {
  assert.equal(nativeNameMismatch(['lib/net10.0/Pragmatic.Result.dll', 'README.md']), null);
  assert.equal(nativeNameMismatch(['runtimes/win-x64/native/Pragmatic.Pdf.Native.dll']), null);
});
