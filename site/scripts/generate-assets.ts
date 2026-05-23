/**
 * Generate all static brand assets for hotrepl.glockyco.com:
 *   static/og-image.png              1200×630 Open Graph card
 *   static/favicon.ico               32×32 ICO (legacy fallback)
 *   static/favicon-32x32.png         32×32 PNG
 *   static/apple-touch-icon.png      180×180 PNG (20 px padding)
 *   static/icons/pwa-192.png         192×192 PNG
 *   static/icons/pwa-512.png         512×512 PNG
 *   static/icons/pwa-maskable-512.png 512×512 PNG (bolt in safe zone)
 *   static/sitemap.xml               2-URL sitemap
 *
 * Colors are pre-computed hex equivalents of the site's CSS custom properties:
 *   --bg      oklch(0.11 0.01 240) → #020507
 *   --accent  oklch(0.75 0.18 45)  → #ff823f (clamped from P3)
 *   --text    oklch(0.93 0.01 240) → #e2e9ee
 *   --muted   oklch(0.58 0.02 240) → #707c85
 */

import { mkdirSync, writeFileSync } from "fs";
import { dirname, resolve } from "path";
import sharp from "sharp";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const STATIC = resolve(__dirname, "../static");
const ICONS = resolve(STATIC, "icons");

mkdirSync(STATIC, { recursive: true });
mkdirSync(ICONS, { recursive: true });

// ── Brand palette (sRGB hex for rasterizer compat) ────────────────────────────
const BG = "#020507";
const ACCENT = "#ff823f";
const TEXT = "#e2e9ee";
const MUTED = "#9aa8b2";

// The lightning bolt path from favicon.svg (viewBox 0 0 64 64)
const BOLT_PATH = "M18 38 30 16h16L36 30h10L28 52l6-14H18Z";

// ── Minimal ICO encoder (PNG-in-ICO, no extra deps) ───────────────────────────
function pngToIco(pngBuffer: Buffer, size: number): Buffer {
  const IMAGE_OFFSET = 6 + 16; // header(6) + one dir entry(16)
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); // reserved
  header.writeUInt16LE(1, 2); // type: icon
  header.writeUInt16LE(1, 4); // image count

  const dir = Buffer.alloc(16);
  dir.writeUInt8(size >= 256 ? 0 : size, 0); // width (0 = 256)
  dir.writeUInt8(size >= 256 ? 0 : size, 1); // height
  dir.writeUInt8(0, 2); // color count (0 = truecolor)
  dir.writeUInt8(0, 3); // reserved
  dir.writeUInt16LE(1, 4); // color planes
  dir.writeUInt16LE(32, 6); // bits per pixel
  dir.writeUInt32LE(pngBuffer.length, 8);
  dir.writeUInt32LE(IMAGE_OFFSET, 12);

  return Buffer.concat([header, dir, pngBuffer]);
}

