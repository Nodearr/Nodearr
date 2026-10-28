// Optional music fixture for the existing two-node UI kit. No data or volumes are deleted.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const musicManifest = JSON.parse(readFileSync(path.join(here, 'music-fixtures/manifest.json'), 'utf8'));
const image = 'lscr.io/linuxserver/lidarr@sha256:d8549ff9ea29b4015c32257360652c1000c31ea471a2b3f0835ae77292faf35b';

function run(program, args) {
  return execFileSync(program, args, { encoding: 'utf8', windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
}

export async function waitForMusic(what, check, seconds = 180) {
  const end = Date.now() + seconds * 1000;
  for (;;) {
    const value = await check();
    if (value) return value;
    if (Date.now() >= end) throw new Error(`Timed out: ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
}

export function musicKit(kit) {
  const folder = path.resolve(process.env.NODEARR_MUSIC_KIT ?? path.join(kit, 'music'));
  const credentials = path.join(folder, 'credentials.json');
  const keys = existsSync(credentials) ? JSON.parse(readFileSync(credentials, 'utf8')) : {
    a: existsSync(path.join(folder, 'lidarr-key.txt')) ? readFileSync(path.join(folder, 'lidarr-key.txt'), 'utf8').trim() : randomBytes(16).toString('hex'),
    b: randomBytes(16).toString('hex'),
  };
  mkdirSync(folder, { recursive: true });
  writeFileSync(credentials, JSON.stringify(keys));
  const port = Number(process.env.NODEARR_MUSIC_PORT ?? 38686);
  return Object.fromEntries(['a', 'b'].map((side, index) => [side, {
    folder, port: port + index, side, container: `nodearr-ui-music-${side}`,
    async api(method, route, body) {
      const response = await fetch(`http://127.0.0.1:${port + index}/api/v1${route}`, {
        method, headers: { 'X-Api-Key': keys[side], 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      const text = await response.text();
      if (!response.ok) throw new Error(`Lidarr ${side}: ${method} ${route}: ${response.status}: ${text}`);
      return text ? JSON.parse(text) : null;
    },
    key: keys[side],
  }]));
}

async function command(node, name) {
  const queued = await node.api('POST', '/command', { name });
  await waitForMusic(`${name} on ${node.name}`, async () => {
    const current = await node.api('GET', `/command/${queued.id}`);
    assert.notEqual(current.status, 'failed', current.message);
    return current.status === 'completed';
  });
}

