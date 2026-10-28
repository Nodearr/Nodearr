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
// only those pages. `catalogAvailability` needs the mixed library states described above its
// implementation and is run explicitly. `receivedDetails` likewise needs the five prepared sends.
// `receivedAvailability` needs A to hold Little Shop, Felix E01/E02 but not E03, and the original
// Metropolis while B offers its PROPER. It creates and puts away four sends without downloading.
// The "send" screenshot changes the test data: Sam sends Alex a Felix the Cat
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
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { musicKit, musicManifest, prepareMusic } from './ui-test-music.mjs';

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

if (process.argv[2] === 'prepareMusic') {
  await prepareMusic(kit, { a: setUp('a', A, 'Alex'), b: setUp('b', B, 'Sam') });
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
      if (['artist', 'album'].includes(subject.toLowerCase())) {
        title = (await node.api('GET', `/catalog/music/local/${subject}/${applicationId}/${arrId}`)).title;
      }
      const kind = { series: 'tv', movie: 'movie' }[subject.toLowerCase()];
      const titles = await node.api('GET', '/sharing/titles');
      title ??= titles.find((t) => `${t.applicationId}` === applicationId && `${t.arrId}` === arrId && t.mediaKind === kind)?.title;
    } else if (parts[at + 1] === 'peer') {
      const items = await node.api('GET', '/catalog/peers/items?page=1&pageSize=1000');
      title = items.records.find((i) => `${i.id}` === parts[at + 2])?.title;
    }

    // Original geometric artwork, not the unverified cover art fetched by Lidarr.
    if (musicManifest.albums.some((album) => album.title === title || album.artist === title)) {
      return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: readFileSync(path.join(root, 'tools/music-fixtures/cover.svg')) });
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
async function shoot(target, name, { chrome = false, margin = 16 } = {}) {
  const page = target.page ? target.page() : target;
  const file = path.join(out, `${name}.png`);
  await page.evaluate(mask, kit);

  if (target === page) {
    const view = page.viewportSize();
    const clip = chrome ? undefined : { x: SIDEBAR, y: TOPBAR, width: view.width - SIDEBAR, height: view.height - TOPBAR };
    await page.screenshot({ path: file, animations: 'disabled', clip });
  } else {
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
  // Run after prepareMusic, before downloading into B. Deliberately requires exact fixture state.
  async music({ browser }) {
    const nodes = { a: setUp('a', A, 'Alex'), b: setUp('b', B, 'Sam') };
    const apps = musicKit(kit);
    const source = (await nodes.a.api('GET', '/catalog/music/peer?level=album')).records;
    assert.equal(source.length, 0, 'B must still have only an incomplete album');
    const offered = (await nodes.b.api('GET', '/catalog/music/peer?level=album')).records;
    assert.equal(offered.length, musicManifest.albums.length);
    assert.equal(new Set(offered.map((entry) => entry.key)).size, musicManifest.albums.length);
    const lofi = offered.find((entry) => entry.title === 'Lofi Ftw');
    assert(lofi);
    const partial = await nodes.b.api('GET', lofi.path);
    assert.equal(partial.tracks.length, 9);
    assert.equal(partial.tracks.filter((track) => track.localStatus === 'upgrade').length, 1);
    assert.equal(partial.tracks.filter((track) => track.localStatus === 'missing').length, 8);
    const roots = (await nodes.b.api('GET', '/catalog/music/peer?level=artist')).records;
    assert.equal(roots.length, 2);
    const komiku = roots.find((entry) => entry.title === 'Komiku');
    const komikuAlbums = musicManifest.albums.filter((album) => album.artist === 'Komiku').length;
    assert.equal(komiku.albumCount, komikuAlbums);
    assert.equal(komiku.getIt.scope, 'artist');
    assert.equal(lofi.getIt.scope, 'file');
    for (const theme of ['light', 'dark']) {
      const context = await browser.newContext({ ...options, colorScheme: theme });
      context.setDefaultTimeout(15000);
      const failures = [];
      context.on('page', (page) => page.on('pageerror', (error) => failures.push(error.message)));
      try {
        for (const [side, base] of [['a', A], ['b', B]]) {
          await swapPosters(context, nodes[side]);
          const page = await signIn(context, base);
          for (const peer of [false, true]) {
            await open(page, base, `/catalog/${peer ? 'peers' : 'local'}`);
            await page.getByRole('button', { name: 'Music', exact: true }).click();
            const area = page.getByRole('region', { name: 'Music', exact: true });
            for (const [level, label] of [['artist', 'Artists'], ['album', 'Albums']]) {
              const toggle = area.getByRole('button', { name: label, exact: true });
              if (await toggle.isEnabled()) await toggle.click();
              const entries = (await nodes[side].api('GET', `/catalog/music/${peer ? 'peer' : 'local'}?level=${level}&pageSize=48`)).records;
              for (const view of ['Table', 'Posters']) {
                console.log(`music checking ${theme} ${side} ${peer ? 'peer' : 'local'} ${level} ${view}: ${entries.length} entries`);
                const control = page.getByRole('button', { name: view, exact: true });
                if (await control.isEnabled()) await control.click();
                for (const entry of entries) {
                  const details = area.getByRole('button', { name: `Details: ${entry.title}`, exact: true });
                  await details.first().waitFor(); // Poster and title intentionally open the same card.
                  assert.equal(await details.count(), 2);
                }
                assert.equal(await area.getByRole('button', { name: /^Send/ }).count(), entries.filter((entry) => entry.send).length);
                assert.equal(await area.getByRole('button', { name: /^Get (it|.*)/ }).count(), entries.filter((entry) => entry.getIt).length);
              }
            }
          }
          await page.close();
        }
        const page = await signIn(context, B);
        await open(page, B, '/catalog/peers');
        await page.getByRole('button', { name: 'Music', exact: true }).click();
        const area = page.getByRole('region', { name: 'Music', exact: true });
        const artists = area.getByRole('button', { name: 'Artists', exact: true });
        if (await artists.isEnabled()) await artists.click();
        await area.getByRole('button', { name: 'Details: Komiku', exact: true }).first().click();
        let card = page.getByRole('dialog', { name: 'Komiku', exact: true });
        await card.getByRole('button', { name: `Details: ${musicManifest.albums[1].title}`, exact: true }).waitFor();
        assert.equal(await card.locator('img').count(), komikuAlbums);
        if (theme === 'dark') await shoot(card, 'music-artist', { margin: 0 });
        await card.getByRole('button', { name: `Details: ${musicManifest.albums[1].title}`, exact: true }).click();
        card = page.getByRole('dialog', { name: musicManifest.albums[1].title, exact: true });
        await card.getByRole('region', { name: 'Songs', exact: true }).waitFor();
        assert.equal(await card.getByRole('region', { name: 'Songs', exact: true }).locator('li').count(), 13);
        assert.equal(await card.getByRole('region', { name: 'Songs', exact: true }).getByRole('button').count(), 0);
        await card.getByRole('button', { name: 'Back', exact: true }).click();
        await page.getByRole('dialog', { name: 'Komiku', exact: true }).waitFor();
        await page.keyboard.press('Escape');
        const albums = area.getByRole('button', { name: 'Albums', exact: true });
        await albums.click();
        await area.getByRole('button', { name: 'Details: Lofi Ftw', exact: true }).first().click();
        card = page.getByRole('dialog', { name: 'Lofi Ftw', exact: true });
        await card.getByRole('region', { name: 'Songs', exact: true }).waitFor();
        const songs = card.getByRole('region', { name: 'Songs', exact: true });
        assert.equal(await songs.getByText('Missing', { exact: true }).count(), 8);
        assert.equal(await songs.getByText('Upgrade', { exact: true }).count(), 1);
        if (theme === 'dark') await shoot(card, 'music-album', { margin: 0 });
        await page.keyboard.press('Escape');
        assert.deepEqual(failures, []);
        console.log(`music: ${theme} local/peer artist/album table/poster parity, cards, songs and availability passed`);
      } finally { await context.close(); }
    }
    // Real Home sends, with separate artist/album scope, not fabricated received records.
    const local = (await nodes.a.api('GET', '/catalog/music/local?level=artist')).records.find((entry) => entry.title === 'Komiku');
    const detail = await nodes.a.api('GET', local.path);
    const sentAlbum = detail.albums.find((entry) => entry.title === musicManifest.albums[1].title);
    const peer = await friendOf(nodes.a, nodes.b);
    for (const [entry, expected] of [[local, 'artist'], [sentAlbum, 'album']]) {
      const before = new Set((await nodes.b.api('GET', '/sharing/sends/received')).map((note) => note.id));
      await nodes.a.api('POST', '/sharing/sends', { peerId: peer.id, items: [entry.send] });
      const note = await until('music send to reach Home', async () => {
        const added = (await nodes.b.api('GET', '/sharing/sends/received')).filter((note) => !before.has(note.id));
        assert(added.length <= 1, 'Duplicate received music cards');
        return added[0];
      });
      const received = await nodes.b.api('GET', `/sharing/sends/received/${note.id}/details`);
      assert.equal(received.kind, expected);
      assert.equal(expected === 'artist' ? received.albums.length : received.tracks.length, expected === 'artist' ? komikuAlbums : 13);
      await nodes.b.api('POST', `/sharing/sends/received/${note.id}/dismiss`);
    }
    // Download through Nodearr/Torznab/Lidarr, then compare imported bytes by recording ID.
    for (const entry of [komiku, lofi]) {
      const selection = entry.getIt;
      const route = `/catalog/peers/items/${selection.itemId}/getit`;
      const choices = await nodes.b.api('GET', `${route}?scope=${selection.scope}`);
      assert(!choices.plan.problem, choices.plan.problem);
      assert.equal(choices.plan.targets.length, 1);
      const queued = await nodes.b.api('POST', route, { ...selection, options: choices.plan.targets[0].defaults });
      await until('music Get it to finish', async () => {
        const state = await nodes.b.api('GET', `/command/${queued.id}`);
        assert.notEqual(state.status, 'failed', state.message);
        return state.status === 'completed';
      }, 300);
    }
    for (const fixture of musicManifest.albums) {
      const artist = await until(`Lidarr to add ${fixture.artist}`, async () => (await apps.b.api('GET', '/artist')).find((artist) => artist.foreignArtistId === fixture.artistId));
      const album = await until(`Lidarr to import ${fixture.title}`, async () => {
        const album = (await apps.b.api('GET', `/album?artistId=${artist.id}`)).find((album) => album.foreignAlbumId === fixture.albumId);
        if (!album) return false;
        const files = (await apps.b.api('GET', `/trackfile?artistId=${artist.id}`)).filter((file) => file.albumId === album.id);
        assert(files.length <= fixture.tracks, 'Duplicate imported songs');
        return files.length === fixture.tracks && files.every((file) => file.quality.quality.name === 'MP3-320') && album;
      }, 600);
      assert.equal(artist.monitorNewItems, fixture.artist === 'Komiku' ? 'all' : 'none');
      assert.equal(album.monitored, true);
      const sourceArtist = (await apps.a.api('GET', '/artist')).find((artist) => artist.foreignArtistId === fixture.artistId);
      const sourceAlbum = (await apps.a.api('GET', `/album?artistId=${sourceArtist.id}`)).find((album) => album.foreignAlbumId === fixture.albumId);
      const fingerprints = async (app, artistId, albumId) => {
        const files = (await app.api('GET', `/trackfile?artistId=${artistId}`)).filter((file) => file.albumId === albumId);
        return files.map((file) => createHash('sha256').update(readFileSync(path.join(app.folder, `music-${app.side}`, file.path.replace(/^\/music\//, '')))).digest('hex')).sort();
      };
      assert.deepEqual(await fingerprints(apps.a, sourceArtist.id, sourceAlbum.id), await fingerprints(apps.b, artist.id, album.id));
      console.log(`music: ${fixture.artist} / ${fixture.title}: ${fixture.tracks} imported songs, matching SHA256`);
    }
  },
  // Requires the reused kit: A holds B's Little Shop, Felix E01/E02 but not E03,
  // and the original Metropolis; B offers its PROPER. Creates and puts away four sends.
  async receivedAvailability({ a, browser }) {
    const receiver = setUp('a', A, 'Alex');
    const sender = setUp('b', B, 'Sam');
    await open(a, A, '/system/status');
    const peer = (await sender.api('GET', '/nodes/peers')).find((peer) => peer.state === 'trusted' && peer.name === 'Alex');
    const titles = await sender.api('GET', '/sharing/titles');
    const felix = titles.find((title) => /Felix the Cat/i.test(title.title));
    const seasons = await sender.api('GET', `/sharing/titles/${felix.applicationId}/${felix.arrId}/seasons`);
    const episode = seasons.find((season) => season.seasonNumber === 1919).episodes.find((episode) => episode.episodeNumber === 1);
    const key = (title) => ({ mediaKind: title.mediaKind, applicationId: title.applicationId, arrId: title.arrId });
    const cases = [
      { title: 'The Little Shop of Horrors', held: true },
      { title: felix.title, held: true, episodeArrId: episode.episodeArrId },
      { title: felix.title, newFiles: 1 },
      { title: 'Metropolis', newFiles: 0, upgrades: 1 },
    ];
    const created = [];
    try {
      for (const scenario of cases) {
        const before = new Set((await receiver.api('GET', '/sharing/sends/received')).map((note) => note.id));
        const item = key(titles.find((title) => title.title === scenario.title));
        if (scenario.episodeArrId) item.episodeArrId = scenario.episodeArrId;
        const sent = await sender.api('POST', '/sharing/sends', { peerId: peer.id, items: [item] });
        await until('the sender to say Told', async () => (await sender.api('GET', '/sharing/sends')).find((note) => note.id === sent.id)?.status === 'told');
        scenario.note = await until('the real send to reach Home', async () => {
          const notes = (await receiver.api('GET', '/sharing/sends/received')).filter((note) => !before.has(note.id));
          if (notes.length > 1) throw new Error('A send created duplicate received cards');
          return notes[0];
        });
        created.push(scenario.note.id);
        if (scenario.note.alreadyHaveIt !== !!scenario.held || scenario.note.alreadyHeldSeenAt) throw new Error('Incorrect initial holdings or seen state');
        if (!scenario.held && (scenario.note.newFiles !== scenario.newFiles || scenario.note.upgradeFiles !== (scenario.upgrades ?? 0))) throw new Error('Incorrect new/upgrade file counts');
      }

      const offscreen = await browser.newContext(options);
      await swapPosters(offscreen, receiver);
      await offscreen.addInitScript(() => document.addEventListener('DOMContentLoaded', () => {
        const style = document.createElement('style');
        style.id = 'offscreen-check';
        style.textContent = '[class*="receivedWithPoster"] { margin-top: 2000px !important; }';
        document.head.append(style);
      }));
      const page = await signIn(offscreen, A);
      await open(page, A, '/');
      await page.getByText('You already have it.', { exact: true }).first().waitFor({ state: 'attached' });
      await page.waitForTimeout(5500); // Includes a background received-card poll.
      if ((await receiver.api('GET', '/sharing/sends/received')).filter((note) => created.includes(note.id)).some((note) => note.alreadyHeldSeenAt)) throw new Error('Offscreen cards were marked seen');
      // Exercise the browser visibility event separately from geometric visibility.
      await page.evaluate(() => Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' }));
      await page.locator('#offscreen-check').evaluate((style) => style.remove());
      await page.locator(`[data-received-id="${cases[0].note.id}"]`).scrollIntoViewIfNeeded();
      await page.waitForTimeout(5500);
      if ((await receiver.api('GET', '/sharing/sends/received')).filter((note) => created.includes(note.id)).some((note) => note.alreadyHeldSeenAt)) throw new Error('A hidden document marked a card seen');
      await page.evaluate(() => {
        delete document.visibilityState;
        document.dispatchEvent(new Event('visibilitychange'));
      });
      await until('foreground visibility event to mark the card', async () => (await receiver.api('GET', '/sharing/sends/received')).find((note) => note.id === cases[0].note.id)?.alreadyHeldSeenAt);
      await offscreen.close();

      for (const theme of ['light', 'dark']) {
        const context = await browser.newContext({ ...options, colorScheme: theme });
        await swapPosters(context, receiver);
        const planRequests = [];
        context.on('request', (request) => { if (/\/received\/\d+\/getit/.test(request.url())) planRequests.push(request.url()); });
        const home = await signIn(context, A);
        await open(home, A, '/');
        for (const scenario of cases) {
          const note = scenario.note;
          const card = home.locator(`[data-received-id="${note.id}"]`);
          await card.scrollIntoViewIfNeeded();
          if (scenario.held) {
            await card.getByText('You already have it.', { exact: true }).waitFor();
            if (await card.getByRole('button', { name: 'Get it', exact: true }).count()) throw new Error('An already-held card offered Get it');
            if (planRequests.some((url) => url.includes(`/received/${note.id}/getit`))) throw new Error('An already-held card fetched an unnecessary plan');
            await until('visible notification to be marked seen', async () => (await receiver.api('GET', '/sharing/sends/received')).find((item) => item.id === note.id)?.alreadyHeldSeenAt);
            if (theme === 'dark' && note.mediaKind === 'movie') await shoot(card, 'received-already-held', { margin: 0 });
          } else {
            await card.getByText(`New files: ${scenario.newFiles}.`, { exact: !scenario.upgrades }).waitFor();
            if (scenario.upgrades) await card.getByText(/Upgrades: 1\./).waitFor();
            await card.getByRole('button', { name: 'Get it', exact: true }).waitFor();
            if (theme === 'dark' && note.mediaKind === 'tv') await shoot(card, 'received-partly-held', { margin: 0 });
          }
        }
        await context.close();
        console.log(`receivedAvailability: ${theme} held movie/episode, missing episode, PROPER upgrade and visible-only acknowledgement passed`);
      }
      const firstSeen = (await receiver.api('GET', '/sharing/sends/received')).filter((note) => created.includes(note.id) && note.alreadyHaveIt);
      for (const note of firstSeen) {
        await receiver.api('POST', `/sharing/sends/received/${note.id}/seen`);
        const again = (await receiver.api('GET', '/sharing/sends/received')).find((item) => item.id === note.id);
        if (again.alreadyHeldSeenAt !== note.alreadyHeldSeenAt) throw new Error('A repeat visit reset the first-seen time');
      }
      await open(a, A, '/');
      const held = cases[0].note;
      const heldCard = a.locator(`[data-received-id="${held.id}"]`);
      await heldCard.getByRole('button', { name: 'Put away', exact: true }).click();
      await heldCard.waitFor({ state: 'detached' });
      console.log('receivedAvailability: Put away removed the held card; sender remains Told');
    } finally {
      for (const id of created) await receiver.api('POST', `/sharing/sends/received/${id}/dismiss`);
    }
  },

  // Browser-only responses exercise every activity state without changing the kit's jobs.
  // The real sending/receiving check and guide capture use slowedTransfer below.
  async activityStatus({ browser }) {
    for (const theme of ['light', 'dark']) {
      const context = await browser.newContext({ ...options, colorScheme: theme });
      const page = await signIn(context, A);
      let downloads = [];
      let uploads = [];
      await page.route(/\/api\/v5\/transfers(?:\/uploads)?(?:\?|$)/, (route) => route.fulfill({
        json: new URL(route.request().url()).pathname.endsWith('/uploads') ? uploads : downloads,
      }));
      const download = (state, extra = {}) => ({ id: 101, downloadId: 'activity-check', title: 'Download check', peerName: 'Sam', mediaKind: 'tv', state, failure: 'None', attempts: 0, size: 1000, bytesDone: 500, added: '2026-10-25T12:00:00Z', ...extra });
      const upload = (state, extra = {}) => ({ id: 'activity-upload', title: 'Upload check', peerName: 'Sam', state, attempts: 1, size: 1000, bytesSent: 500, started: '2026-10-25T12:00:00Z', ...extra });
      const cases = [
        { downloads: [], uploads: [], description: null },
        { downloads: [download('downloading')], uploads: [upload('sending')], description: '1 downloading, 1 sending', directions: [['download', '1'], ['upload', '1']], kinds: ['info', 'info'], kind: 'info' },
        { downloads: [download('queued')], uploads: [upload('waiting', { queuePosition: 1 })], description: '2 waiting', directions: [['download', '1'], ['upload', '1']], kinds: ['warning', 'warning'], kind: 'warning' },
        { downloads: [download('downloading', { waitingPosition: 1 })], uploads: [], description: '1 waiting', directions: [['download', '1']], kinds: ['warning'], kind: 'warning' },
        { downloads: [download('queued', { attempts: 1 })], uploads: [upload('preparing', { attempts: 2, lastFailure: 'ConnectionLost' })], description: '2 retrying', directions: [['download', '1'], ['upload', '1']], kinds: ['warning', 'warning'], kind: 'warning' },
        { downloads: [download('failed')], uploads: [upload('waiting')], description: '1 waiting, 1 failed', directions: [['download', '1'], ['upload', '1']], kinds: ['danger', 'warning'], kind: 'danger' },
        { downloads: [download('completed')], uploads: [upload('failed')], description: null, kinds: ['success', 'danger'] },
        { downloads: [], uploads: [upload('completed')], description: null, kinds: ['success'] },
        { downloads: [], uploads: [upload('sending')], description: '1 sending', directions: [['upload', '1']], kinds: ['info'], kind: 'info' },
      ];
      for (const scenario of cases) {
        downloads = scenario.downloads;
        uploads = scenario.uploads;
        await open(page, A, '/activity/transfers');
        const badge = page.getByRole('link', { name: /^Activity/ }).locator('span[aria-label]');
        if (scenario.description === null) {
          if (await badge.count()) throw new Error('Finished/history-only activity kept a badge');
        } else {
          if (await badge.getAttribute('title') !== scenario.description || JSON.stringify(await badge.locator('svg').evaluateAll((icons) => icons.map((icon) => [icon.dataset.icon, icon.parentElement.textContent]))) !== JSON.stringify(scenario.directions)) {
            throw new Error(`Incorrect ${theme} activity badge: ${await badge.textContent()}, ${await badge.getAttribute('title')}`);
          }
          await checkColor(badge, scenario.kind);
        }
        const labels = page.getByRole('row').filter({ hasText: /(?:Download|Upload) check/ }).locator('td:nth-child(3) > span');
        for (const [index, kind] of (scenario.kinds ?? []).entries()) await checkColor(labels.nth(index), kind);
      }
      // The always-mounted sidebar must also show sending while another page is open.
      await open(page, A, '/');
      await page.getByLabel('1 sending', { exact: true }).waitFor();
      await context.close();
      console.log(`activityStatus: ${theme} directions, waiting, retries, failure precedence, history exclusion and row colors passed`);

      async function checkColor(label, kind) {
        const expected = await page.evaluate((kind) => {
          const color = getComputedStyle(document.documentElement).getPropertyValue(`--${kind}Color`).trim();
          const swatch = document.createElement('span');
          swatch.style.backgroundColor = color;
          document.body.append(swatch);
          const resolved = getComputedStyle(swatch).backgroundColor;
          swatch.remove();
          return resolved;
        }, kind);
        const actual = await label.evaluate((element) => getComputedStyle(element).backgroundColor);
        if (actual !== expected) throw new Error(`${kind} activity label used ${actual}, expected ${expected}`);
      }
    }
  },

  // Hold the real startup request so the transient splash can be checked and captured.
  // The broken health item exercises the actual React error boundary without changing data.
  async branding({ browser }) {
    for (const theme of ['light', 'dark']) {
      const context = await browser.newContext({ ...options, colorScheme: theme });
      const page = await signIn(context, A);
      let releaseStatus;
      const statusHeld = new Promise((resolve) => { releaseStatus = resolve; });
      await page.route('**/api/v5/system/status*', async (route) => {
        await statusHeld;
        await route.continue();
      });

      try {
        await page.goto(A, { waitUntil: 'domcontentloaded' });
        await page.getByText('Loading Nodearr…', { exact: true }).waitFor();
        const logo = page.getByRole('img', { name: 'Nodearr', exact: true });
        await logo.waitFor();
        if (!(await logo.evaluate((image) => image.complete && image.naturalWidth > 0 && image.src.endsWith('/Content/Images/logo.svg')))) {
          throw new Error(`The ${theme} splash did not load Nodearr's logo`);
        }
        const background = await page.locator('html').evaluate((element) => getComputedStyle(element).backgroundColor);
        const expected = theme === 'dark' ? 'rgb(32, 32, 32)' : 'rgb(245, 249, 247)';
        if (background !== expected) throw new Error(`The ${theme} splash used ${background}`);
        await shoot(page, `loading-${theme}`, { chrome: true });
      } finally {
        releaseStatus();
      }

      await page.getByText('Loading Nodearr…', { exact: true }).waitFor({ state: 'detached' });
      await page.unroute('**/api/v5/system/status*');
      await open(page, A, '/page-that-does-not-exist');
      await page.getByText('You must be lost, nothing to see here.', { exact: true }).waitFor();
      if (!(await page.title()).startsWith('Page not found - ')) throw new Error('Incorrect 404 tab title');
      const mark = page.locator('img[src$="/Content/Images/logo.svg"]');
      if (await mark.count() < 2) throw new Error('The 404 page is missing its Nodearr mark');
      await shoot(page, `not-found-${theme}`);

      await page.route('**/api/v5/health*', (route) => route.fulfill({
        json: [{ source: 'BrandingCheck', message: 'Test error', type: null }],
      }));
      await open(page, A, '/system/status');
      await page.getByText('There was an error loading this page', { exact: true }).waitFor();
      if (await mark.count() < 2) throw new Error('The error page is missing its Nodearr mark');
      await page.locator('details').click({ position: { x: 4, y: 7 } });
      await page.getByText(/Version: /).waitFor();
      await page.locator('details').click({ position: { x: 4, y: 7 } });
      await shoot(page, `error-${theme}`);
      await page.unroute('**/api/v5/health*');

      // All routed pages inherit the configured instance name in the browser tab.
      for (const route of ['/', '/catalog/local', '/catalog/peers', '/catalog/sharing', '/nodes/trusted', '/nodes/invites', '/activity/transfers', '/settings', '/settings/apps', '/settings/node', '/settings/general', '/system/status', '/system/tasks', '/system/backup', '/system/events', '/system/logs/files']) {
        await open(page, A, route);
        const instance = await page.evaluate(() => window.Nodearr.instanceName);
        const title = await page.title();
        if (!title.endsWith(instance) || /Sonarr/.test(title)) throw new Error(`Incorrect tab title at ${route}: ${title}`);
      }
      await context.close();
      console.log(`branding: ${theme} loading, 404, error details and routed tab titles passed`);
    }
  },

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
    await selectCatalogKind(a, 'Series');
    await a.getByText('Out of the Inkwell (1919)', { exact: true }).waitFor();
    await a.getByRole('button', { name: 'Movies', exact: true }).click();
    await a.getByText('The Little Shop of Horrors (1960)', { exact: true }).waitFor();
    await expectTitle(a, 'Out of the Inkwell (1919)', false);
    await a.getByRole('button', { name: 'Series', exact: true }).click();
    await a.getByText('Out of the Inkwell (1919)', { exact: true }).waitFor();
    await expandFirstSeries(a);
    await shoot(a, 'peer-catalog');
    await a.getByRole('button', { name: 'Get it', exact: true }).last().click();
    await a.getByRole('dialog').last().getByRole('button', { name: 'Get it', exact: true }).waitFor();
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
    await a.getByRole('dialog').last().getByRole('button', { name: 'Get it', exact: true }).waitFor();
    await a.keyboard.press('Escape');
    await a.getByText('Table', { exact: true }).click();
    await open(a, A, '/catalog/local');
    await selectCatalogKind(a, 'Series');
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
    await card.getByRole('button', { name: /^S1919E03/ }).click();
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
    await a.getByRole('dialog').last().getByRole('button', { name: 'Get it', exact: true }).waitFor();
    await a.keyboard.press('Escape');
    await a.keyboard.press('Escape');
    await a.getByRole('button', { name: /^Details: The Little Shop of Horrors/ }).first().click();
    await card.getByText('Have', { exact: true }).waitFor();
    if (await card.getByRole('button', { name: 'Get it', exact: true }).count()) {
      throw new Error('A held movie card still offers Get it');
    }
    await a.keyboard.press('Escape');
  },

  // Details are read from the received offer. Compare them with the sender's local cards,
  // then check the visible series, selected episode and movie rather than only their JSON.
  async peerMetadata({ a }) {
    const receiver = setUp('a', A, 'Alex');
    const sender = setUp('b', B, 'Sam');
    const titles = await receiver.api('GET', '/catalog/peers/titles?kind=series&pageSize=50');
    const title = titles.records.find((item) => /felix/i.test(item.title));
    if (!title) throw new Error('Sam must share Felix the Cat before checking peer metadata');
    const series = await receiver.api('GET', `/catalog/peers/details/series/${title.posterItemId}`);
    const shared = await sender.api('GET', '/sharing/titles');
    const owner = shared.find((item) => item.mediaKind === 'tv' && /felix/i.test(item.title));
    const local = await sender.api('GET', `/catalog/local/details/series/${owner.applicationId}/${owner.arrId}`);
    for (const field of ['overview', 'genres', 'network', 'runtime', 'tvdbId', 'tmdbId', 'imdbId', 'year']) {
      if (series[field] !== local[field]) throw new Error(`Peer series differs from local ${field}`);
    }
    if (!series.overview || !series.genres || !series.runtime) throw new Error('Series metadata is missing');
    const episode = await receiver.api('GET', series.episodes[0].path);
    const localEpisode = await sender.api('GET', local.episodes.find((item) =>
      item.seasonNumber === episode.seasonNumber && item.episodeNumber === episode.episodeNumber).path);
    for (const field of ['title', 'overview', 'airDate', 'resolution', 'quality', 'size']) {
      if (episode[field] !== localEpisode[field]) throw new Error(`Peer episode differs from local ${field}`);
    }
    for (const field of ['genres', 'network', 'runtime']) {
      if (episode[field] !== series[field]) throw new Error(`Episode lost parent ${field}`);
    }
    await open(a, A, '/catalog/peers');
    await a.getByRole('button', { name: 'Series', exact: true }).click();
    const posters = a.getByRole('button', { name: 'Posters', exact: true });
    if (await posters.isEnabled()) await posters.click();
    await a.getByRole('button', { name: /^Details: Felix the Cat/i }).first().click();
    const card = a.getByRole('dialog').first();
    async function visibleDetails(details) {
      if (!details.overview || !details.genres) throw new Error('Card needs description and genres');
      for (const value of [details.overview, details.genres, details.network, `${details.runtime} min`].filter(Boolean)) {
        await card.getByText(value, { exact: true }).waitFor();
      }
    }
    await visibleDetails(series);
    await shoot(card, 'peer-catalog-details');
    await card.getByRole('button', { name: /^S1919E01/ }).click();
    await visibleDetails(episode);
    await card.getByText(`${episode.resolution}p`, { exact: true }).waitFor();
    await shoot(card, 'peer-episode-details');
    await a.keyboard.press('Escape');
    const movies = await receiver.api('GET', '/catalog/peers/titles?kind=movie&pageSize=50');
    const movie = movies.records.find((item) => item.title === 'Metropolis');
    const film = await receiver.api('GET', `/catalog/peers/details/movie/${movie.posterItemId}`);
    const ownerMovie = shared.find((item) => item.title === 'Metropolis');
    const localMovie = await sender.api('GET', `/catalog/local/details/movie/${ownerMovie.applicationId}/${ownerMovie.arrId}`);
    for (const field of ['overview', 'genres', 'runtime', 'studio', 'releaseDate', 'resolution', 'quality', 'size', 'year', 'tmdbId', 'imdbId']) {
      if (film[field] !== localMovie[field]) throw new Error(`Peer movie differs from local ${field}`);
    }
    await a.getByRole('button', { name: 'Movies', exact: true }).click();
    await a.getByRole('button', { name: /^Details: Metropolis/ }).first().click();
    await visibleDetails(film);
    await card.getByText(`${film.resolution}p`, { exact: true }).waitFor();
    await shoot(card, 'peer-movie-details');
    await a.keyboard.press('Escape');
    console.log('Peer series, episode and movie cards match the sender metadata');
  },

  // Uses real mixed library states: Felix has both held and missing episodes on A;
  // Metropolis is an upgrade and Aladdin is outside A's library. No responses are mocked.
  async catalogAvailability({ a }) {
    const originalScheme = await a.evaluate(() => matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    if (await a.evaluate(() => window.Nodearr.theme) !== 'auto') {
      throw new Error('Use the kit with Nodearr__Theme=auto to check both system themes');
    }
    const keys = ['nodearr.localCatalog.showMissingEpisodes', 'nodearr.peerCatalog.showMissingEpisodes'];
    const preferences = await a.evaluate((names) => names.map((key) => localStorage.getItem(key)), keys);
    const colors = {};
    async function checkColors(scope, theme) {
      const seen = await scope.locator('[data-catalog-status]').evaluateAll((elements) => elements.map((element) => ({
        status: element.dataset.catalogStatus, color: getComputedStyle(element).color, text: element.textContent.trim(),
      })));
      for (const item of seen) {
        if (!item.text) throw new Error('Availability needs words as well as color');
        const key = `${theme}:${item.status}`;
        if (colors[key] && colors[key] !== item.color) throw new Error(`Inconsistent ${key} across catalog views`);
        colors[key] = item.color;
      }
    }
    async function table(route) {
      await open(a, A, route);
      await selectCatalogKind(a, 'Series');
    }
    try {
      for (const theme of ['dark', 'light']) {
        await a.emulateMedia({ colorScheme: theme });
        await a.evaluate((names) => names.forEach((key) => localStorage.removeItem(key)), keys);
        await table('/catalog/local');
        const toggle = a.getByRole('checkbox', { name: 'Show missing episodes', exact: true });
        if (await toggle.isChecked()) throw new Error('Local missing must default off');
        await a.getByRole('button', { name: 'Expand Out of the Inkwell', exact: true }).click();
        await a.getByRole('button', { name: 'Expand Season 1', exact: true }).click();
        await checkColors(a, theme);
        await toggle.check();
        await a.locator('[data-catalog-status="missing"]').first().waitFor();
        await checkColors(a, theme);
        await a.reload({ waitUntil: 'networkidle' });
        if (!(await toggle.isChecked())) throw new Error('Local missing choice was not remembered');
        await a.getByRole('button', { name: /^Details: Out of the Inkwell/ }).first().click();
        const card = a.getByRole('dialog').first();
        const cardToggle = card.getByRole('checkbox', { name: 'Show missing episodes', exact: true });
        if (!(await cardToggle.isChecked())) throw new Error('Local card did not share table preference');
        await checkColors(card, theme);
        await card.locator('li').filter({ has: a.locator('[data-catalog-status="missing"]') }).first().getByRole('button').click();
        await card.locator('[data-catalog-status="missing"]').waitFor();
        await checkColors(card, theme);
        await card.getByRole('button', { name: 'Back to series', exact: true }).click();
        await cardToggle.uncheck();
        await card.waitFor();
        if (await card.locator('[data-catalog-status="missing"]').count()) throw new Error('Local card still shows missing episodes');
        await checkColors(card, theme);
        await a.keyboard.press('Escape');
        await card.waitFor({ state: 'hidden' });
        await until('local card choice to update the table', async () => !(await toggle.isChecked()), 10);
        await table('/catalog/peers');
        if (!(await toggle.isChecked())) throw new Error('Peer missing must default on independently');
        await a.getByRole('button', { name: /^Expand Felix the Cat$/i }).click();
        await a.getByRole('button', { name: 'Expand Season 1919', exact: true }).click();
        await a.getByText('Missing', { exact: true }).last().waitFor();
        await checkColors(a, theme);
        await shoot(a, `catalog-availability-${theme}`);
        await a.getByRole('button', { name: /^Missing \(/ }).click();
        await a.getByRole('button', { name: /^Details: Felix the Cat/i }).first().click();
        if (!(await cardToggle.isChecked())) throw new Error('Peer card must default to showing missing');
        await card.locator('[data-catalog-status="missing"]').first().waitFor();
        await checkColors(card, theme);
        await card.locator('li').filter({ has: a.locator('[data-catalog-status="missing"]') }).first().getByRole('button').click();
        await card.locator('[data-catalog-status="missing"]').waitFor();
        await checkColors(card, theme);
        await card.getByRole('button', { name: 'Back to series', exact: true }).click();
        await cardToggle.uncheck();
        await card.waitFor();
        if (await card.locator('[data-catalog-status="missing"]').count()) throw new Error('Peer card still shows Missing');
        await a.keyboard.press('Escape');
        await card.waitFor({ state: 'hidden' });
        await until('peer card choice to update the table', async () => !(await toggle.isChecked()), 10);
        await a.reload({ waitUntil: 'networkidle' });
        if (await toggle.isChecked()) throw new Error('Peer missing choice was not remembered');
        await toggle.check();
        await a.getByRole('button', { name: /^All \(/ }).click();
        await a.getByRole('button', { name: 'Movies', exact: true }).click();
        await a.locator('[data-catalog-status="notInLibrary"]').first().waitFor();
        await a.locator('[data-catalog-status="upgrade"]').first().waitFor();
        await checkColors(a, theme);
        const posters = a.getByRole('button', { name: 'Posters', exact: true });
        await posters.click();
        await a.locator('[data-catalog-status="upgrade"]').first().waitFor();
        await checkColors(a, theme);
        await a.getByRole('button', { name: /^Details: Metropolis/ }).first().click();
        await card.locator('[data-catalog-status="upgrade"]').waitFor();
        await checkColors(card, theme);
        await a.keyboard.press('Escape');
        await card.waitFor({ state: 'hidden' });
        await open(a, A, '/catalog/local');
        const localPosters = a.getByRole('button', { name: 'Posters', exact: true });
        if (await localPosters.isEnabled()) await localPosters.click();
        await a.locator('[data-catalog-status="have"]').first().waitFor();
        await checkColors(a, theme);
        const palette = ['have', 'missing', 'upgrade', 'notInLibrary'].map((status) => colors[`${theme}:${status}`]);
        if (palette.some((color) => !color) || new Set(palette).size !== 4) throw new Error('All four states need distinct colors');
        if (theme === 'light' && ['have', 'missing', 'upgrade', 'notInLibrary'].some((status) => colors[`dark:${status}`] === colors[`light:${status}`])) {
          throw new Error('The theme did not change the availability palette');
        }
        console.log(`${theme}: consistent colors in tables, posters and cards; defaults and remembered filters passed`);
      }
    } finally {
      await a.emulateMedia({ colorScheme: originalScheme });
      await a.evaluate(({ names, values }) => names.forEach((key, i) => {
        if (values[i] == null) localStorage.removeItem(key); else localStorage.setItem(key, values[i]);
      }), { names: keys, values: preferences });
    }
  },

  // Prepare whole-series, season, multi-episode, single-episode and movie sends from B
  // to A first. This read-only check opens both controls and keeps Home as the current page.
  async receivedDetails({ a }) {
    const receiver = setUp('a', A, 'Alex');
    const notes = (await receiver.api('GET', '/sharing/sends/received')).filter((note) => note.detailsAvailable);
    const cases = [
      notes.find((note) => note.mediaKind === 'tv' && note.isWhole),
      notes.find((note) => /^Season /.test(note.summary ?? '')),
      notes.find((note) => note.summary === '2 episodes'),
      notes.find((note) => /^S\d+E\d+$/.test(note.summary ?? '')),
      notes.find((note) => note.mediaKind === 'movie'),
    ];
    if (cases.some((note) => !note)) throw new Error('Prepare all five received-send cases before running receivedDetails');
    await open(a, A, '/');
    const preference = await a.evaluate(() => localStorage.getItem('nodearr.peerCatalog.showMissingEpisodes'));
    await a.evaluate(() => localStorage.setItem('nodearr.peerCatalog.showMissingEpisodes', 'false'));
    try {
      for (const note of cases) {
        const what = note.summary ? `${note.title} - ${note.summary}` : note.title;
        const expected = await receiver.api('GET', `/sharing/sends/received/${note.id}/details`);
        for (const index of [1, 0]) {
          await a.getByRole('button', { name: `Details: ${what}`, exact: true }).nth(index).click();
          const card = a.getByRole('dialog', { name: expected.title, exact: true });
          await card.waitFor();
          if (new URL(a.url()).pathname !== '/') throw new Error('Received details navigated away from Home');
          const markers = card.locator('li').getByText('Sent to you', { exact: true });
          const sent = expected.episodes.filter((episode) => episode.wasSent);
          if (await markers.count() !== sent.length) throw new Error('The card did not mark every sent episode');
          for (const episode of sent) {
            const number = `S${String(episode.seasonNumber).padStart(2, '0')}E${String(episode.episodeNumber).padStart(2, '0')}`;
            await card.locator('li').filter({ has: a.getByRole('button', { name: new RegExp(`^${number}`) }) }).getByText('Sent to you', { exact: true }).waitFor();
          }
          if (expected.getIt) {
            await card.getByRole('button', { name: 'Get it', exact: true }).click();
            const getIt = a.getByRole('dialog', { name: /^Get it -/ });
            await getIt.getByRole('button', { name: 'Get it', exact: true }).waitFor();
            const scope = getIt.locator('select[name="catalogGetItScope"]');
            if (await scope.count() && await scope.inputValue() !== expected.getIt.scope) throw new Error('Wrong Get it selection from received details');
            await a.keyboard.press('Escape');
            await getIt.waitFor({ state: 'hidden' });
          }
          if (index === 1 && note.summary === '2 episodes') await shoot(card, 'received-details');
          await a.keyboard.press('Escape');
          await card.waitFor({ state: 'hidden' });
        }
      }
      const movie = cases[4];
      const homeCard = a.getByRole('button', { name: `Details: ${movie.title}`, exact: true }).first().locator('xpath=..');
      await shoot(homeCard, 'received');
      console.log('Whole, season, multiple-episode, single-episode and movie sends open from both Home controls, with correct markers and Get it');
    } finally {
      await a.evaluate((value) => {
        if (value == null) localStorage.removeItem('nodearr.peerCatalog.showMissingEpisodes');
        else localStorage.setItem('nodearr.peerCatalog.showMissingEpisodes', value);
      }, preference);
    }
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
    await a.getByLabel(/\d+ downloading/).first().waitFor({
      timeout: 120000,
    });
    await open(a, A, '/activity/transfers');
    await a.getByText('Downloading').first().waitFor({ timeout: 120000 });
    await a.getByLabel(/\d+ downloading/).first().waitFor();
    await b.goto(B + '/');
    await b.getByLabel(/\d+ sending/).first().waitFor();
    console.log('Sending is visible in the sender sidebar on Home during a real transfer');
    await a.waitForTimeout(4000);
    await open(a, A, '/activity/transfers');
    await a.getByLabel(/\d+ downloading/).first().waitFor();
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
    // These checks need the fixture states described above, so run them by name.
    if (wanted.includes(name) || (wanted.length === 0 && !['transfers', 'catalogAvailability', 'receivedDetails', 'receivedAvailability', 'music'].includes(name))) {
      await take({ a, b, browser });
    }
  }
} finally {
  await browser.close();
}
