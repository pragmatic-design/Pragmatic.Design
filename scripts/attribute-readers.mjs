/**
 * Which declared attributes nothing reads, and which of their properties nothing names.
 *
 *   node scripts/attribute-readers.mjs            the counts and every name behind them
 *   node scripts/attribute-readers.mjs --why      with what reads each of the others
 *
 * The gate carries the two numbers (`attributeReaderRatchet()` in check.mjs); this prints the names,
 * which is what a story about one of them needs. What counts as read, and the two blind spots left in
 * deliberately, are documented in `scripts/lib/attribute-readers.mjs`.
 */
import { measureAttributeReaders } from './lib/attribute-readers.mjs';

const why = process.argv.includes('--why');
const m = measureAttributeReaders({ repoRoot: '.' });

console.log(`attributes ${m.total}   read by something ${m.total - m.unread.length}`
  + `   read by nobody ${m.unread.length}   declared properties nothing names ${m.unreadProperties.length}`);
console.log();

if (m.unread.length > 0) {
  console.log('read by nobody — declared, and nothing but a shape check names it');
  for (const r of [...m.unread].sort((a, b) => a.module.localeCompare(b.module) || a.attribute.localeCompare(b.attribute)))
    console.log(`  ${r.module.padEnd(38)} ${r.attribute}`);
  console.log();
}

if (m.unreadProperties.length > 0) {
  console.log('properties nothing names — the type is read, this member of it is not');
  for (const p of [...m.unreadProperties].sort()) console.log(`  ${p}`);
  console.log();
}

if (why)
  for (const r of m.rows.filter((x) => x.readBy.length > 0)
    .sort((a, b) => a.attribute.localeCompare(b.attribute)))
    console.log(`  ${r.attribute.padEnd(46)} ${r.readBy.join(', ')}`);
