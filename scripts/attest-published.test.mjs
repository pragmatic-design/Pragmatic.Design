/**
 * What `attest-published.mjs` accepts as "the build, as nuget.org serves it", and what it refuses.
 *
 *   node --test scripts/attest-published.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deflateRawSync } from 'node:zlib';
import { SIGNATURE, differences, flatContainerUrl, packageIdentity } from './attest-published.mjs';
import { zipEntries } from './lib/zip-entries.mjs';

const entries = (o) => new Map(Object.entries(o).map(([k, v]) => [k, Buffer.from(v)]));
const built = () => entries({ 'Pkg.nuspec': '<id>Pkg</id><version>1.0.0-alpha.1</version>', 'lib/a.dll': 'IL' });

test('the build plus nuget.org signature is accepted', () => {
  const served = built();
  served.set(SIGNATURE, Buffer.from('pkcs7'));
  assert.deepEqual(differences(built(), served), []);
});

test('the build without a signature is accepted', () => {
  assert.deepEqual(differences(built(), built()), []);
});

test('a changed entry is refused', () => {
  const served = built();
  served.set('lib/a.dll', Buffer.from('other IL'));
  assert.deepEqual(differences(built(), served), ['lib/a.dll differs from the build']);
});

test('a missing entry is refused', () => {
  const served = built();
  served.delete('lib/a.dll');
  assert.deepEqual(differences(built(), served), ['lib/a.dll is missing from the served package']);
});

test('an added entry other than the signature is refused', () => {
  const served = built();
  served.set('tools/install.ps1', Buffer.from('script'));
  assert.deepEqual(differences(built(), served), ['tools/install.ps1 is in the served package, not in the build']);
});

test('the identity comes from the nuspec, and the URL is lowercase', () => {
  const identity = packageIdentity(built());
  assert.deepEqual(identity, { id: 'Pkg', version: '1.0.0-alpha.1' });
  assert.equal(flatContainerUrl(identity), 'https://api.nuget.org/v3-flatcontainer/pkg/1.0.0-alpha.1/pkg.1.0.0-alpha.1.nupkg');
});

/** A minimal zip with one stored and one deflated entry, one of them named like a glob pattern. */
function zip(files) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, content, deflate] of files) {
    const nameBytes = Buffer.from(name);
    const data = deflate ? deflateRawSync(content) : content;
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(deflate ? 8 : 0, 8);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(content.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(deflate ? 8 : 0, 10);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(content.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    centrals.push(central, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(files.length, 8);
  end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

test('zip entries are read by exact name, stored or deflated', () => {
  const archive = zip([
    ['[Content_Types].xml', Buffer.from('<Types/>'), false],
    ['lib/a.dll', Buffer.from('IL'.repeat(50)), true],
  ]);
  const read = zipEntries(archive);
  assert.equal(read.get('[Content_Types].xml').toString(), '<Types/>');
  assert.equal(read.get('lib/a.dll').toString(), 'IL'.repeat(50));
});
