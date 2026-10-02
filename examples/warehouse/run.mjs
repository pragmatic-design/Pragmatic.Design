#!/usr/bin/env node
// Starts the whole Warehouse topology the suite runs: five Agents in one cluster, Orders, Stock twice and
// Shipping, each announcing its route to the Agent beside it, and the gateway building its routes from them.
//
//   docker compose up -d --wait          # PostgreSQL, RabbitMQ, Redis
//   node run.mjs                         # starts everything, prints the gateway and a token per role
//   node run.mjs --check                 # starts everything, proves the rotation, stops, exits 0 or 1
//
// What WarehouseFixture does inside the suite (tests/…/Infrastructure/WarehouseFixture.cs, AgentDaemon.cs),
// on fixed ports instead of free ones. The keys below are for development and are not secrets.

import { spawn, spawnSync } from 'node:child_process';
import { createHmac, randomUUID } from 'node:crypto';
import { createWriteStream, existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const check = process.argv.includes('--check');

const JWT_KEY = 'warehouse-development-signing-key-not-a-secret';
const GOSSIP_KEY = 'warehouse-development-gossip-key';
const ISSUER = 'warehouse';
const GATEWAY = 'http://127.0.0.1:5220';

// The gateway's Agent first: every other Agent joins the cluster through it.
const AGENTS = [
  { name: 'gateway', gossipPort: 7951 },
  { name: 'orders', gossipPort: 7952 },
  { name: 'stock-a', gossipPort: 7953 },
  { name: 'stock-b', gossipPort: 7954 },
  { name: 'shipping', gossipPort: 7955 },
];

const HOSTS = [
  { name: 'orders', project: 'Warehouse.Orders.Host', port: 5221, route: 'orders' },
  { name: 'stock-a', project: 'Warehouse.Stock.Host', port: 5222, route: 'warehouse' },
  { name: 'stock-b', project: 'Warehouse.Stock.Host', port: 5223, route: 'warehouse' },
  { name: 'shipping', project: 'Warehouse.Shipping.Host', port: 5224, route: 'shipping' },
];

const ROLES = ['order-desk', 'stock-clerk', 'stock-manager', 'shipping-clerk'];

const work = join(tmpdir(), 'warehouse-run');
const logs = join(work, 'logs');
const children = new Map();

function projectDir(project) {
  return project === 'Pragmatic.Gateway'
    ? join(root, 'Pragmatic.Gateway', 'src', project)
    : project === 'Pragmatic.Agent'
      ? join(root, 'Pragmatic.Agent', 'src', project)
      : join(here, 'src', project);
}

function dll(project) {
  return join(projectDir(project), 'bin', 'Debug', 'net10.0', `${project}.dll`);
}

// A pipe name on Windows, a socket file elsewhere — the two transports the Agent client speaks.
function socketOf(agent) {
  return process.platform === 'win32' ? `warehouse-run-${agent}` : join(work, agent, 'agent.sock');
}

function build() {
  for (const project of ['Pragmatic.Agent', 'Pragmatic.Gateway', ...new Set(HOSTS.map(h => h.project))]) {
    console.log(`build   ${project}`);
    const built = spawnSync('dotnet', ['build', projectDir(project), '-v', 'q', '-nologo', '-clp:ErrorsOnly'],
      { stdio: 'inherit' });
    if (built.status !== 0) throw new Error(`dotnet build ${project} failed (${built.status})`);
  }
}

/** Starts a process, its output in a log file; resolves when `ready` sees a line it accepts. */
function start(name, args, { cwd, env, ready }) {
  const log = join(logs, `${name}.log`);
  const out = createWriteStream(log);
  const child = spawn('dotnet', args, { cwd, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] });
  children.set(name, child);

  return new Promise((resolveStarted, reject) => {
    const onLine = chunk => {
      out.write(chunk);
      if (ready?.(chunk.toString())) resolveStarted(child);
    };
    child.stdout.on('data', onLine);
    child.stderr.on('data', chunk => out.write(chunk));
    child.on('exit', code => {
      children.delete(name);
      reject(new Error(`${name} exited (${code}) before it was ready — see ${log}`));
    });
    if (!ready) resolveStarted(child);
  });
}

function within(promise, seconds, what) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(`${what}: not within ${seconds}s`)), seconds * 1000);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

async function startAgent({ name, gossipPort }, peer) {
  const dataDir = join(work, name);
  mkdirSync(dataDir, { recursive: true });
  const args = [dll('Pragmatic.Agent'), 'start', '--socket', socketOf(name), '--data-dir', dataDir,
    '--bind', '127.0.0.1', '--port', String(gossipPort), '--gossip-key', GOSSIP_KEY];
  if (peer) args.push('--peers', `127.0.0.1:${peer.gossipPort}`);

  // The line the daemon prints once it listens, as AgentDaemon waits for it.
  await within(start(`agent-${name}`, args, { cwd: work, ready: line => line.includes('Agent ready') }),
    30, `the Agent '${name}' is ready`);
  console.log(`agent   ${name.padEnd(9)} gossip 127.0.0.1:${gossipPort}  socket ${socketOf(name)}`);
}

async function startHost({ name, project, port, route }) {
  const address = `http://127.0.0.1:${port}`;
  await start(name, [dll(project)], {
    // The project directory is the content root: appsettings.json is read from there.
    cwd: projectDir(project),
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: address,
      Jwt__Key: JWT_KEY,
      Pragmatic__Agent__SocketPath: socketOf(name),
      Pragmatic__Agent__Announce__RouteId: route,
      Pragmatic__Agent__Announce__Path: `/${route}/{**catch-all}`,
      Pragmatic__Agent__Announce__PathRemovePrefix: `/${route}`,
      Pragmatic__Agent__Announce__RequireAuth: 'true',
      Pragmatic__Agent__Announce__Address: address,
      Messaging__OutboxPollingIntervalSeconds: '1',
    },
  });

  // Up once it answers at all: the migrations run before it listens. A fetch that cannot connect throws.
  await until(async () => (await fetch(`${address}/health`)).status > 0, 90, `${name} answers at ${address}`);
  console.log(`host    ${name.padEnd(9)} ${address}  announces /${route}`);
}