// ── SVG helpers ───────────────────────────────────────────────────────────────
function escapeXml(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

// Render an SVG string to a PNG Buffer via sharp
async function svgToPng(svgStr: string): Promise<Buffer> {
  return sharp(Buffer.from(svgStr)).png({ compressionLevel: 9 }).toBuffer();
}

// ── Icon SVG builder (bolt on dark square) ────────────────────────────────────
// padding: fraction of size to leave as margin around the bolt (0 = fill, 0.2 = safe zone)
function iconSvg(size: number, padding = 0): string {
  // The bolt occupies roughly the rect [18,16]→[46,52] in a 64×64 space.
  // We scale it to fit inside the padded area while preserving aspect ratio.
  const boltW = 46 - 18; // 28
  const boltH = 52 - 16; // 36
  const available = size * (1 - 2 * padding);
  const scale = available / Math.max(boltW, boltH); // fit longer side
  const scaledW = boltW * scale;
  const scaledH = boltH * scale;
  const tx = (size - scaledW) / 2 - 18 * scale;
  const ty = (size - scaledH) / 2 - 16 * scale;

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
  <rect width="${size}" height="${size}" rx="${size * 0.1875}" fill="${BG}"/>
  <g transform="translate(${tx.toFixed(2)}, ${ty.toFixed(2)}) scale(${scale.toFixed(4)})">
    <path d="${BOLT_PATH}" fill="${ACCENT}"/>
  </g>
</svg>`;
}

// ── OG image (1200×630) ───────────────────────────────────────────────────────
const OG_W = 1200;
const OG_H = 630;
const TITLE = "HotRepl";
const TAGLINE = "Runtime C# REPL and typed command bridge for Unity games";
const DOMAIN = "hotrepl.glockyco.com";

// Keep the bolt out of the text's readable zone. It should register as brand
// texture, not compete with the tagline in compressed social previews.
const BOLT_SCALE = 8.8;
const BOLT_TX = 850 - 18 * BOLT_SCALE;
const BOLT_TY = 52 - 16 * BOLT_SCALE;

const ogSvg = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="${OG_W}" height="${OG_H}" viewBox="0 0 ${OG_W} ${OG_H}">
  <defs>
    <!-- Subtle warm glow top-right, pulls the eye toward the bolt -->
    <radialGradient id="glow" cx="85%" cy="15%" r="50%">
      <stop offset="0%" stop-color="${ACCENT}" stop-opacity="0.13"/>
      <stop offset="100%" stop-color="${BG}" stop-opacity="0"/>
    </radialGradient>
  </defs>

  <!-- Background -->
  <rect width="${OG_W}" height="${OG_H}" fill="${BG}"/>
  <rect width="${OG_W}" height="${OG_H}" fill="url(#glow)"/>

  <!-- Thin accent bar; enough for brand recognition without reading as a crop artifact -->
  <rect width="${OG_W}" height="4" fill="${ACCENT}" opacity="0.95"/>

  <!-- Lightning bolt watermark (right side, low opacity) -->
  <g transform="translate(${BOLT_TX.toFixed(1)}, ${
  BOLT_TY.toFixed(1)
}) scale(${BOLT_SCALE})" opacity="0.12" fill="${ACCENT}">
    <path d="${BOLT_PATH}"/>
  </g>

  <!-- "HotRepl" wordmark -->
  <text x="80" y="300"
        font-family="'Helvetica Neue', Helvetica, Arial, sans-serif"
        font-size="92" font-weight="800" fill="${TEXT}" letter-spacing="-2">${
  escapeXml(TITLE)
}</text>

  <!-- Tagline: intentionally bright enough to survive small social preview cards -->
  <text x="80" y="382"
        font-family="'Helvetica Neue', Helvetica, Arial, sans-serif"
        font-size="34" font-weight="400" fill="${MUTED}">${escapeXml(TAGLINE)}</text>

  <!-- Domain kept comfortably inside preview crop safe areas -->
  <text x="80" y="552"
        font-family="'Helvetica Neue', Helvetica, Arial, sans-serif"
        font-size="26" font-weight="600" fill="${ACCENT}" letter-spacing="1">${
  escapeXml(DOMAIN)
}</text>
</svg>`;

// ── Generate all assets ───────────────────────────────────────────────────────
async function main() {
  // OG image
  const ogPng = await svgToPng(ogSvg);
  writeFileSync(resolve(STATIC, "og-image.png"), ogPng);
  console.log(`  wrote og-image.png (${ogPng.length} bytes, ${OG_W}×${OG_H})`);

  // Icon variants
  const sizes: Array<{ name: string; size: number; padding: number; dir?: string }> = [
    { name: "favicon-32x32.png", size: 32, padding: 0 },
    { name: "apple-touch-icon.png", size: 180, padding: 0.11 }, // ~20px padding
    { name: "pwa-192.png", size: 192, padding: 0, dir: "icons" },
    { name: "pwa-512.png", size: 512, padding: 0, dir: "icons" },
    { name: "pwa-maskable-512.png", size: 512, padding: 0.1, dir: "icons" }, // safe zone
  ];

  for (const { name, size, padding, dir } of sizes) {
    const svg = iconSvg(size, padding);
    const png = await svgToPng(svg);
    const dest = dir ? resolve(ICONS, name) : resolve(STATIC, name);
    writeFileSync(dest, png);
    console.log(`  wrote ${dir ? `icons/${name}` : name} (${png.length} bytes, ${size}×${size})`);
  }

  // favicon.ico — 32×32 PNG wrapped in ICO
  const ico32 = await svgToPng(iconSvg(32, 0));
  const ico = pngToIco(ico32, 32);
  writeFileSync(resolve(STATIC, "favicon.ico"), ico);
  console.log(`  wrote favicon.ico (${ico.length} bytes)`);

  // sitemap.xml
  const sitemap = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>https://hotrepl.glockyco.com/</loc>
    <changefreq>monthly</changefreq>
    <priority>1.0</priority>
  </url>
  <url>
    <loc>https://hotrepl.glockyco.com/protocol/</loc>
    <changefreq>monthly</changefreq>
    <priority>0.9</priority>
  </url>
</urlset>
`;
  writeFileSync(resolve(STATIC, "sitemap.xml"), sitemap);
  console.log("  wrote sitemap.xml");

  console.log("\nAll assets generated.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
