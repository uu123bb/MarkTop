// Generates Assets/marktop.ico (256x256 PNG-in-ICO). Dev utility, not part of the build.
import { deflateSync } from "node:zlib";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const size = 256;
const pixels = Buffer.alloc(size * size * 4, 0);
const accent = [0x37, 0x94, 0xff];
const white = [0xf5, 0xf7, 0xfa];

function set(x, y, [r, g, b], a = 255) {
  if (x < 0 || y < 0 || x >= size || y >= size) return;
  const offset = (y * size + x) * 4;
  pixels[offset] = r;
  pixels[offset + 1] = g;
  pixels[offset + 2] = b;
  pixels[offset + 3] = a;
}

function roundedRect(radius, bg) {
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const dx = Math.max(radius - x, x - (size - 1 - radius), 0);
      const dy = Math.max(radius - y, y - (size - 1 - radius), 0);
      const distance = Math.hypot(dx, dy);
      const alpha = distance <= radius ? 255 : distance <= radius + 1.5 ? Math.round(255 * (radius + 1.5 - distance) / 1.5) : 0;
      if (alpha > 0) set(x, y, bg, alpha);
    }
  }
}

function thickLine(x0, y0, x1, y1, width, color) {
  const steps = 800;
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const cx = x0 + (x1 - x0) * t;
    const cy = y0 + (y1 - y0) * t;
    for (let ox = -width; ox <= width; ox++) {
      for (let oy = -width; oy <= width; oy++) {
        if (ox * ox + oy * oy <= width * width) {
          set(Math.round(cx + ox), Math.round(cy + oy), color);
        }
      }
    }
  }
}

roundedRect(52, accent);

// Stylised "M": two verticals and a V, echoing a markdown heading mark.
thickLine(78, 176, 78, 92, 11, white);
thickLine(78, 92, 128, 148, 11, white);
thickLine(128, 148, 178, 92, 11, white);
thickLine(178, 92, 178, 176, 11, white);

function crc32(buffer) {
  let c;
  const table = [];
  for (let n = 0; n < 256; n++) {
    c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  let crc = 0xffffffff;
  for (const byte of buffer) crc = table[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const typeBuffer = Buffer.from(type, "ascii");
  const crcBuffer = Buffer.alloc(4);
  crcBuffer.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])));
  return Buffer.concat([length, typeBuffer, data, crcBuffer]);
}

const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(size, 0);
ihdr.writeUInt32BE(size, 4);
ihdr[8] = 8;
ihdr[9] = 6;

const raw = Buffer.alloc((size * 4 + 1) * size);
for (let y = 0; y < size; y++) {
  raw[y * (size * 4 + 1)] = 0;
  pixels.copy(raw, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4);
}

const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk("IHDR", ihdr),
  chunk("IDAT", deflateSync(raw, { level: 9 })),
  chunk("IEND", Buffer.alloc(0)),
]);

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(1, 4);

const entry = Buffer.alloc(16);
entry[0] = 0; // 256px
entry[1] = 0;
entry[2] = 0;
entry[3] = 0;
entry.writeUInt16LE(1, 4);
entry.writeUInt16LE(32, 6);
entry.writeUInt32LE(png.length, 8);
entry.writeUInt32LE(header.length + entry.length, 12);

const ico = Buffer.concat([header, entry, png]);
const target = join(dirname(fileURLToPath(import.meta.url)), "..", "src", "MarkTop.App", "Assets", "marktop.ico");
mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, ico);
console.log(`[icon] ${target} (${ico.length} bytes)`);
