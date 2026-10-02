// Cases for skill-portability.mjs: each rule refuses what another agent would misread, and a portable
// skill passes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { skillProblems } from './skill-portability.mjs';

const skill = (frontmatter, body = '# Body\n') => `---\n${frontmatter}\n---\n\n${body}`;
const good = 'name: pragmatic-use-demo\ndescription: Use when the thing is needed — does a thing.';

test('the control: a portable skill has no problems', () => {
  assert.deepEqual(skillProblems('pragmatic-use-demo', skill(good)), []);
});

test('a byte-order mark before the frontmatter is refused', () => {
  const problems = skillProblems('pragmatic-use-demo', `﻿${skill(good)}`);
  assert.equal(problems.length, 1);
  assert.match(problems[0], /byte-order mark/);
});

test('when_to_use is refused: only Claude Code reads it', () => {
  const problems = skillProblems('pragmatic-use-demo', skill(`${good}\nwhen_to_use: Trigger when X.`));
  assert.equal(problems.length, 1);
  assert.match(problems[0], /when_to_use/);
});

test('a name that is not its folder is refused', () => {
  assert.match(skillProblems('pragmatic-use-other', skill(good)).join('\n'), /differs from its folder/);
});

test('a description over 300 characters is refused: Codex would cut its end', () => {
  const long = `name: pragmatic-use-demo\ndescription: Use when ${'x'.repeat(292)}`;
  assert.match(skillProblems('pragmatic-use-demo', skill(long)).join('\n'), /301 characters/);
});

test('the control: a description of exactly 300 characters passes', () => {
  const edge = `name: pragmatic-use-demo\ndescription: Use when ${'x'.repeat(291)}`;
  assert.deepEqual(skillProblems('pragmatic-use-demo', skill(edge)), []);
});

test('a description whose trigger is not first is refused', () => {
  const late = 'name: pragmatic-use-demo\ndescription: Does a thing. Trigger when the thing is needed.';
  assert.match(skillProblems('pragmatic-use-demo', skill(late)).join('\n'), /does not start with "Use when"/);
});

test('an unquoted value with ": " is refused, a quoted one passes', () => {
  const bare = 'name: pragmatic-use-demo\ndescription: Use when X: Y and Z.';
  assert.match(skillProblems('pragmatic-use-demo', skill(bare)).join('\n'), /not valid YAML/);
  const quoted = 'name: pragmatic-use-demo\ndescription: "Use when X: Y and Z."';
  assert.deepEqual(skillProblems('pragmatic-use-demo', skill(quoted)), []);
});

test('a missing frontmatter is refused', () => {
  assert.match(skillProblems('pragmatic-use-demo', '# Just a body\n').join('\n'), /no frontmatter/);
});

test('Claude Code UI fields are allowed: another agent ignores them and loses nothing', () => {
  const ui = `${good}\nargument-hint: <name>\nshell: bash\nuser-invocable: true`;
  assert.deepEqual(skillProblems('pragmatic-use-demo', skill(ui)), []);
});

test('an unknown field is refused', () => {
  assert.match(skillProblems('pragmatic-use-demo', skill(`${good}\ntriggers: X`)).join('\n'), /"triggers"/);
});
