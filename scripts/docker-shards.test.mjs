/**
 * The split of the container suites across CI jobs, and the check that keeps it whole.
 *
 *   node --test scripts/docker-shards.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DOCKER_SHARDS, parseShard, shardProblems } from './docker-shards.mjs';

const SHARDS = [['A.Tests', 'B.Tests'], ['C.Tests']];

test('every suite in exactly one shard: nothing to report', () => {
  assert.deepEqual(shardProblems(SHARDS, ['A.Tests', 'B.Tests', 'C.Tests']), []);
});

test('a container suite in no shard is reported: it would stop running in CI', () => {
  assert.deepEqual(shardProblems(SHARDS, ['A.Tests', 'B.Tests', 'C.Tests', 'D.Tests']), [
    'D.Tests requires Docker and is in no shard',
  ]);
});

test('a shard naming a suite that does not exist is reported', () => {
  assert.deepEqual(shardProblems(SHARDS, ['A.Tests', 'C.Tests']), [
    'B.Tests is in shard 1 and is not a container suite',
  ]);
});

test('a suite in two shards is reported: it would run twice and hide nothing, but the map is wrong', () => {
  assert.deepEqual(shardProblems([['A.Tests'], ['A.Tests']], ['A.Tests']), ['A.Tests is in shards 1 and 2']);
});

test('k/n picks the k-th shard, counted from one', () => {
  assert.deepEqual(parseShard('2/2', SHARDS), { index: 1, names: ['C.Tests'] });
});

test('an n that is not the number of shards is refused: the map and the CI matrix disagree', () => {
  assert.throws(() => parseShard('1/3', SHARDS), /3 shards asked for, the map has 2/);
});

test('a k outside 1..n, or a value that is not k/n, is refused', () => {
  assert.throws(() => parseShard('0/2', SHARDS), /k\/n/);
  assert.throws(() => parseShard('3/2', SHARDS), /k\/n/);
  assert.throws(() => parseShard('two', SHARDS), /k\/n/);
});

test('the map itself names no suite twice', () => {
  const names = DOCKER_SHARDS.flat();
  assert.equal(new Set(names).size, names.length);
});
