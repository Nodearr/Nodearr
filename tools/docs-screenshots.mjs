// Captures the user guide's screenshots from the two isolated UI test instances (A and B).
// Run these commands from the repository root to recreate the kit and capture all screenshots.
// The down command deletes the kit's existing test data; it does not affect an installed Nodearr.
//
//   powershell -ExecutionPolicy Bypass -File tools/ui-test.ps1 down
//   powershell -ExecutionPolicy Bypass -File tools/ui-test.ps1 up -Solo
//   node tools/docs-screenshots.mjs prepare
//   node tools/docs-screenshots.mjs
//
// "prepare" does through the API what the walkthrough in docs/guides/ui-test.md does by hand: it
// sets up signing in, names the nodes Alex (A) and Sam (B), connects their Sonarr and Radarr,
// makes them friends, and has each send the other something it then gets: Alex sends Sam Cowboy
// Bebop, and Sam sends Alex Spirited Away. Each step it finds done already is skipped.
//
// With no arguments, the capture command takes every screenshot. Pass screenshot names to capture
// only those pages. The "send" screenshot changes the test data: Sam sends Alex Frieren and Alex
// requests it. Recreate the kit before capturing "send" a second time.
//
// The help texts in the shots come from the server, so run the kit on a package built from the
// branch being documented (build/Build-Release.ps1, then up -Solo -NodearrZip PATH), not on a
// release.
//
// "firstrun" needs a third Nodearr that has never been started, at NODEARR_NEW
// (http://127.0.0.1:26663 unless set). Start the kit's own package with an empty folder:
// Nodearr__Server__Port and Nodearr__Transport__Port set to 26663, and
// Nodearr.Console.exe -nobrowser -data=EMPTY_FOLDER. Without it, every shot before "firstrun" is
// taken and then the run stops.
//
// It needs Playwright with Chromium, and sharp to make the files smaller, which are not among
// Nodearr's dependencies: run it where they are installed, with NODE_PATH pointing there.
// NODEARR_SCREENSHOT_BROWSER can select an already installed Chromium executable.
// Sign-in comes from NODEARR_USERNAME and NODEARR_PASSWORD, the same on both set-ups, and the kit's
// folder from NODEARR_KIT, ~/nodearr-ui-test unless set.
//
// What a screenshot would give away about the machine it was taken on is replaced first: the
// folder the kit lives in, the machine's public and local addresses, and its disk's free space. The guide's
// screenshots show example values, not anyone's.

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const out = path.join(root, 'docs', 'user-guide', 'images');
const A = process.env.NODEARR_A ?? 'http://localhost:26633';
const B = process.env.NODEARR_B ?? 'http://127.0.0.1:26643';
const user = process.env.NODEARR_USERNAME;
const password = process.env.NODEARR_PASSWORD;

if (!user || !password) {
  console.error('Set NODEARR_USERNAME and NODEARR_PASSWORD to the test set-ups\' sign-in.');
  process.exit(1);
}

mkdirSync(out, { recursive: true });

// --- Preparing the set-ups -------------------------------------------------------------------

const kit = process.env.NODEARR_KIT ?? path.join(os.homedir(), 'nodearr-ui-test');

