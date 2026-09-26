#!/usr/bin/env node
/**
 * Generates an article's cover image (Article.coverImageUrl) and uploads it
 * to R2 as covers/<slug>.jpg.
 *
 * Two sources, chosen by the caller:
 *   --image <url>  the article's own inline image, scaled down to fit a
 *                  1200x630 canvas (--fit contain, the default, letterboxes
 *                  it; --fit cover crops to fill). Works for SVG diagrams
 *                  too, which Cloudflare's /cdn-cgi/image transform passes
 *                  through without rasterizing.
 *   --icon <name>  fallback for articles without an image: a Bootstrap Icons
 *                  glyph (https://icons.getbootstrap.com, MIT), centered on
 *                  the site's blue.
 *
 * Rendered once in headless Chromium and stored as a plain JPEG, so serving
 * it afterwards is a static file read (og:image crawlers don't reliably
 * render SVG, and per-request transforms can get evicted from cache).
 *
 * Usage:
 *   node make-cover.mjs --slug <slug> (--image <url> | --icon <name>)
 *                       [--fit contain|cover] [--background <css color>]
 *                       [--out <file>] [--no-upload]
 *
 * Needs Playwright (global install is fine) and, unless --no-upload,
 * R2_ACCOUNT_ID / BUCKET_ID / R2_ACCESS_KEY_ID / R2_SECRET_ACCESS_KEY /
 * R2_PUBLIC_BASE_URL in the environment. Prints the public URL to stdout.
 */
import { createHash, createHmac } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { parseArgs } from 'node:util';

const WIDTH = 1200;
const HEIGHT = 630;
const SITE_BLUE = '#1e3a8a';
const SITE_BLUE_LIGHT = '#dbeafe';

const { values: args } = parseArgs({
  options: {
    slug: { type: 'string' },
    image: { type: 'string' },
    icon: { type: 'string' },
    fit: { type: 'string', default: 'contain' },
    background: { type: 'string' },
    out: { type: 'string' },
    'no-upload': { type: 'boolean', default: false },
  },
});

if (!args.slug || !/^[a-z0-9-]+$/.test(args.slug) || !!args.image === !!args.icon) {
  console.error('Usage: make-cover.mjs --slug <slug> (--image <url> | --icon <name>) [--fit contain|cover] [--background <color>] [--out <file>] [--no-upload]');
  process.exit(2);
}
if (!['contain', 'cover'].includes(args.fit)) {
  console.error('--fit must be contain or cover');
  process.exit(2);
}

function loadPlaywright() {
  const require = createRequire(import.meta.url);
  try {
    return require('playwright');
  } catch {
    const globalRoot = execSync('npm root -g').toString().trim();
    return require(`${globalRoot}/playwright`);
  }
}

async function fetchOk(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`GET ${url} -> ${res.status}`);
  return res;
}

async function buildHtml() {
  if (args.image) {
    const res = await fetchOk(args.image);
    const type = res.headers.get('content-type')?.split(';')[0] || 'image/png';
    const dataUri = `data:${type};base64,${Buffer.from(await res.arrayBuffer()).toString('base64')}`;
    const background = args.background ?? '#ffffff';
    // Padding only when letterboxing, so a diagram doesn't touch the edges.
    const padding = args.fit === 'contain' ? 32 : 0;
    return `<body style="margin:0;width:${WIDTH}px;height:${HEIGHT}px;background:${background};box-sizing:border-box;padding:${padding}px">
      <img src="${dataUri}" style="display:block;width:100%;height:100%;object-fit:${args.fit}">
    </body>`;
  }

  if (!/^[a-z0-9-]+$/.test(args.icon)) throw new Error(`Invalid icon name: ${args.icon}`);
  const svg = await (await fetchOk(`https://cdn.jsdelivr.net/npm/bootstrap-icons@1/icons/${args.icon}.svg`)).text();
  const background = args.background ?? SITE_BLUE;
  return `<body style="margin:0;width:${WIDTH}px;height:${HEIGHT}px;background:${background};display:flex;align-items:center;justify-content:center;color:${SITE_BLUE_LIGHT}">
    <style>svg{width:280px;height:280px}</style>${svg}
  </body>`;
}

async function render(html) {
  const { chromium } = loadPlaywright();
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ viewport: { width: WIDTH, height: HEIGHT } });
    await page.setContent(html, { waitUntil: 'load' });
    return await page.screenshot({ type: 'jpeg', quality: 85 });
  } finally {
    await browser.close();
  }
}

// Minimal AWS SigV4 PutObject against R2's S3-compatible endpoint -- avoids
// pulling in the AWS SDK for a single call.
async function uploadToR2(key, body) {
  const env = (name) => {
    const value = process.env[name];
    if (!value) throw new Error(`${name} is not set`);
    return value;
  };
  const host = `${env('R2_ACCOUNT_ID')}.r2.cloudflarestorage.com`;
  const path = `/${env('BUCKET_ID')}/${key}`;
  const amzDate = new Date().toISOString().replace(/[:-]|\.\d{3}/g, '');
  const date = amzDate.slice(0, 8);
  const region = 'auto';
  const sha256 = (data) => createHash('sha256').update(data).digest('hex');
  const hmac = (k, data) => createHmac('sha256', k).update(data).digest();
  const payloadHash = sha256(body);
  const headers = {
    'cache-control': 'public, max-age=31536000',
    'content-type': 'image/jpeg',
    host,
    'x-amz-content-sha256': payloadHash,
    'x-amz-date': amzDate,
  };
  const signedHeaders = Object.keys(headers).join(';');
  const canonicalRequest = [
    'PUT', path, '',
    ...Object.entries(headers).map(([k, v]) => `${k}:${v}`), '',
    signedHeaders, payloadHash,
  ].join('\n');
  const scope = `${date}/${region}/s3/aws4_request`;
  const stringToSign = ['AWS4-HMAC-SHA256', amzDate, scope, sha256(canonicalRequest)].join('\n');
  let signingKey = hmac(`AWS4${env('R2_SECRET_ACCESS_KEY')}`, date);
  for (const part of [region, 's3', 'aws4_request']) signingKey = hmac(signingKey, part);
  const signature = createHmac('sha256', signingKey).update(stringToSign).digest('hex');

  const { host: _host, ...sendHeaders } = headers;
  const res = await fetch(`https://${host}${path}`, {
    method: 'PUT',
    headers: {
      ...sendHeaders,
      authorization: `AWS4-HMAC-SHA256 Credential=${env('R2_ACCESS_KEY_ID')}/${scope}, SignedHeaders=${signedHeaders}, Signature=${signature}`,
    },
    body,
  });
  if (!res.ok) throw new Error(`R2 PUT ${key} -> ${res.status}: ${await res.text()}`);
  return `${env('R2_PUBLIC_BASE_URL').replace(/\/$/, '')}/${key}`;
}

const jpeg = await render(await buildHtml());
if (args.out) await writeFile(args.out, jpeg);
if (!args['no-upload']) {
  console.log(await uploadToR2(`covers/${args.slug}.jpg`, jpeg));
} else if (!args.out) {
  console.error('--no-upload without --out does nothing');
  process.exit(2);
}
