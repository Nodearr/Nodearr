// Renders the PNG and ICO files from the mark's SVGs, and the YouTube and Twitch banners' PNGs from
// theirs (build/logo/make-logo.py writes those).
//
//   node build/logo/render-icons.mjs
//
// It needs sharp, which is not one of Nodearr's dependencies: run it where sharp is installed,
// for example with NODE_PATH pointing at a folder where `npm install sharp` was run.

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const sharp = require('sharp');

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const light = readFileSync(path.join(root, 'Logo', 'Nodearr.svg'));
const dark = readFileSync(path.join(root, 'Logo', 'Nodearr-dark.svg'));
const icons = path.join(root, 'frontend', 'src', 'Content', 'Images', 'Icons');

async function png(svg, size) {
  return sharp(svg, { density: Math.max(72, (72 * size) / 512 * 4) })
    .resize(size, size)
    .png({ compressionLevel: 9 })
    .toBuffer();
}

// A square icon centred on a wider tile, as Windows' wide tile wants.
async function tile(svg, width, height, size) {
  const mark = await png(svg, size);
  return sharp({ create: { width, height, channels: 4, background: { r: 0, g: 0, b: 0, alpha: 0 } } })
    .composite([{ input: mark, left: Math.round((width - size) / 2), top: Math.round((height - size) / 2) }])
    .png({ compressionLevel: 9 })
    .toBuffer();
}

// An ICO holding each size as a PNG, which every Windows since Vista reads.
async function ico(svg, sizes) {
  const images = await Promise.all(sizes.map((s) => png(svg, s)));
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);

  let offset = header.length;
  images.forEach((image, i) => {
    const size = sizes[i];
    const entry = 6 + 16 * i;
    header.writeUInt8(size >= 256 ? 0 : size, entry);
    header.writeUInt8(size >= 256 ? 0 : size, entry + 1);
    header.writeUInt8(0, entry + 2);
    header.writeUInt8(0, entry + 3);
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(image.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += image.length;
  });

  return Buffer.concat([header, ...images]);
}

function write(file, data) {
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, data);
  console.log('wrote', path.relative(root, file));
}

for (const size of [16, 32, 48, 64, 72, 96, 128, 256, 400, 512, 800, 1024]) {
  write(path.join(root, 'Logo', `${size}.png`), await png(light, size));
}

write(path.join(icons, 'favicon-16x16.png'), await png(light, 16));
write(path.join(icons, 'favicon-32x32.png'), await png(light, 32));
write(path.join(icons, 'favicon.ico'), await ico(light, [16, 32, 48]));

// A debug build shows the dark version, so it is told apart from a release at a glance, as
// Sonarr's debug builds show a different icon.
write(path.join(icons, 'favicon-debug-16x16.png'), await png(dark, 16));
write(path.join(icons, 'favicon-debug-32x32.png'), await png(dark, 32));
write(path.join(icons, 'favicon-debug.ico'), await ico(dark, [16, 32, 48]));

write(path.join(icons, 'apple-touch-icon.png'), await png(light, 180));
write(path.join(icons, 'android-chrome-192x192.png'), await png(light, 192));
write(path.join(icons, 'android-chrome-512x512.png'), await png(light, 512));
// Windows' tiles, at the 1.8 times scale their names leave out, with the margin the tiles want.
write(path.join(icons, 'mstile-70x70.png'), await png(light, 128));
write(path.join(icons, 'mstile-144x144.png'), await png(light, 144));
write(path.join(icons, 'mstile-150x150.png'), await tile(light, 270, 270, 180));
write(path.join(icons, 'mstile-310x310.png'), await tile(light, 558, 558, 360));
write(path.join(icons, 'mstile-310x150.png'), await tile(light, 558, 270, 180));

// The program's icon: the exe, the tray and the installer.
write(path.join(root, 'src', 'NzbDrone.Host', 'Nodearr.ico'), await ico(light, [16, 20, 24, 32, 40, 48, 64, 128, 256]));

// The channels' banners, each at the size its SVG is drawn. YouTube wants at least 2048 x 1152,
// and no more than 6 MB; Twitch recommends 1200 x 480.
async function banner(name, width, height) {
  const svg = readFileSync(path.join(root, 'Logo', `${name}.svg`));
  write(path.join(root, 'Logo', `${name}.png`), await sharp(svg, { density: 72 }).resize(width, height).png({ compressionLevel: 9 }).toBuffer());
}

await banner('youtube-banner', 2560, 1440);
await banner('twitch-banner', 1200, 480);
