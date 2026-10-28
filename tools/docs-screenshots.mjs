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
// makes them friends, and has each send the other something it then gets: Alex sends Sam Out of
// the Inkwell, and Sam sends Alex The Little Shop of Horrors. Each step it finds done already is
// skipped.
//
// With no arguments, the capture command takes every screenshot. Pass screenshot names to capture
// only those pages. The "send" screenshot changes the test data: Sam sends Alex a Felix the Cat
// short from its table row and Alex sends Sam Night of the Living Dead from its poster. Recreate
// the kit before capturing "send" a second time.
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
// folder from NODEARR_KIT, ~/nodearr-ui-test unless set. For a local kit started with
// Nodearr__Auth__Method=External, set NODEARR_EXTERNAL_AUTH=true to skip the sign-in page.
//
// What a screenshot would give away about the machine it was taken on is replaced first: the
// folder the kit lives in, the machine's public and local addresses, and its disk's free space. The guide's
// screenshots show example values, not anyone's.
//
// The kit's titles are all in the public domain, but the posters Sonarr and Radarr fetch for them
// are often modern artwork. Every poster the pages ask Nodearr for is answered instead from
// tools/screenshot-posters, which holds public-domain images and where each came from. A title
// with no file there gets a plain placeholder, never the fetched poster.

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
const externalAuth = process.env.NODEARR_EXTERNAL_AUTH === 'true';

if ((!user || !password) && !externalAuth) {
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
  if (!externalAuth && (general.authenticationMethod !== 'forms' || general.username !== user)) {
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

  await sendAndGet(alex, sam, 'Out of the Inkwell');
  await sendAndGet(sam, alex, 'The Little Shop of Horrors');
}

if (process.argv[2] === 'prepare') {
  await prepare();
  process.exit(0);
}

// --- Posters ---------------------------------------------------------------------------------

const posters = path.join(root, 'tools', 'screenshot-posters');
const placeholder =
  '<svg xmlns="http://www.w3.org/2000/svg" width="400" height="600"><rect width="400" height="600" fill="#2a2f36"/></svg>';

// A file in tools/screenshot-posters is named after its title: "The Little Shop of Horrors" is
// the-little-shop-of-horrors.jpg.
function posterFile(title) {
  const slug = (title ?? '').toLowerCase().replace(/&/g, 'and').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  try {
    return readFileSync(path.join(posters, `${slug}.jpg`));
  } catch {
    return null;
  }
}

// Answers the page's poster requests on one node. Nodearr names a poster by the title's place in
// its own library, or by a friend's catalog item, so the node is asked which title that is.
async function swapPosters(context, node) {
  const missing = new Set();

  await context.route('**/api/v5/catalog/posters/**', async (route) => {
    const parts = new URL(route.request().url()).pathname.split('/');
    const at = parts.indexOf('posters');
    let title = null;

    if (parts[at + 1] === 'local') {
      const [applicationId, subject, arrId] = parts.slice(at + 2, at + 5);
      const kind = { series: 'tv', movie: 'movie', album: 'music' }[subject.toLowerCase()];
      const titles = await node.api('GET', '/sharing/titles');
      title = titles.find((t) => `${t.applicationId}` === applicationId && `${t.arrId}` === arrId && t.mediaKind === kind)?.title;
    } else if (parts[at + 1] === 'peer') {
      const items = await node.api('GET', '/catalog/peers/items?page=1&pageSize=1000');
      title = items.records.find((i) => `${i.id}` === parts[at + 2])?.title;
    }

    const image = posterFile(title);
    if (!image) {
      if (!missing.has(title)) console.warn(`${node.name}: no public-domain poster for ${title ?? route.request().url()}; a placeholder is shown`);
      missing.add(title);
      return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: placeholder });
    }

    return route.fulfill({ status: 200, contentType: 'image/jpeg', body: image });
  });
}

