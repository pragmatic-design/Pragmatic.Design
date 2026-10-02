/**
 * What a reader outside the maintainers cannot open: a tracker key, a review tag, a section of the
 * internal rulebook. Read by the publication scan (files) and the commit-message check (history), so
 * the two refuse the same thing.
 *
 * Named prefixes rather than any "ABC-123": SHA-256, AES-256 and ORD-001 share that shape.
 */
export const TRACKER_REFERENCE =
  /\bPRAG-\d+\b|\b(?:ABS|ACT|AGENT|CFG|COMP|DEC|DOC|END|FF|IDN|IMG|LOG|MAP|MIG|MSG|PER|PERSIST|REL|SEC|SITE|SMP|STO|TEMP)-(?:[A-Z]-)?[A-Z0-9]*\d[A-Z0-9]*(?:-[A-Z0-9]+)*\b|#[A-Z]\d{1,2}(?:-\d+)?\b|\b[Ss]tory \d+\b|§(?:1\.3-bis|5\.2-bis|5\.3|2\.9|2\.8)\b/;

/** Lines that match the shape and are not a reference. */
export const TRACKER_REFERENCE_IGNORE = /END-TO-END|RFC \d/;