async function configure(app, node) {
  await waitForMusic(`Nodearr ${app.side} API`, async () => { try { return await node.api('GET', '/system/status'); } catch { return false; } });
  const config = path.join(app.folder, `lidarr-${app.side}`);
  const library = path.join(app.folder, `music-${app.side}`);
  mkdirSync(config, { recursive: true });
  mkdirSync(library, { recursive: true });
  const settings = await node.api('GET', '/settings/node');
  const downloads = settings.downloadFolder || settings.defaultDownloadFolder;
  mkdirSync(downloads, { recursive: true });
  const environment = path.join(app.folder, `${app.side}.env`);
  writeFileSync(environment, `LIDARR__AUTH__METHOD=External\nLIDARR__AUTH__APIKEY=${app.key}\nLIDARR__LOG__ANALYTICSENABLED=false\n`);
  let existing;
  try { existing = JSON.parse(run('docker', ['inspect', app.container]))[0]; } catch { /* New kit. */ }
  if (existing) {
    assert(existing.Mounts.some((mount) => mount.Destination === '/config' && mount.Source.toLowerCase().replaceAll('\\', '/').endsWith(config.toLowerCase().replaceAll('\\', '/').replace(/^[a-z]:/, ''))), 'A different music kit owns this container; stop it and choose other test ports/names');
    run('docker', ['start', app.container]);
  } else {
    run('docker', ['run', '-d', '--name', app.container, '--label', 'com.docker.compose.project=nodearr-ui-music',
      '--add-host', 'host.docker.internal:host-gateway', '-p', `127.0.0.1:${app.port}:8686`, '--env-file', environment,
      '-v', `${config}:/config`, '-v', `${library}:/music`, '-v', `${downloads}:/downloads`, image]);
  }
  await waitForMusic(`Lidarr ${app.side} API`, async () => { try { return await app.api('GET', '/system/status'); } catch { return false; } });
  run('docker', ['exec', app.container, 'chown', 'abc:abc', '/music']);
  const profile = await app.api('GET', '/qualityprofile/1');
  await app.api('PUT', '/qualityprofile/1', { ...profile, upgradeAllowed: true, cutoff: 1004 });
  // Real 320 kbit/s files with cover art in every song run above Lidarr's default size ceiling
  // for MP3-320, so this isolated fixture has no upper limit for it.
  const quality = (await app.api('GET', '/qualitydefinition')).find((definition) => definition.quality.name === 'MP3-320');
  await app.api('PUT', `/qualitydefinition/${quality.id}`, { ...quality, maxSize: null });
  if (!(await app.api('GET', '/rootfolder')).length) {
    await app.api('POST', '/rootfolder', { name: 'Music', path: '/music', defaultQualityProfileId: 1, defaultMetadataProfileId: 1, defaultMonitorOption: 'none', defaultNewItemMonitorOption: 'none' });
  }
  const mappings = await app.api('GET', '/remotepathmapping');
  const normalized = (value) => value.replace(/[\\/]+$/, '');
  const downloadMappings = mappings.filter((mapping) => mapping.host === 'host.docker.internal' && normalized(mapping.remotePath) === normalized(downloads));
  assert(downloadMappings.length <= 1, 'Duplicate download path mappings in this fixture');
  if (!downloadMappings.length) {
    await app.api('POST', '/remotepathmapping', { host: 'host.docker.internal', remotePath: downloads, localPath: '/downloads/' });
  } else {
    assert.equal(normalized(downloadMappings[0].localPath), '/downloads');
  }
  const localMappings = await node.api('GET', '/remotepathmapping');
  const musicMappings = localMappings.filter((mapping) => mapping.host === '127.0.0.1' && normalized(mapping.remotePath) === '/music');
  assert(musicMappings.length <= 1, 'Duplicate music path mappings in this fixture');
  if (!musicMappings.length) {
    await node.api('POST', '/remotepathmapping', { host: '127.0.0.1', remotePath: '/music/', localPath: library + path.sep });
  } else {
    assert.equal(normalized(musicMappings[0].localPath), normalized(library));
  }
  if (!(await node.api('GET', '/applications')).some((definition) => definition.implementation === 'Lidarr')) {
    const definition = (await node.api('GET', '/applications/schema')).find((definition) => definition.implementation === 'Lidarr');
    const values = { baseUrl: `http://127.0.0.1:${app.port}`, apiKey: app.key, nodearrUrl: `http://host.docker.internal:${node.port}`, fetchNewFilesAutomatically: false };
    for (const field of definition.fields) if (field.name in values) field.value = values[field.name];
    await node.api('POST', '/applications', { ...definition, name: 'Lidarr', syncLevel: 'fullSync' });
  }
}

async function artistAndAlbum(app, fixture) {
  let artist = (await app.api('GET', '/artist')).find((artist) => artist.foreignArtistId === fixture.artistId);
  if (!artist) {
    const found = (await app.api('GET', `/artist/lookup?term=lidarr:${fixture.artistId}`))[0];
    assert(found, `Lidarr cannot resolve ${fixture.artist}`);
    artist = await app.api('POST', '/artist', { ...found, qualityProfileId: 1, metadataProfileId: 1, rootFolderPath: '/music', monitored: true, monitorNewItems: 'none', addOptions: { monitor: 'none', searchForMissingAlbums: false } });
  }
  const album = await waitForMusic(`${fixture.slug} release metadata`, async () =>
    (await app.api('GET', `/album?artistId=${artist.id}`)).find((album) => album.foreignAlbumId === fixture.albumId && album.releases?.some((release) => release.foreignReleaseId === fixture.releaseId)));
  const release = album.releases.find((release) => release.foreignReleaseId === fixture.releaseId);
  for (const edition of album.releases) edition.monitored = edition.id === release.id;
  await app.api('PUT', `/album/${album.id}`, { ...album, anyReleaseOk: false, monitored: true });
  await app.api('PUT', `/artist/${artist.id}`, { ...artist, monitored: true });
  const tracks = await app.api('GET', `/track?albumId=${album.id}&albumReleaseId=${release.id}`);
  assert.equal(tracks.length, fixture.tracks, 'Pinned release track count changed');
  return { artist, album, release, tracks: tracks.sort((a, b) => a.absoluteTrackNumber - b.absoluteTrackNumber) };
}

function filesBelow(folder) {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => entry.isDirectory() ? filesBelow(path.join(folder, entry.name)) : [path.join(folder, entry.name)]);
}