// Runs in the page: every text and form value with something of this machine's in it is replaced
// by an example.
function mask(kitFolder) {
  const kitPattern = kitFolder.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const rules = [
    // The kit's folders: ...\nodearr-ui-test\a\tv\Out of the Inkwell becomes D:\Media\TV\Out of the Inkwell.
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

  if (externalAuth) {
    await open(page, base, '/');
    return page;
  }

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

// Show one branch in the guide shot; the live catalog starts with every show and season closed.
async function expandFirstSeries(page) {
  const show = page.getByRole('button', { name: /^Expand / }).first();

  if ((await show.count()) === 0) {
    throw new Error('The series table has no show to expand');
  }

  await show.click();
  const season = page.getByRole('button', { name: /^Expand Season / }).first();

  if ((await season.count()) === 0) {
    throw new Error('The expanded show has no season to expand');
  }

  await season.click();
  // Felix the Cat's seasons are numbered by year, as TheTVDB has them.
  await page.getByText(/ - S\d+E01/).first().waitFor();
}

async function expectTitle(page, title, expected) {
  const count = await page.getByText(title, { exact: true }).count();

  if ((count > 0) !== expected) {
    throw new Error(
      `Expected ${expected ? 'to see' : 'not to see'} "${title}" in the selected catalog kind`
    );
  }
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
    const beforeSize = a.viewportSize();
    await a.setViewportSize({ width: beforeSize.width, height: 1200 });
    // The card's whole face is a button laid over its title.
    await a.getByText('Sonarr', { exact: true }).first().click({ force: true });
    await a.waitForSelector('[role="dialog"], .modal, [class*="Modal"]');
    await a.waitForTimeout(600);
    const automatic = a.locator('input[name="fetchNewFilesAutomatically"]');
    const upgrades = a.locator('input[name="includeAutomaticUpgrades"]');
    const enabled = await automatic.isChecked();
    await upgrades.waitFor({ state: enabled ? 'visible' : 'hidden' });
    await automatic.locator('..').click();
    await upgrades.waitFor({ state: enabled ? 'hidden' : 'visible' });
    await automatic.locator('..').click();
    await upgrades.waitFor({ state: enabled ? 'visible' : 'hidden' });
    await shoot(a.getByRole('dialog'), 'settings-apps-sonarr');
    await a.keyboard.press('Escape');
    await a.setViewportSize(beforeSize);
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
    await hidden.getByPlaceholder('Filter').fill('Night');
    await a.waitForTimeout(300);
    await shoot(hidden, 'sharing-hidden');
    await hidden.getByPlaceholder('Filter').fill('');
  },

  async catalog({ a }) {
    await open(a, A, '/catalog/peers');
    await a.getByRole('button', { name: 'Series', exact: true }).click();
    await a.getByText('Out of the Inkwell (1919)', { exact: true }).waitFor();
    await a.getByRole('button', { name: 'Movies', exact: true }).click();
    await a.getByText('The Little Shop of Horrors (1960)', { exact: true }).waitFor();
    await expectTitle(a, 'Out of the Inkwell (1919)', false);
    await a.getByRole('button', { name: 'Series', exact: true }).click();
    await a.getByText('Out of the Inkwell (1919)', { exact: true }).waitFor();
    await expandFirstSeries(a);
    await shoot(a, 'peer-catalog');
    await a.getByRole('button', { name: 'Get it', exact: true }).last().click();
    await a.locator('select[name="qualityProfileId"]').waitFor({ state: 'visible' });
    await a.waitForLoadState('networkidle');
    await shoot(a, 'peer-catalog-get-it', { chrome: true });
    await a.keyboard.press('Escape');
    await a.getByText('Posters', { exact: true }).click();
    await a.getByText('Out of the Inkwell', { exact: true }).waitFor();
    await a.waitForTimeout(1200);
    const seriesGet = a.getByRole('button', { name: /^Get Felix the Cat/i });
    await seriesGet.hover();
    await shoot(a, 'peer-catalog-posters');
    await seriesGet.click();
    await a.locator('select[name="catalogGetItScope"]').waitFor();
    if (await a.locator('select[name="catalogGetItScope"]').inputValue() !== 'title') {
      throw new Error('The peer poster icon did not select the whole series');
    }
    await a.keyboard.press('Escape');
    await a.getByRole('button', { name: 'Movies', exact: true }).click();
    await a.waitForLoadState('networkidle');
    await a.waitForTimeout(1200);
    await expectTitle(a, 'The Little Shop of Horrors', true);
    await expectTitle(a, 'Out of the Inkwell', false);
    const movieGet = a.getByRole('button', { name: /^Get Metropolis/ });
    await movieGet.hover();
    await shoot(a, 'peer-catalog-movies-posters');
    await movieGet.click();
    await a.locator('select[name="qualityProfileId"]').waitFor();
    await a.keyboard.press('Escape');
    await a.getByText('Table', { exact: true }).click();
    await open(a, A, '/catalog/local');
    await a.getByRole('button', { name: 'Series', exact: true }).click();
    await a.getByText('Out of the Inkwell (1919)', { exact: true }).waitFor();
    await a.getByRole('button', { name: 'Movies', exact: true }).click();
    await a.getByText('Night of the Living Dead (1968)', { exact: true }).waitFor();
    await expectTitle(a, 'Out of the Inkwell (1919)', false);
    await a.getByRole('button', { name: 'Series', exact: true }).click();
    await a.getByText('Out of the Inkwell (1919)', { exact: true }).waitFor();
    await expandFirstSeries(a);
    await shoot(a, 'local-catalog');
    await a.getByText('Posters', { exact: true }).click();
    await a.getByText('Out of the Inkwell', { exact: true }).waitFor();
    await a.waitForTimeout(1200);
    await shoot(a, 'local-catalog-posters');
    await a.getByRole('button', { name: 'Movies', exact: true }).click();
    await a.waitForLoadState('networkidle');
    await a.waitForTimeout(1200);
    await expectTitle(a, 'Night of the Living Dead', true);
    await expectTitle(a, 'Out of the Inkwell', false);
    await shoot(a, 'local-catalog-movies-posters');
    await a.getByText('Table', { exact: true }).click();
  },

  // The peer cards use the same Get it workflow as their rows and tiles. Changing the card
  // from a series to an episode must also change the selection supplied to that workflow.
  async peerCards({ a }) {
    await open(a, A, '/catalog/peers');
    await a.getByRole('button', { name: 'Series', exact: true }).click();
    const postersButton = a.getByRole('button', { name: 'Posters', exact: true });
    if (await postersButton.isEnabled()) await postersButton.click();
    await a.getByRole('button', { name: /^Details: Felix the Cat/i }).first().click();
    const card = a.getByRole('dialog').first();
    await card.getByRole('button', { name: 'Get it', exact: true }).waitFor();
    await shoot(card, 'peer-catalog-details');
    await card.getByRole('button', { name: 'Get it', exact: true }).click();
    const scope = a.locator('select[name="catalogGetItScope"]');
    await scope.waitFor();
    if (await scope.inputValue() !== 'title') throw new Error('Series card Get it did not select the whole series');
    await scope.selectOption('season');
    const season = a.locator('select[name="seasonNumber"]');
    await season.waitFor();
    if (await season.inputValue() !== '1919') throw new Error('Series card Get it did not select the offered season');
    await a.keyboard.press('Escape');
    await card.getByRole('button', { name: /^S1919E02/ }).click();
    await card.getByRole('button', { name: 'Get it', exact: true }).waitFor();
    const request = a.waitForRequest((r) => r.method() === 'GET' && /\/catalog\/peers\/items\/\d+\/getit/.test(r.url()));
    await card.getByRole('button', { name: 'Get it', exact: true }).click();
    const episodePlan = new URL((await request).url());
    await scope.waitFor();
    if (await scope.inputValue() !== 'file' || episodePlan.searchParams.get('scope') !== 'file') {
      throw new Error('Episode card Get it did not select its file');
    }
    await a.keyboard.press('Escape');
    await a.keyboard.press('Escape');
    await a.getByRole('button', { name: 'Movies', exact: true }).click();
    await a.getByRole('button', { name: /^Details: Metropolis/ }).first().click();
    await card.getByRole('button', { name: 'Get it', exact: true }).click();
    await a.locator('select[name="qualityProfileId"]').waitFor();
    await a.keyboard.press('Escape');
    await a.keyboard.press('Escape');
    await a.getByRole('button', { name: /^Details: The Little Shop of Horrors/ }).first().click();
    await card.getByText('Have', { exact: true }).waitFor();
    if (await card.getByRole('button', { name: 'Get it', exact: true }).count()) {
      throw new Error('A held movie card still offers Get it');
    }
    await a.keyboard.press('Escape');
  },

  async settings({ a }) {
    await open(a, A, '/settings/node');
    await shoot(a, 'settings-node');
    await open(a, A, '/system/backup');
    await shoot(a, 'backup');
  },

  // B sends A a Felix the Cat short. A's Home shows it, with Get it. B's uploads are slowed for a moment, so the
  // transfer that Get it starts is still running when Activity is taken. It also checks the show,
  // season, file and poster actions preselect their intended share scope.
  async send({ a, b }) {
    await open(b, B, '/catalog/local');
    await selectCatalogKind(b, 'Series');
    await b.getByText(/Felix the Cat/i).first().waitFor();

    const showRow = b.getByRole('row').filter({ hasText: /Felix the Cat/i }).first();
    let dialog = await openCatalogSend(b, showRow);
    const wholeSeries = await selectedCheckboxLabels(dialog);
    if (wholeSeries.length !== 1 || !/Felix the Cat/i.test(wholeSeries[0])) {
      throw new Error(`The show action did not select the whole series: ${wholeSeries.join(', ')}`);
    }
    await b.keyboard.press('Escape');
    await dialog.waitFor({ state: 'detached' });

    await b.getByRole('button', { name: /^Expand .*Felix the Cat/i }).click();
    const seasonRow = b.getByRole('row').filter({ hasText: /Season 1919/ }).first();
    dialog = await openCatalogSend(b, seasonRow);
    const selectedSeason = await dialog.locator('input[type="checkbox"]:checked').first().evaluate((input) => input.closest('label')?.innerText.trim() ?? '');
    if (!/Season 1919/.test(selectedSeason)) {
      throw new Error(`The season action did not select Season 1919: ${selectedSeason}`);
    }
    await b.keyboard.press('Escape');
    await dialog.waitFor({ state: 'detached' });

    await b.getByRole('button', { name: /^Expand Season 1919/ }).click();
    const episodeRow = b.getByRole('row').filter({ hasText: /Felix the Cat - S1919E01/i }).first();
    dialog = await openCatalogSend(b, episodeRow);
    const selectedEpisode = await selectedCheckboxLabels(dialog);
    if (selectedEpisode.length !== 1 || !/^1\./.test(selectedEpisode[0])) {
      throw new Error(`The file action did not select its episode: ${selectedEpisode.join(', ')}`);
    }
    await dialog.locator('select[name="recipient"]').selectOption({ label: 'Alex' });
    await b.waitForTimeout(300);
    await shoot(dialog, 'send-to-a-friend');
    const episodeRequest = waitForSendRequest(b);
    await dialog.getByRole('button', { name: 'Send', exact: true }).click();
    const episodeBody = await episodeRequest;
    if (episodeBody.items.length !== 1 || episodeBody.items[0].mediaKind !== 'tv' || !episodeBody.items[0].episodeArrId) {
      throw new Error(`The file send did not contain the selected episode: ${JSON.stringify(episodeBody.items)}`);
    }

    await open(a, A, '/');
    await a.getByText(/Sam sent you Felix/i).waitFor({ timeout: 60000 });
    await a.waitForTimeout(800);
    await shoot(a, 'received');

    // A reused kit may already have this title. Keep the Home card in the shot, and only start a
    // transfer when this node does not already have the title.
    if (!(await hasLocalShareableTitle(a, 'Felix the Cat'))) {
      const felixCard = a
        .getByText(/Sam sent you Felix/i)
        .locator('xpath=../../..');
      await slowedTransfer(a, b, () =>
        felixCard.getByRole('button', { name: 'Get it', exact: true }).click()
      );
      await open(a, A, '/');
      await a.getByLabel(/\d+ unfinished downloads?/).first().waitFor({
        state: 'detached',
        timeout: 300000,
      });
    }

    await open(a, A, '/catalog/local');
    await selectCatalogKind(a, 'Movies');
    const posters = a.getByRole('button', { name: 'Posters', exact: true });
    if (!(await posters.isDisabled())) {
      await posters.click();
    }
    await a.getByText('Night of the Living Dead', { exact: true }).waitFor();
    await a.getByTitle('Send Night of the Living Dead (1968) to a friend').click();
    dialog = a.getByRole('dialog');
    await dialog.waitFor();
    // Its selection arrives with the send form's titles, a moment after the dialog opens.
    await dialog.locator('input[type="checkbox"]:checked').first().waitFor();
    const selectedMovie = await selectedCheckboxLabels(dialog);
    if (selectedMovie.length !== 1 || !selectedMovie[0].includes('Night of the Living Dead')) {
      throw new Error(`The poster action did not select the whole movie: ${selectedMovie.join(', ')}`);
    }
    await dialog.locator('select[name="recipient"]').selectOption({ label: 'Sam' });
    const movieRequest = waitForSendRequest(a);
    await dialog.getByRole('button', { name: 'Send', exact: true }).click();
    const movieBody = await movieRequest;
    if (movieBody.items.length !== 1 || movieBody.items[0].mediaKind !== 'movie' || 'episodeArrId' in movieBody.items[0] || 'seasonNumber' in movieBody.items[0]) {
      throw new Error(`The poster send did not contain a whole movie: ${JSON.stringify(movieBody.items)}`);
    }

    await open(b, B, '/');
    await b.getByText(/Alex sent you Night of the Living Dead/).waitFor({ timeout: 60000 });
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

async function selectCatalogKind(page, kind) {
  const table = page.getByRole('button', { name: 'Table', exact: true });
  if (!(await table.isDisabled())) {
    await table.click();
  }

  const button = page.getByRole('button', { name: kind, exact: true });
  if (!(await button.isDisabled())) {
    await button.click();
  }
}

async function openCatalogSend(page, row) {
  await row.locator('button[title^="Send "]').click();
  const dialog = page.getByRole('dialog');
  await dialog.waitFor();
  await dialog.locator('input[type="checkbox"]:checked').first().waitFor();
  return dialog;
}

async function selectedCheckboxLabels(dialog) {
  return dialog.locator('input[type="checkbox"]:checked').evaluateAll((inputs) =>
    inputs.map((input) => input.closest('label')?.innerText.trim() ?? '')
  );
}

async function hasLocalShareableTitle(page, title) {
  return page.evaluate(async (expectedTitle) => {
    const response = await fetch('/api/v5/sharing/titles', {
      headers: { 'X-Api-Key': window.Nodearr.apiKey },
    });
    const titles = await response.json();
    // TheTVDB writes some titles with other capitals, as "Felix The Cat".
    return titles.some((item) => item.title.toLowerCase() === expectedTitle.toLowerCase() && item.files > 0);
  }, title);
}

function waitForSendRequest(page) {
  return page.waitForRequest((request) =>
    request.method() === 'POST' && new URL(request.url()).pathname.endsWith('/sharing/sends')
  ).then((request) => request.postDataJSON());
}

// B's uploads are slowed while start() starts a transfer to A, so it is still running when
// Activity is taken. B's limit is put back however it ends.
async function slowedTransfer(a, b, start) {
  const before = await nodeSettings(b, { uploadLimitKbps: 400 });
  console.log("B's uploads slowed; waiting for a transfer to A");
  try {
    await start();
    await a.getByLabel(/\d+ unfinished downloads?/).first().waitFor({
      timeout: 120000,
    });
    await open(a, A, '/activity/transfers');
    await a.getByText('Downloading').first().waitFor({ timeout: 120000 });
    await a.getByLabel(/\d+ unfinished downloads?/).first().waitFor();
    await a.waitForTimeout(4000);
    await open(a, A, '/activity/transfers');
    await shoot(a, 'transfers', { chrome: true });
  } finally {
    await nodeSettings(b, { uploadLimitKbps: before.uploadLimitKbps });
  }
}

const wanted = process.argv.slice(2);
const browser = await chromium.launch({ executablePath: process.env.NODEARR_SCREENSHOT_BROWSER });
// Twice the pixels, so text stays sharp when the guide shows a screenshot smaller than it was.
const options = { viewport: { width: 1280, height: 800 }, deviceScaleFactor: 2, colorScheme: 'dark' };
const contextA = await browser.newContext(options);
const contextB = await browser.newContext(options);
await swapPosters(contextA, setUp('a', A, 'Alex'));
await swapPosters(contextB, setUp('b', B, 'Sam'));
const a = await signIn(contextA, A);
const b = await signIn(contextB, B);

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
