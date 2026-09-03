// Salvages the three truncated atlases from the equipment drop.
//
// Each file stops mid-IDAT with no IEND, so no image loader will touch it. The pixel data
// that IS present is still valid deflate output, so this inflates as far as the stream
// allows, defilters the scanlines that completed, fills whatever is missing by repeating
// the last good row, and writes a fresh well-formed PNG.
//
// Repeating the last row rather than filling with a flat colour matters: these are texture
// atlases, so a missing tail region reads far better as a smear of the neighbouring
// material than as a magenta or black band across the bottom cells.

import fs from "fs";
import zlib from "zlib";
import path from "path";
import { fileURLToPath } from "url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const TEX = path.resolve(HERE, "../../Assets/_Game/Art/Equipment/Textures");
const FILES = [
  "HD_CompactCarbine_Atlas_Albedo.png",
  "HD_TraumaKit_Atlas_Albedo.png",
  "HD_LightHunterVest_Atlas_Albedo.png",
];

const CHANNELS = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 };

function crc32(buf) {
  let c, table = [];
  for (let n = 0; n < 256; n++) {
    c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  let crc = 0xffffffff;
  for (const b of buf) crc = table[(crc ^ b) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}

function paeth(a, b, c) {
  const p = a + b - c;
  const pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}

for (const name of FILES) {
  const file = path.join(TEX, name);
  const buf = fs.readFileSync(file);

  // ---- parse chunks -------------------------------------------------------
  let off = 8;
  let ihdr = null;
  const idat = [];
  const ancillary = [];

  while (off + 8 <= buf.length) {
    const len = buf.readUInt32BE(off);
    const type = buf.toString("ascii", off + 4, off + 8);
    const dataStart = off + 8;
    const dataEnd = dataStart + len;
    if (dataEnd + 4 > buf.length) break;          // truncated chunk: stop
    const data = buf.subarray(dataStart, dataEnd);

    if (type === "IHDR") ihdr = data;
    else if (type === "IDAT") idat.push(data);
    else if (type === "PLTE" || type === "tRNS") ancillary.push([type, data]);
    else if (type === "IEND") break;

    off = dataEnd + 4;
  }

  const width = ihdr.readUInt32BE(0);
  const height = ihdr.readUInt32BE(4);
  const depth = ihdr[8];
  const colorType = ihdr[9];
  const interlace = ihdr[12];

  if (depth !== 8 || interlace !== 0) {
    console.log(`${name}: unsupported (depth=${depth} interlace=${interlace}) -- skipped`);
    continue;
  }

  const channels = CHANNELS[colorType];
  const bpp = channels;
  const stride = width * bpp;

  // ---- inflate as far as the stream goes ----------------------------------
  let raw;
  try {
    raw = zlib.inflateSync(Buffer.concat(idat), { finishFlush: zlib.constants.Z_SYNC_FLUSH });
  } catch (e) {
    console.log(`${name}: inflate failed (${e.message}) -- skipped`);
    continue;
  }

  const completeRows = Math.floor(raw.length / (stride + 1));
  const usable = Math.min(completeRows, height);

  // ---- defilter -----------------------------------------------------------
  const out = Buffer.alloc(height * stride);
  for (let y = 0; y < usable; y++) {
    const filter = raw[y * (stride + 1)];
    const src = raw.subarray(y * (stride + 1) + 1, y * (stride + 1) + 1 + stride);
    const cur = out.subarray(y * stride, (y + 1) * stride);
    const prev = y > 0 ? out.subarray((y - 1) * stride, y * stride) : null;

    for (let i = 0; i < stride; i++) {
      const a = i >= bpp ? cur[i - bpp] : 0;
      const b = prev ? prev[i] : 0;
      const c = prev && i >= bpp ? prev[i - bpp] : 0;
      let v = src[i];
      if (filter === 1) v += a;
      else if (filter === 2) v += b;
      else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) v += paeth(a, b, c);
      cur[i] = v & 0xff;
    }
  }

  // ---- fill the missing tail ----------------------------------------------
  if (usable === 0) {
    console.log(`${name}: no complete scanlines -- skipped`);
    continue;
  }
  const last = out.subarray((usable - 1) * stride, usable * stride);
  for (let y = usable; y < height; y++) {
    last.copy(out, y * stride);
  }

  // ---- re-encode ----------------------------------------------------------
  const filtered = Buffer.alloc(height * (stride + 1));
  for (let y = 0; y < height; y++) {
    filtered[y * (stride + 1)] = 0;               // filter: none
    out.copy(filtered, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  }

  const png = Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    ...ancillary.map(([t, d]) => chunk(t, d)),
    chunk("IDAT", zlib.deflateSync(filtered, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);

  fs.copyFileSync(file, file + ".truncated.bak");
  fs.writeFileSync(file, png);

  const pct = ((usable / height) * 100).toFixed(1);
  console.log(`${name}: ${width}x${height} ct=${colorType} -- recovered ${usable}/${height} rows (${pct}%), rewrote ${png.length} bytes`);
}