async function seed(app, fixture, partial = false) {
  const metadata = await artistAndAlbum(app, fixture);
  const held = (await app.api('GET', `/trackfile?artistId=${metadata.artist.id}`)).filter((file) => file.albumId === metadata.album.id);
  if (held.length) {
    assert.equal(held.length, partial ? 1 : fixture.tracks, 'The music fixture has changed; prepare a fresh isolated kit rather than weakening its expected state');
    return;
  }
  const archive = path.join(app.folder, `${fixture.slug}.zip`);
  if (!existsSync(archive)) {
    const response = await fetch(fixture.download);
    assert(response.ok, `Download failed: ${fixture.download}`);
    writeFileSync(archive, Buffer.from(await response.arrayBuffer()));
  }
  assert.equal(createHash('sha256').update(readFileSync(archive)).digest('hex'), fixture.sha256, 'Fixture archive changed; review its contents and licence before updating the checksum');
  const raw = path.join(app.folder, `raw-${fixture.slug}`);
  mkdirSync(raw, { recursive: true });
  run(process.env.PYTHON ?? 'python', ['-c', 'import sys,zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])', archive, raw]);
  const sources = filesBelow(raw).filter((file) => /\.mp3$/i.test(file)).sort((a, b) => a.localeCompare(b));
  assert.equal(sources.length, fixture.tracks);
  const folderName = fixture.slug;
  const output = path.join(app.folder, `lidarr-${app.side}`, 'seed', folderName);
  mkdirSync(output, { recursive: true });
  const importFiles = [];
  const quality = (await app.api('GET', '/qualitydefinition')).find((definition) => definition.quality.name === (partial ? 'MP3-128' : 'MP3-320')).quality;
  for (const [index, track] of metadata.tracks.entries()) {
    if (partial && index > 0) break;
    const tags = { artist: fixture.artist, album_artist: fixture.artist, album: metadata.album.title, title: track.title, track: `${index + 1}/${fixture.tracks}`, disc: '1/1', date: String(metadata.album.releaseDate).slice(0, 4),
      MUSICBRAINZ_ARTISTID: fixture.artistId, MUSICBRAINZ_ALBUMARTISTID: fixture.artistId, MUSICBRAINZ_ALBUMID: fixture.releaseId, MUSICBRAINZ_RELEASEGROUPID: fixture.albumId, MUSICBRAINZ_RELEASETRACKID: track.foreignTrackId };
    const name = `${fixture.artist} - ${metadata.album.title.slice(0, 60)} - ${String(index + 1).padStart(2, '0')}.mp3`.replace(/[<>:"/\\|?*]/g, '');
    run('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', '-i', sources[index], '-map', '0:a:0', '-c:a', partial ? 'libmp3lame' : 'copy',
      ...(partial ? ['-b:a', '128k'] : []), '-id3v2_version', '3', ...Object.entries(tags).flatMap(([key, value]) => ['-metadata', `${key}=${value}`]), path.join(output, name)]);
    importFiles.push({ path: `/config/seed/${folderName}/${name}`, artistId: metadata.artist.id, albumId: metadata.album.id, albumReleaseId: metadata.release.id, trackIds: [track.id], quality: { quality, revision: { version: 1, real: 0 } } });
  }
  await app.api('POST', '/command', { name: 'ManualImport', importMode: 'copy', replaceExistingFiles: false, files: importFiles });
  console.log(`Waiting for ${fixture.slug} to import on ${app.side}`);
  await waitForMusic(`${fixture.slug} imported on ${app.side}`, async () => (await app.api('GET', `/trackfile?artistId=${metadata.artist.id}`)).filter((file) => file.albumId === metadata.album.id).length === (partial ? 1 : fixture.tracks), 300);
  console.log(`Music fixture ${app.side}: ${fixture.artist}, ${metadata.album.title}, ${partial ? 'one lower-quality song' : fixture.tracks + ' songs'}`);
}

export async function prepareMusic(kit, nodes) {
  const apps = musicKit(kit);
  for (const side of ['a', 'b']) await configure(apps[side], nodes[side]);
  for (const fixture of musicManifest.albums) await seed(apps.a, fixture);
  await seed(apps.b, musicManifest.albums[0], true);
  for (const side of ['a', 'b']) await command(nodes[side], 'CatalogRefresh');
  for (const side of ['a', 'b']) await command(nodes[side], 'PeerCatalogSync');
  console.log('Music kit ready: two artists, two CC0 albums, a partial local album and an offered quality upgrade');
}
