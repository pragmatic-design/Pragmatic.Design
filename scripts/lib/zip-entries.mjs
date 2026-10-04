/**
 * Reads every entry of a zip archive held in memory, by name, decompressed. A .nupkg is a zip; reading it
 * here instead of through `unzip` keeps names such as `[Content_Types].xml` from being taken for
 * patterns, and needs nothing beyond Node.
 *
 * Supports what NuGet writes: stored and deflated entries, no ZIP64, no encryption.
 */
import { inflateRawSync } from 'node:zlib';

const EOCD = 0x06054b50;
const CENTRAL = 0x02014b50;
const LOCAL = 0x04034b50;

/** @returns {Map<string, Buffer>} */
export function zipEntries(buffer) {
  // The end-of-central-directory record is the last 22 bytes, followed by a comment of up to 64 KiB.
  let eocd = -1;
  for (let i = buffer.length - 22; i >= Math.max(0, buffer.length - 22 - 0xffff); i--) {
    if (buffer.readUInt32LE(i) === EOCD) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('not a zip archive: no end-of-central-directory record');

  const count = buffer.readUInt16LE(eocd + 10);
  let offset = buffer.readUInt32LE(eocd + 16);
  const entries = new Map();

  for (let n = 0; n < count; n++) {
    if (buffer.readUInt32LE(offset) !== CENTRAL) throw new Error(`bad central directory header at ${offset}`);
    const method = buffer.readUInt16LE(offset + 10);
    const compressedSize = buffer.readUInt32LE(offset + 20);
    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    const localOffset = buffer.readUInt32LE(offset + 42);
    const name = buffer.toString('utf8', offset + 46, offset + 46 + nameLength);
    offset += 46 + nameLength + extraLength + commentLength;

    if (buffer.readUInt32LE(localOffset) !== LOCAL) throw new Error(`bad local header for ${name}`);
    const start = localOffset + 30 + buffer.readUInt16LE(localOffset + 26) + buffer.readUInt16LE(localOffset + 28);
    const data = buffer.subarray(start, start + compressedSize);

    if (method === 0) entries.set(name, Buffer.from(data));
    else if (method === 8) entries.set(name, inflateRawSync(data));
    else throw new Error(`${name}: unsupported compression method ${method}`);
  }
  return entries;
}
