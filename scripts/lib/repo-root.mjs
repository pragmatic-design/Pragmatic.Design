/**
 * Moves the process to the root of the repository this file lives in, as soon as it is imported.
 *
 * `check.mjs` and what it imports read the repository through paths relative to the current directory:
 * `Pragmatic.Design.slnx`, `global.json`, `readdirSync('.')`. Started from anywhere else, the gate found
 * the test projects of whatever tree was there, and then crashed on the solution with Node's stack trace
 * (#302). Importing this first makes every one of those paths mean the repository, wherever the command
 * was typed.
 *
 * ⚠️ It must be the first import of the script that needs it: ES modules run in the order they are
 * imported, and a module imported before this one would still see the old directory.
 */
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

process.chdir(REPO_ROOT);
