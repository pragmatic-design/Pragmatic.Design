#!/usr/bin/env node
/**
 * commit-message-check — what the public history accepts in a commit message.
 *
 * - a Conventional Commits header: `type(scope)!: description`, at most 72 characters, the
 *   description starting in lowercase;
 * - no tracker key, review tag or rulebook section (scripts/lib/tracker-reference.mjs): the reader
 *   cannot open what they point at;
 * - no session link (`Claude-Session:`) and no path from a maintainer's machine.
 *
 * Lines starting with `#` are skipped, as git strips them; messages git writes itself (Revert,
 * fixup!, squash!) pass.
 *
 *   node scripts/commit-message-check.mjs <message file>          the commit-msg hook
 *   node scripts/commit-message-check.mjs --range <base>..<head>   every commit in a range (CI)
 *   node scripts/commit-message-check.mjs --text "<message>"       one message, e.g. a PR title
 *
 * Exit code: 0 accepted, 1 refused.
 */
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { TRACKER_REFERENCE, TRACKER_REFERENCE_IGNORE } from './lib/tracker-reference.mjs';

const TYPES = ['feat', 'fix', 'docs', 'test', 'refactor', 'perf', 'build', 'ci', 'chore', 'style', 'revert'];
const HEADER = new RegExp(`^(${TYPES.join('|')})(\\([a-z0-9][a-z0-9-]*\\))?!?: (.+)$`);
const GENERATED = /^(Revert "|fixup! |squash! |amend! )/;
const MAX_HEADER = 72;

/** The problems of one commit message. Empty when the public history accepts it. */
export function messageProblems(message) {
  const lines = message.split(/\r?\n/).filter((l) => !l.startsWith('#'));
  const header = lines[0] ?? '';
  const problems = [];
  if (GENERATED.test(header)) return problems;

  const m = HEADER.exec(header);
  if (!m) problems.push(`the header is not "type(scope): description" with type one of ${TYPES.join(', ')}`);
  else if (/^[A-Z]/.test(m[3])) problems.push('the description starts in lowercase: "fix: handle…", not "fix: Handle…"');
  if (header.length > MAX_HEADER) problems.push(`the header is ${header.length} characters; at most ${MAX_HEADER}`);

  for (const line of lines) {
    if (TRACKER_REFERENCE.test(line) && !TRACKER_REFERENCE_IGNORE.test(line))
      problems.push(`a tracker key or internal reference a reader cannot open: "${line.trim()}"`);
    if (/^Claude-Session:/i.test(line.trim()))
      problems.push('a session link: the public history carries no trailer of that kind');
    if (/[A-Z]:\\Users\\|\/home\/[a-z][\w.-]*\//.test(line))
      problems.push(`a local path from someone's machine: "${line.trim()}"`);
  }
  return problems;
}

function main() {
  const args = process.argv.slice(2);
  let messages;
  if (args[0] === '--range') {
    const out = execFileSync('git', ['log', '--format=%H%n%B%x00', args[1]], { encoding: 'utf8' });
    messages = out.split('\0').map((s) => s.trim()).filter(Boolean).map((s) => {
      const [sha, ...body] = s.split('\n');
      return { label: sha.slice(0, 9), text: body.join('\n') };
    });
  } else if (args[0] === '--text') {
    messages = [{ label: 'message', text: args[1] ?? '' }];
  } else if (args[0]) {
    messages = [{ label: 'commit message', text: readFileSync(args[0], 'utf8') }];
  } else {
    console.error('usage: commit-message-check.mjs <file> | --range <base>..<head> | --text "<message>"');
    process.exit(1);
  }

  let refused = 0;
  for (const { label, text } of messages) {
    const problems = messageProblems(text);
    if (!problems.length) continue;
    refused++;
    console.error(`${label}: ${text.split(/\r?\n/)[0]}`);
    for (const p of problems) console.error(`  - ${p}`);
  }
  if (refused) {
    console.error('\nCommit message rules: CONTRIBUTING.md, "Commit messages".');
    process.exit(1);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
