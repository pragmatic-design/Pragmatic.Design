/**
 * The native source stamp check, on trees and binaries built here.
 *
 * ⚠️ What this file does and does not cover. It proves the hash follows the source and not the
 * checkout (line endings), that a binary is judged by the stamp of its own crate, and that a source two
 * crates share makes both stale. It does not build a library: that the Rust builds write the stamp is
 * proven by the gate itself, which reads it out of the committed binaries in each module's runtimes/.
 *
 *   node --test scripts/native-stamp.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { CRATES, ROOT, sourceHash, stampMismatch, stampOf, staleBinaries } from './native-stamp.mjs';

const SOURCES = ['crate/Cargo.toml', 'crate/src'];

function tree(files) {
  const root = mkdtempSync(join(tmpdir(), 'native-stamp-'));
  for (const [path, text] of Object.entries(files)) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), text);
  }
  return root;
}

const crate = {
  'crate/Cargo.toml': '[package]\nname = "x"\n',
  'crate/src/lib.rs': 'mod a;\nfn main() {}\n',
  'crate/src/a/b.rs': 'pub fn b() {}\n',
  'shared/src/lib.rs': 'pub fn shared() {}\n',
};

const stamped = (env, hash) => Buffer.concat([Buffer.from([0x7f, 0x45, 0x4c, 0x46, 0]), Buffer.from(`${env}:${hash}\0`, 'latin1')]);

const spec = (name, env, sources, binaries) => ({ name, env, sources, binaries });

test('the hash is the same whether the checkout wrote CRLF or LF', () => {
  const lf = tree(crate);
  const crlf = tree(Object.fromEntries(Object.entries(crate).map(([p, t]) => [p, t.replace(/\n/g, '\r\n')])));

  assert.equal(sourceHash(crlf, SOURCES), sourceHash(lf, SOURCES));
});

test('a changed line changes the hash', () => {
  const before = tree(crate);
  const after = tree({ ...crate, 'crate/src/a/b.rs': 'pub fn b() { }\n' });

  assert.notEqual(sourceHash(after, SOURCES), sourceHash(before, SOURCES));
});

test('a new source file changes the hash', () => {
  const before = tree(crate);
  const after = tree({ ...crate, 'crate/src/a/c.rs': '' });

  assert.notEqual(sourceHash(after, SOURCES), sourceHash(before, SOURCES));
});

test('a file outside the listed sources does not change the hash', () => {
  const before = tree(crate);
  const after = tree({ ...crate, 'crate/target/release/x.dll': 'binary', 'crate/README.md': 'notes' });

  assert.equal(sourceHash(after, SOURCES), sourceHash(before, SOURCES));
});

test('the stamp is read out of the bytes by its own marker, and a binary without one has none', () => {
  const hash = 'a'.repeat(64);

  assert.equal(stampOf(stamped('PRAGMATIC_PDF_SOURCE_HASH', hash), 'PRAGMATIC_PDF_SOURCE_HASH'), hash);
  assert.equal(stampOf(stamped('PRAGMATIC_PDF_SOURCE_HASH', hash), 'PRAGMATIC_IMAGING_SOURCE_HASH'), null);
  assert.equal(stampOf(Buffer.from('no stamp here'), 'PRAGMATIC_PDF_SOURCE_HASH'), null);
});

test('a binary is stale when it carries no stamp or another hash, and absent binaries are not judged', () => {
  const root = tree(crate);
  const one = spec('one', 'ONE_SOURCE_HASH', SOURCES, ['current.dll', 'older.so', 'unstamped.so', 'absent.dylib']);
  const expected = sourceHash(root, SOURCES);
  writeFileSync(join(root, 'current.dll'), stamped('ONE_SOURCE_HASH', expected));
  writeFileSync(join(root, 'older.so'), stamped('ONE_SOURCE_HASH', 'b'.repeat(64)));
  writeFileSync(join(root, 'unstamped.so'), Buffer.from('built by hand'));

  const stale = staleBinaries(root, [one]);

  assert.deepEqual(stale.map((s) => [s.path, s.stamp]), [['older.so', 'b'.repeat(64)], ['unstamped.so', null]]);
  assert.ok(stale.every((s) => s.crate === 'one' && s.expected === expected));
});

// What a CI workflow checks a freshly built library with, instead of grep: BSD grep fails the same
// check on macOS that GNU grep passes on Linux and Windows.
test('a built library with the current stamp passes verify, with another or none it says which', () => {
  const env = 'PRAGMATIC_X_SOURCE_HASH';
  const expected = 'a'.repeat(64);
  const library = (stamp) => Buffer.concat([Buffer.from([0xff, 0xfe, 0x00]), Buffer.from(`${env}:${stamp}\0`), Buffer.from([0xc3, 0x28])]);

  assert.equal(stampMismatch(library(expected), env, expected), null);
  assert.match(stampMismatch(library('b'.repeat(64)), env, expected), /built from b{64}, source is a{64}/);
  assert.match(stampMismatch(library('unstamped'), env, expected), /carries no PRAGMATIC_X_SOURCE_HASH stamp/);
});

// Each crate's workflow builds what the gate accepts: stamped, tested, and named as runtimes/ commits it.
// It is the only source of the osx-arm64 binaries: artifacts without a stamp, placed where its own
// header says, would turn the gate red.
for (const c of CRATES) {
  const workflow = () => readFileSync(join(ROOT, c.workflow), 'utf8');

  test(`${c.workflow} stamps its build with ${c.env}`, () => {
    const text = workflow();

    assert.match(text, new RegExp(`hash=\\$\\(node scripts/native-stamp\\.mjs hash ${c.name}\\)`));
    assert.match(text, new RegExp(`echo "${c.env}=\\$hash" >> "\\$GITHUB_ENV"`));
    assert.match(text, /cargo build --release --locked/);
    // And the run reads the stamp back out of the library it built, with the gate's own reader.
    assert.match(text, new RegExp(`native-stamp\\.mjs" verify ${c.name} "target/release/`));
    assert.doesNotMatch(text, /grep -aq/, 'grep is not the gate\'s reader, and BSD grep missed the stamp on macOS');
  });

  test(`${c.workflow} runs the crate's unit tests before it builds`, () => {
    const text = workflow();
    const tests = text.indexOf('cargo test --release --locked');

    assert.ok(tests >= 0, 'cargo test --release --locked is missing');
    assert.ok(tests < text.indexOf('cargo build --release --locked'), 'the tests run after the build');
  });

  test(`${c.workflow} names each artifact as runtimes/ commits it`, () => {
    const matrix = [...workflow().matchAll(/rid:\s*(\S+)\s+artifact:\s*(\S+)\s+target_name:\s*(\S+)/g)]
      .map(([, rid, artifact, target]) => ({ rid, artifact, target }));

    assert.deepEqual(
      matrix.map((m) => [m.rid, m.artifact, m.target]).sort(),
      c.targets.map((t) => [t.rid, t.built, basename(t.path)]).sort());
  });
}

test('a change to a source two crates share makes both stale', () => {
  const sourcesOf = (dir) => [`${dir}/src`, 'shared/src'];
  const files = { ...crate, 'other/src/lib.rs': 'fn other() {}\n' };
  const one = spec('one', 'ONE_SOURCE_HASH', sourcesOf('crate'), ['one.dll']);
  const two = spec('two', 'TWO_SOURCE_HASH', sourcesOf('other'), ['two.dll']);

  const root = tree(files);
  writeFileSync(join(root, 'one.dll'), stamped('ONE_SOURCE_HASH', sourceHash(root, one.sources)));
  writeFileSync(join(root, 'two.dll'), stamped('TWO_SOURCE_HASH', sourceHash(root, two.sources)));
  assert.deepEqual(staleBinaries(root, [one, two]), []);

  writeFileSync(join(root, 'shared/src/lib.rs'), 'pub fn shared() { }\n');
  assert.deepEqual(staleBinaries(root, [one, two]).map((s) => s.crate), ['one', 'two']);
});
