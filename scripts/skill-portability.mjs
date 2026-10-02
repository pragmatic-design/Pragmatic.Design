#!/usr/bin/env node
/**
 * skill-portability — every skill of the plugin reads the same in every agent that loads SKILL.md.
 *
 * The skills ship to Claude Code as a plugin and to Codex and other agents as plain Agent Skills
 * folders (https://agentskills.io/specification). Claude Code forgives what the others do not:
 *
 * - a byte-order mark before the opening `---`: Codex does not find the frontmatter and drops the
 *   skill from its list without a word — pragmatic-use-persistence was missing that way;
 * - `when_to_use`: a Claude Code field, appended to the description there and ignored everywhere
 *   else, so an agent other than Claude never learned when to trigger the skill;
 * - a description whose "when" comes last: Codex shares one listing budget among every installed skill
 *   and cuts each entry to its share — about 430 characters with forty skills installed, about 305 with
 *   fifty, shorter still with more. No length survives every setup, so the order does: a description
 *   starts with "Use when", and a cut takes the detail, not the trigger. What the skill covers in full
 *   is in its body, under the title, where nothing is cut. 300 keeps most setups whole;
 * - an unquoted value containing ": ", which is not valid YAML and fails a strict parser.
 *
 * Fields that only tune Claude Code's own UI (`argument-hint`, `shell`, `user-invocable`) are
 * allowed: another agent ignores them and loses nothing it needed.
 *
 *   node scripts/skill-portability.mjs [--root <repo root>]
 *
 * Exit code: 0 portable, 1 a skill is not.
 */
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const SKILLS = join('marketplace', 'plugins', 'pragmatic-design', 'skills');
const DESCRIPTION_LIMIT = 300;
const SPEC_FIELDS = ['name', 'description', 'license', 'compatibility', 'metadata', 'allowed-tools'];
const CLAUDE_UI_FIELDS = ['argument-hint', 'shell', 'user-invocable', 'disable-model-invocation'];

/** The problems of one SKILL.md, given its folder name and its text. Empty when it is portable. */
export function skillProblems(folder, text) {
  const problems = [];
  if (text.startsWith('﻿'))
    problems.push('starts with a byte-order mark: agents other than Claude Code do not find the frontmatter');

  const lines = text.replace(/^﻿/, '').split(/\r?\n/);
  const end = lines.indexOf('---', 1);
  if (lines[0] !== '---' || end < 0) return [...problems, 'no frontmatter between --- lines at the top'];

  const fields = new Map();
  for (const line of lines.slice(1, end)) {
    const m = /^([A-Za-z_-]+):\s*(.*)$/.exec(line);
    if (!m) continue;
    const value = m[2].trim();
    const quoted = /^(".*"|'.*')$/.test(value);
    if (!quoted && /: | #/.test(value))
      problems.push(`${m[1]} is unquoted and contains ": " or " #": not valid YAML — quote it`);
    fields.set(m[1], quoted ? value.slice(1, -1) : value);
  }

  const name = fields.get('name') ?? '';
  if (name !== folder) problems.push(`name "${name}" differs from its folder "${folder}"`);
  if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(name) || name.length > 64)
    problems.push(`name "${name}" is not lowercase letters, digits and single hyphens, at most 64`);

  const description = fields.get('description') ?? '';
  if (description.length === 0 || description.length > DESCRIPTION_LIMIT)
    problems.push(`description is ${description.length} characters; keep it between 1 and ${DESCRIPTION_LIMIT} so Codex lists it whole`);
  if (description && !description.startsWith('Use when '))
    problems.push('description does not start with "Use when": a cut would take the trigger instead of the detail');

  if (fields.has('when_to_use'))
    problems.push('when_to_use is read by Claude Code only: fold it into the description');

  for (const key of fields.keys())
    if (!SPEC_FIELDS.includes(key) && !CLAUDE_UI_FIELDS.includes(key) && key !== 'when_to_use')
      problems.push(`field "${key}" is neither in the specification nor a known Claude Code UI field`);

  return problems;
}

function main() {
  const at = process.argv.indexOf('--root');
  const root = at > 0 ? process.argv[at + 1] : join(dirname(fileURLToPath(import.meta.url)), '..');
  const dir = join(root, SKILLS);
  if (!existsSync(dir)) {
    console.error(`skill-portability: ${dir} does not exist — nothing was checked`);
    process.exit(1);
  }

  const folders = readdirSync(dir, { withFileTypes: true }).filter((d) => d.isDirectory()).map((d) => d.name);
  let failing = 0;
  for (const folder of folders) {
    const file = join(dir, folder, 'SKILL.md');
    const problems = existsSync(file) ? skillProblems(folder, readFileSync(file, 'utf8')) : ['has no SKILL.md'];
    if (!problems.length) continue;
    failing++;
    for (const p of problems) console.log(`  ${folder}: ${p}`);
  }

  if (failing) {
    console.log(`skills portable: ${folders.length - failing} of ${folders.length}`);
    process.exit(1);
  }
  console.log(`skills portable (${folders.length} skills)`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) main();