async function startGateway() {
  await start('gateway', [dll('Pragmatic.Gateway')], {
    cwd: projectDir('Pragmatic.Gateway'),
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      // Not ASPNETCORE_URLS: the gateway sets its own listen address from Gateway:HttpUrl.
      Gateway__HttpUrl: GATEWAY,
      Gateway__AgentSocketPath: socketOf('gateway'),
      Gateway__Jwt__SigningKey: JWT_KEY,
      Gateway__Jwt__Issuer: ISSUER,
      Gateway__Jwt__Audience: ISSUER,
    },
  });
  console.log(`gateway ${GATEWAY}  (routes from its Agent)`);
}

/** Retries `probe` until it returns true; a thrown error counts as not yet. */
async function until(probe, seconds, what) {
  const deadline = Date.now() + seconds * 1000;
  let last;
  while (Date.now() < deadline) {
    try {
      if (await probe()) return;
    } catch (error) {
      last = error;
    }
    await new Promise(r => setTimeout(r, 250));
  }
  throw new Error(`${what}: not within ${seconds}s${last ? ` (last: ${last.message})` : ''}`);
}

// ASSUMPTION: the claims JwtTokenGenerator writes (sub, jti, iat, role, iss, aud, exp; HS256 over Jwt:Key).
// A second copy of those rules — the check below is what catches it drifting: a token the hosts refuse
// fails it.
function token(role) {
  const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const body = `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode({
    sub: `${role}-dev`, jti: randomUUID(), iat: now, nbf: now, exp: now + 8 * 3600,
    iss: ISSUER, aud: ISSUER, role,
  })}`;
  return `${body}.${createHmac('sha256', JWT_KEY).update(body).digest('base64url')}`;
}

async function throughTheGateway(path, role) {
  const response = await fetch(`${GATEWAY}${path}`, { headers: { Authorization: `Bearer ${token(role)}` } });
  return { status: response.status, servedBy: response.headers.get('x-served-by') };
}

async function reads(count) {
  const round = [];
  for (let i = 0; i < count; i++) round.push(await throughTheGateway('/warehouse/health', 'stock-manager'));
  return round;
}

async function proveTheRotation() {
  for (const [route, role] of [['orders', 'order-desk'], ['warehouse', 'stock-clerk'], ['shipping', 'shipping-clerk']])
    await until(async () => (await throughTheGateway(`/${route}/health`, role)).status === 200,
      60, `GET /${route}/health through the gateway answers 200`);

  // A request the host itself authorizes: the token is accepted past the gateway, not only by it.
  const products = await throughTheGateway('/warehouse/api/products', 'stock-clerk');
  if (products.status !== 200) throw new Error(`GET /warehouse/api/products as stock-clerk answered ${products.status}`);
  console.log('check   every service answers through the gateway, and Stock accepts the token');

  let both;
  await until(async () => {
    both = new Set((await reads(8)).map(read => read.servedBy));
    return both.size === 2;
  }, 60, 'both Stock instances answer through the gateway');
  console.log(`check   Stock answered by ${[...both].join(' and ')}`);

  stop('stock-b');
  let survivor;
  await until(async () => {
    const round = await reads(20);
    survivor = round[0].servedBy;
    return round.every(read => read.status === 200 && read.servedBy === survivor);
  }, 30, 'twenty reads after stopping stock-b, all answered by one instance');
  if (!both.has(survivor)) throw new Error(`the reads came from ${survivor}, which answered nothing before`);
  console.log(`check   stock-b stopped: twenty reads, all 200, all from ${survivor}`);
}

function stop(name) {
  const child = children.get(name);
  children.delete(name);
  child?.kill();
}

function stopAll() {
  for (const name of [...children.keys()].reverse()) stop(name);
}

async function main() {
  rmSync(work, { recursive: true, force: true });
  mkdirSync(logs, { recursive: true });
  build();

  const [gatewayAgent, ...others] = AGENTS;
  await startAgent(gatewayAgent);
  await Promise.all(others.map(agent => startAgent(agent, gatewayAgent)));
  await Promise.all(HOSTS.map(startHost));
  await startGateway();

  if (check) {
    await proveTheRotation();
    return;
  }

  console.log(`\nGateway: ${GATEWAY}   logs: ${logs}`);
  for (const role of ROLES) console.log(`\n${role}:\n  ${token(role)}`);
  console.log(`\ne.g. curl -i -H "Authorization: Bearer <stock-manager token>" ${GATEWAY}/warehouse/health`);
  console.log('Ctrl+C stops everything.');
  await new Promise(() => {});
}

process.on('SIGINT', () => { stopAll(); process.exit(130); });

main().then(
  () => { stopAll(); process.exit(0); },
  error => {
    console.error(`\nFAIL ${error.message}`);
    // A process that never started has no log; the others still say what they saw.
    for (const name of ['gateway', ...HOSTS.map(h => h.name)].filter(n => existsSync(join(logs, `${n}.log`)))) {
      const tail = readFileSync(join(logs, `${name}.log`), 'utf8').split('\n').slice(-15).join('\n');
      console.error(`\n--- ${name}.log (tail) ---\n${tail}`);
    }
    stopAll();
    process.exit(1);
  });