// One kit set-up: its Nodearr, reached with its API key, and its Arr apps' addresses and keys.
function setUp(side, base, name) {
  const folder = path.join(kit, side);
  // Windows PowerShell writes it with a byte order mark, which JSON does not allow.
  const state = JSON.parse(readFileSync(path.join(folder, 'state.json'), 'utf8').replace(/^﻿/, ''));
  const config = readFileSync(path.join(folder, 'nodearr-data', 'config.xml'), 'utf8');
  const key = config.match(/<ApiKey>([^<]+)<\/ApiKey>/)[1];

  return {
    name,
    base,
    port: state.nodearr_port,
    apps: [
      { implementation: 'Sonarr', port: state.sonarr_port, key: state.sonarr_key },
      { implementation: 'Radarr', port: state.radarr_port, key: state.radarr_key },
    ],
    async api(method, route, body) {
      const response = await fetch(`${base}/api/v5${route}`, {
        method,
        headers: { 'X-Api-Key': key, 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      if (!response.ok) {
        throw new Error(`${name}: ${method} ${route} answered ${response.status}: ${await response.text()}`);
      }
      const text = await response.text();
      return text ? JSON.parse(text) : null;
    },
  };
}

async function until(what, check, seconds = 180) {
  const deadline = Date.now() + seconds * 1000;
  for (;;) {
    const found = await check();
    if (found) return found;
    if (Date.now() > deadline) throw new Error(`Gave up waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
}

// Signing in, the node's name, and its Sonarr and Radarr connected, as the first start does.
async function setUpNode(node) {
  const general = await node.api('GET', '/settings/general');
  if (general.authenticationMethod !== 'forms' || general.username !== user) {
    await node.api('PUT', `/settings/general/${general.id}`, {
      ...general,
      authenticationMethod: 'forms',
      authenticationRequired: 'enabled',
      username: user,
      password,
      passwordConfirmation: password,
    });
  }

  const settings = await node.api('GET', '/settings/node');
  if (settings.nodeName !== node.name) {
    await node.api('PUT', `/settings/node/${settings.id}`, { ...settings, nodeName: node.name });
  }

  const connected = await node.api('GET', '/applications');
  const schema = await node.api('GET', '/applications/schema');
  for (const app of node.apps) {
    if (connected.some((c) => c.implementation === app.implementation)) continue;

    const definition = schema.find((s) => s.implementation === app.implementation);
    const value = { nodearrUrl: `http://localhost:${node.port}`, baseUrl: `http://localhost:${app.port}`, apiKey: app.key };
    definition.name = app.implementation;
    definition.enable = true;
    definition.fields = definition.fields.map((f) => (f.name in value ? { ...f, value: value[f.name] } : f));
    await node.api('POST', '/applications', definition);
  }

  // Each app's film and three episodes, read into the catalog.
  await until(`${node.name}'s library to be read`, async () => {
    const titles = await node.api('GET', '/sharing/titles');
    return titles.filter((t) => t.files > 0).length >= 2;
  });
  console.log(`${node.name}: signing in, Sonarr and Radarr set up`);
}

async function friendOf(node, other) {
  const peers = await node.api('GET', '/nodes/peers');
  return peers.find((p) => p.state === 'trusted' && p.name === other.name);
}

// One sends the other a title of theirs, and the other gets it, as step 8 of the walkthrough.
async function sendAndGet(from, to, titleStart) {
  const received = await to.api('GET', '/sharing/sends/received');
  if (received.some((r) => r.title.startsWith(titleStart))) return;

  await from.api('POST', '/command', { name: 'PeerCatalogSync' });
  await to.api('POST', '/command', { name: 'PeerCatalogSync' });

  const title = (await from.api('GET', '/sharing/titles')).find((t) => t.title.startsWith(titleStart));
  const friend = await friendOf(from, to);
  await from.api('POST', '/sharing/sends', {
    peerId: friend.id,
    items: [{ mediaKind: title.mediaKind, applicationId: title.applicationId, arrId: title.arrId }],
  });

  const card = await until(`${to.name} to be told about ${title.title}`, async () =>
    (await to.api('GET', '/sharing/sends/received')).find((r) => r.title.startsWith(titleStart)));
  await until(`${to.name} to have ${from.name}'s catalog`, async () => {
    await to.api('POST', '/command', { name: 'PeerCatalogSync' });
    const plan = await to.api('GET', `/sharing/sends/received/${card.id}/getit`);
    return plan.targets?.length > 0 && !plan.problem;
  });

  const plan = await to.api('GET', `/sharing/sends/received/${card.id}/getit`);
  await to.api('POST', `/sharing/sends/received/${card.id}/getit`, plan.targets[0].defaults);

  await until(`${to.name}'s app to import ${title.title}`, async () => {
    const titles = await to.api('GET', '/sharing/titles');
    return titles.some((t) => t.title.startsWith(titleStart) && t.files >= title.files);
  }, 600);
  console.log(`${from.name} sent ${to.name} ${title.title}, and ${to.name} got it`);
}

async function prepare() {
  const alex = setUp('a', A, 'Alex');
  const sam = setUp('b', B, 'Sam');

  await setUpNode(alex);
  await setUpNode(sam);

  if (!(await friendOf(alex, sam))) {
    const invite = await alex.api('POST', '/nodes/invites', { validHours: 1, label: 'Sam', shareAll: true });
    await sam.api('POST', '/nodes/invites/redeem', { code: invite.code, shareAll: true });
    await until('Alex and Sam to be friends', async () => (await friendOf(alex, sam)) && (await friendOf(sam, alex)));
  }
  console.log('Alex and Sam are friends');

  await sendAndGet(alex, sam, 'Cowboy Bebop');
  await sendAndGet(sam, alex, 'Spirited Away');
}

if (process.argv[2] === 'prepare') {
  await prepare();
  process.exit(0);
}

// Runs in the page: every text and form value with something of this machine's in it is replaced
// by an example.
function mask(kitFolder) {
  const kitPattern = kitFolder.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const rules = [
    // The kit's folders: ...\nodearr-ui-test\a\tv\Cowboy Bebop becomes D:\Media\TV\Cowboy Bebop.
    [new RegExp(`${kitPattern}[\\\\/][ab][\\\\/]tv`, 'gi'), 'D:\\Media\\TV'],
    [new RegExp(`${kitPattern}[\\\\/][ab][\\\\/]movies`, 'gi'), 'D:\\Media\\Movies'],
    [new RegExp(`${kitPattern}[\\\\/][ab][\\\\/]nodearr-data`, 'gi'), 'C:\\ProgramData\\Nodearr'],
    [new RegExp(`${kitPattern}[\\\\/][ab]`, 'gi'), 'D:\\Media'],
    [/[A-Za-z]:\\Users\\[^\\\s]+/gi, 'C:\\Users\\you'],
    // Addresses: a public one becomes one kept for documentation (RFC 5737), a local one a home
    // network's.
    [/\b(?!10\.|127\.|172\.(?:1[6-9]|2\d|3[01])\.|192\.168\.)(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b/g, '203.0.113.24'],
    [/\b(?:10|172|192)\.\d{1,3}\.\d{1,3}\.\d{1,3}\b/g, '192.168.1.20'],
    // IPv6, to the documentation prefix (RFC 3849).
    [/\b(?:[0-9a-f]{1,4}:){3,7}[0-9a-f]{1,4}\b/gi, '2001:db8::24'],
    // The free space on the kit's disk, as a root folder shows it, is this machine's.
    [/\(\d+(?:\.\d+)? [KMGTP]iB free\)/g, '(1.8 TiB free)'],
  ];
  const plain = (s) => rules.reduce((t, [re, to]) => t.replace(re, to), s);

  // An invite code is the node's addresses and a secret, encoded. Its addresses are masked as
  // above and its secret replaced, so it still reads as a code and leads nowhere.
  const fromBase64 = (s) => atob(s.replace(/-/g, '+').replace(/_/g, '/'));
  const toBase64 = (s) => btoa(s).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  const invite = (s) =>
    s.replace(/nodearr-invite:([A-Za-z0-9_-]+)/g, (whole, data) => {
      try {
        const code = JSON.parse(fromBase64(data));
        code.a = (code.a ?? []).map(plain);
        code.s = btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(32))));
        return `nodearr-invite:${toBase64(JSON.stringify(code))}`;
      } catch {
        return 'nodearr-invite:…';
      }
    });
  const apply = (s) => plain(invite(s));

  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const masked = apply(n.nodeValue);
    if (masked !== n.nodeValue) n.nodeValue = masked;
  }
  for (const input of document.querySelectorAll('input, textarea')) {
    const masked = apply(input.value);
    if (masked !== input.value) input.value = masked;
  }
  for (const option of document.querySelectorAll('option')) {
    option.textContent = apply(option.textContent);
  }
}

async function signIn(context, base) {
  const page = await context.newPage();
  await page.goto(`${base}/login`);
  await page.fill('input[name="username"]', user);
  await page.fill('input[name="password"]', password);
  await page.click('button[type="submit"]');
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  return page;
}

async function open(page, base, route) {
  await page.goto(base + route);
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(600);
}

// The guide's column is narrower than a window, so a page is shown without the sidebar and the
// bar along the top, which are the same on every page, unless chrome asks for the whole window.
const SIDEBAR = 210;
const TOPBAR = 60;

// A page, or one part of it with a margin of the page round it.
async function shoot(target, name, { chrome = false } = {}) {
  const page = target.page ? target.page() : target;
  const file = path.join(out, `${name}.png`);
  await page.evaluate(mask, kit);

  if (target === page) {
    const view = page.viewportSize();
    const clip = chrome ? undefined : { x: SIDEBAR, y: TOPBAR, width: view.width - SIDEBAR, height: view.height - TOPBAR };
    await page.screenshot({ path: file, animations: 'disabled', clip });
  } else {
    const margin = 16;
    const box = await target.boundingBox();
    const view = page.viewportSize();
    const x = Math.max(0, box.x - margin);
    const y = Math.max(0, box.y - margin);
    await page.screenshot({
      path: file,
      animations: 'disabled',
      clip: { x, y, width: Math.min(view.width - x, box.width + 2 * margin), height: Math.min(view.height - y, box.height + 2 * margin) },
    });
  }

  await compress(file);
  console.log('took', name);
}

// A screenshot is mostly flat colour, so a palette makes it a third of the size with no visible
// change. Done when sharp is installed beside Playwright, and skipped otherwise.
async function compress(file) {
  let sharp;
  try {
    sharp = require('sharp');
  } catch {
    return;
  }

  const smaller = await sharp(file).png({ palette: true, quality: 95, effort: 10, compressionLevel: 9 }).toBuffer();
  writeFileSync(file, smaller);
}

// The node settings through the API, with the key the page itself uses.
async function nodeSettings(page, change) {
  await page.waitForFunction(() => window.Nodearr?.apiKey);
  return page.evaluate(async (change) => {
    const headers = { 'X-Api-Key': window.Nodearr.apiKey, 'Content-Type': 'application/json' };
    const current = await (await fetch('/api/v5/settings/node', { headers })).json();
    if (!change) return current;
    const next = { ...current, ...change };
    await fetch(`/api/v5/settings/node/${current.id}`, { method: 'PUT', headers, body: JSON.stringify(next) });
    return current;
  }, change);
}

const shots = {
  async home({ a }) {
    await open(a, A, '/');
    await shoot(a, 'home', { chrome: true });
  },

  async apps({ a }) {
    await open(a, A, '/settings/apps');
    await shoot(a, 'settings-apps');
    // The card's whole face is a button laid over its title.
    await a.getByText('Sonarr', { exact: true }).first().click({ force: true });
    await a.waitForSelector('[role="dialog"], .modal, [class*="Modal"]');
    await a.waitForTimeout(600);
    await shoot(a, 'settings-apps-sonarr');
    await a.keyboard.press('Escape');
  },

  // The invite is deleted straight after, so the code in the picture leads nowhere.
  async invite({ a }) {
    await open(a, A, '/nodes/invites');
    await a.getByPlaceholder(/A note for yourself/).fill('For Sam');
    await a.getByRole('button', { name: 'Create Invite' }).click();
    await a.getByText(/nodearr-invite:/).first().waitFor();
    await a.waitForTimeout(400);
    await shoot(a, 'invites');

    // The code in the picture must hold example addresses only.
    const shown = (await a.getByText(/nodearr-invite:/).first().textContent()).trim();
    const data = shown.slice('nodearr-invite:'.length).replace(/-/g, '+').replace(/_/g, '/');
    const addresses = JSON.parse(Buffer.from(data, 'base64').toString('utf8')).a ?? [];
    const leaked = addresses.filter((x) => !/^(203\.0\.113\.24|192\.168\.1\.20|\[?2001:db8::24\]?)(:\d+)?$/.test(x));
    if (leaked.length > 0) {
      throw new Error(`The invite in invites.png still holds real addresses: ${leaked.join(', ')}`);
    }
    console.log('invite shows', addresses.join(', '));
    await a.evaluate(async () => {
      const headers = { 'X-Api-Key': window.Nodearr.apiKey };
      const invites = await (await fetch('/api/v5/nodes/invites', { headers })).json();
      for (const invite of invites.filter((i) => i.label === 'For Sam')) {
        await fetch(`/api/v5/nodes/invites/${invite.id}`, { method: 'DELETE', headers });
      }
    });
  },

  async friends({ a }) {
    await open(a, A, '/nodes/trusted');
    await shoot(a, 'trusted-nodes');
  },

  async sharing({ a }) {
    await open(a, A, '/catalog/sharing');
    await shoot(a, 'sharing');
    const hidden = a.locator('fieldset', { has: a.locator('legend', { hasText: /^Hidden$/ }) }).first();
    await hidden.scrollIntoViewIfNeeded();
    await hidden.getByPlaceholder('Filter').fill('Back');
    await a.waitForTimeout(300);
    await shoot(hidden, 'sharing-hidden');
    await hidden.getByPlaceholder('Filter').fill('');
  },

  async catalog({ a }) {
    await open(a, A, '/catalog/peers');
    await shoot(a, 'peer-catalog');
    await a.getByRole('button', { name: 'Get it', exact: true }).first().click();
    await a.locator('select[name="qualityProfileId"]').waitFor({ state: 'visible' });
    await a.waitForLoadState('networkidle');
    await shoot(a, 'peer-catalog-get-it', { chrome: true });
    await a.keyboard.press('Escape');
    await a.getByText('Posters', { exact: true }).click();
    await a.waitForLoadState('networkidle');
    await a.waitForTimeout(1200);
    await shoot(a, 'peer-catalog-posters');
    await a.getByText('Table', { exact: true }).click();
    await open(a, A, '/catalog/local');
    await shoot(a, 'local-catalog');
    await a.getByText('Posters', { exact: true }).click();
    await a.waitForLoadState('networkidle');
    await a.waitForTimeout(1200);
    await shoot(a, 'local-catalog-posters');
    await a.getByText('Table', { exact: true }).click();
  },

  async settings({ a }) {
    await open(a, A, '/settings/node');
    await shoot(a, 'settings-node');
    await open(a, A, '/system/backup');
    await shoot(a, 'backup');
  },

  // B sends A Frieren. A's Home shows it, with Get it. B's uploads are slowed for a moment, so the
  // transfer that Get it starts is still running when Activity is taken.
  async send({ a, b }) {
    await open(b, B, '/catalog/sharing');
    const send = b.locator('fieldset', { has: b.locator('legend', { hasText: 'Send to a friend' }) }).first();
    await send.getByLabel(/^Frieren/).check();
    await send.locator('select').first().selectOption({ label: 'Alex' });
    await b.waitForTimeout(300);
    await shoot(send, 'send-to-a-friend');
    await send.getByRole('button', { name: 'Send', exact: true }).click();

    await open(a, A, '/');
    await a.getByText(/Sam sent you Frieren/).waitFor({ timeout: 60000 });
    await a.waitForTimeout(800);
    await shoot(a, 'received');

    await slowedTransfer(a, b, () => a.getByRole('button', { name: 'Get it' }).first().click());
  },

  // A node started for the first time, with an empty data folder, at NODEARR_NEW: the
  // password it asks for first, then Home's three steps. It is given the same sign-in as A and B.
  async firstrun({ browser }) {
    const base = process.env.NODEARR_NEW ?? 'http://127.0.0.1:26663';
    const page = await (await browser.newContext(options)).newPage();
    await open(page, base, '/');
    await page.getByText(/First, a password/).waitFor();
    await shoot(page, 'first-run', { chrome: true });

    await page.fill('input[name="username"]', user);
    await page.fill('input[name="password"]', password);
    await page.fill('input[name="passwordConfirmation"]', password);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await page.getByText(/First, a password/).waitFor({ state: 'detached' });

    const signedIn = await signIn(page.context(), base);
    await open(signedIn, base, '/');
    await signedIn.getByText('Connect Sonarr').waitFor();
    await shoot(signedIn, 'first-steps', { chrome: true });
  },

  // A transfer already under way on A, from B: started by hand, or by the send above.
  async transfers({ a, b }) {
    await slowedTransfer(a, b, async () => {});
  },
};

// B's uploads are slowed while start() starts a transfer to A, so it is still running when
// Activity is taken. B's limit is put back however it ends.
async function slowedTransfer(a, b, start) {
  const before = await nodeSettings(b, { uploadLimitKbps: 400 });
  console.log("B's uploads slowed; waiting for a transfer to A");
  try {
    await start();
    await open(a, A, '/activity/transfers');
    await a.getByText('Downloading').first().waitFor({ timeout: 120000 });
    await a.waitForTimeout(4000);
    await open(a, A, '/activity/transfers');
    await shoot(a, 'transfers');
  } finally {
    await nodeSettings(b, { uploadLimitKbps: before.uploadLimitKbps });
  }
}

const wanted = process.argv.slice(2);
const browser = await chromium.launch({ executablePath: process.env.NODEARR_SCREENSHOT_BROWSER });
// Twice the pixels, so text stays sharp when the guide shows a screenshot smaller than it was.
const options = { viewport: { width: 1280, height: 800 }, deviceScaleFactor: 2, colorScheme: 'dark' };
const a = await signIn(await browser.newContext(options), A);
const b = await signIn(await browser.newContext(options), B);

try {
  for (const [name, take] of Object.entries(shots)) {
    // transfers needs a transfer started by hand, so it is only taken when named.
    if (wanted.includes(name) || (wanted.length === 0 && name !== 'transfers')) {
      await take({ a, b, browser });
    }
  }
} finally {
  await browser.close();
}
