#!/usr/bin/env node
// Windows-only, no PowerShell: uses native clipboard addon
import { Server } from 'socket.io';
import { io as ioclient } from 'socket.io-client';
import minimist from 'minimist';
import { v4 as uuidv4 } from 'uuid';
import os from 'os';
import crypto from 'crypto';
import AdmZip from 'adm-zip';
import fs from 'fs';
import path from 'path';
import clipboard from 'win-clipboard'; // native Windows clipboard addon

const argv = minimist(process.argv.slice(2), {
  string: ['port','peers','key','maxmb'],
  alias: { p:'port', P:'peers', k:'key' },
  default: { port: '3030', peers: '', maxmb: '100' } // MB cap for transfers
});

const PORT   = Number(argv.port);
const KEY    = (argv.key || '').trim();
const PEERS  = (argv.peers || '').split(',').map(s=>s.trim()).filter(Boolean);
const MAX_MB = Number(argv.maxmb);
const INSTANCE_ID = `${os.hostname()}-${uuidv4().slice(0,8)}`;

const log = (...a) => console.log(`[${new Date().toLocaleTimeString()}]`, ...a);
const sha256 = (bufOrStr) => crypto.createHash('sha256').update(bufOrStr).digest('hex');

const seen = new Set();
const SEEN_MAX = 500;
function markSeen(h){ seen.add(h); if (seen.size>SEEN_MAX){ const last=[...seen].slice(-250); seen.clear(); last.forEach(x=>seen.add(x)); } }
const isSeen = (h)=> seen.has(h);

// ---------------- Clipboard helpers (native) ----------------
/**
 * Detect what's currently on the clipboard, in order:
 * 1) Files (FileDropList)
 * 2) Image (PNG/DIB)
 * 3) Text (Unicode)
 * Returns { kind: 'files'|'image'|'text', data: Buffer, meta: object, hash: string }
 */
function readClipboard() {
  // 1) Files
  try {
    const files = clipboard.readFiles?.() || [];
    if (Array.isArray(files) && files.length) {
      const zip = new AdmZip();
      for (const p of files) {
        try {
          const st = fs.statSync(p);
          if (st.isDirectory()) zip.addLocalFolder(p, path.basename(p));
          else zip.addLocalFile(p);
        } catch {}
      }
      const zipBuf = zip.toBuffer();
      return { kind:'files', data: zipBuf, meta:{ count: files.length }, hash: sha256(zipBuf) };
    }
  } catch (e) {}

  // 2) Image
  try {
    const imgBuf = clipboard.readImage?.(); // Buffer or null
    if (imgBuf && imgBuf.length) {
      return { kind:'image', data: imgBuf, meta:{ format: 'png-or-dib' }, hash: sha256(imgBuf) };
    }
  } catch (e) {}

  // 3) Text
  try {
    const txt = clipboard.readText?.();
    if (typeof txt === 'string') {
      const buf = Buffer.from(txt, 'utf8');
      return { kind:'text', data: buf, meta:{}, hash: sha256(buf) };
    }
  } catch (e) {}

  return null;
}

/**
 * Apply a received payload to local clipboard using native addon.
 */
function writeClipboard({ kind, data, meta }) {
  if (!data) return;

  const mb = data.length / (1024*1024);
  if (mb > MAX_MB) { log(`Refusing to set clipboard > ${MAX_MB}MB`); return; }

  if (kind === 'text') {
    clipboard.writeText?.(data.toString('utf8'));
    return;
  }

  if (kind === 'image') {
    clipboard.writeImage?.(data);
    return;
  }

  if (kind === 'files') {
    // We receive a zip buffer containing files/folders.
    // Extract to a temp folder and set the extracted file paths on clipboard.
    const osTmp = fs.mkdtempSync(path.join(os.tmpdir(), 'clip-sync-'));
    const zipPath = path.join(osTmp, 'recv.zip');
    fs.writeFileSync(zipPath, data);
    const zip = new AdmZip(zipPath);
    zip.extractAllTo(osTmp, true);

    // recursively collect files
    const collected = [];
    (function walk(p){
      const entries = fs.readdirSync(p, { withFileTypes: true });
      for (const e of entries) {
        const full = path.join(p, e.name);
        if (e.isDirectory()) walk(full); else collected.push(full);
      }
    })(osTmp);

    if (collected.length) {
      clipboard.writeFiles?.(collected);
    }
    return;
  }
}

// ---------------- Socket plumbing ----------------
const io = new Server(PORT, { serveClient:false, cors:{ origin:'*' } });
io.use((socket,next)=>{
  if (!KEY) return next();
  const provided = socket.handshake.auth?.key || socket.handshake.query?.key;
  if (provided === KEY) return next();
  next(new Error('unauthorized'));
});

io.on('connection', (socket)=>{
  log(`peer connected: ${socket.handshake.address ?? 'unknown'}`);

  socket.on('clip:payload', (msg)=>{
    if (!msg || !msg.kind || !msg.hash || !msg.data) return;
    if (isSeen(msg.hash)) return;
    markSeen(msg.hash);
    writeClipboard({ kind: msg.kind, data: Buffer.from(msg.data, 'base64'), meta: msg.meta||{} });
    fanout(msg, socket);
    log(`applied ${msg.kind} from peer (${(msg.data.length/1024).toFixed(1)} KiB)`);
  });
});

const peerClients = new Map();
function fanout(msg, except) {
  for (const [,s] of io.sockets.sockets) if (s !== except) s.emit('clip:payload', msg);
  for (const [,c] of peerClients) c.emit('clip:payload', msg);
}

function connectPeer(addr){
  const url = `http://${addr}`;
  log(`connecting -> ${url}`);
  const sock = ioclient(url, {
    auth: KEY ? { key: KEY } : {},
    reconnection: true,
    reconnectionAttempts: Infinity,
    reconnectionDelay: 1000
  });

  sock.on('connect', ()=>log(`connected -> ${addr}`));
  sock.on('disconnect', r=>log(`disconnected <- ${addr}: ${r}`));
  sock.on('connect_error', e=>log(`connect_error <- ${addr}: ${e.message}`));

  sock.on('clip:payload', (msg)=>{
    if (!msg || !msg.kind || !msg.hash || !msg.data) return;
    if (isSeen(msg.hash)) return;
    markSeen(msg.hash);
    writeClipboard({ kind: msg.kind, data: Buffer.from(msg.data, 'base64'), meta: msg.meta||{} });
    fanout(msg);
    log(`applied ${msg.kind} from ${addr}`);
  });

  peerClients.set(addr, sock);
}

// Poll for local clipboard changes and broadcast
let lastHash = '';
const POLL_MS = 900;

function poll() {
  try {
    const clip = readClipboard();
    if (clip) {
      const { kind, data, meta, hash } = clip;
      if (hash !== lastHash && !isSeen(hash)) {
        lastHash = hash; markSeen(hash);
        const mb = data.length/(1024*1024);
        if (mb <= MAX_MB) {
          const msg = { kind, meta, hash, data: data.toString('base64'), ts: Date.now(), source: INSTANCE_ID };
          fanout(msg);
          log(`broadcast ${kind} (${mb.toFixed(2)} MB)`);
        } else {
          log(`skip broadcast > ${MAX_MB}MB (${mb.toFixed(2)} MB)`);
        }
      }
    }
  } catch (e) {
    log('poll error:', e.message);
  } finally {
    setTimeout(poll, POLL_MS);
  }
}

// Start
log(`Clipboard Sync (Windows/native) on :${PORT}`);
log(`Instance: ${INSTANCE_ID}`);
if (KEY) log('Auth key enabled');
if (PEERS.length) {
  log('Peers:', PEERS.join(', '));
  PEERS.forEach(connectPeer);
} else {
  log('No peers configured (use --peers ip:port,ip:port)');
}
poll();

process.on('SIGINT', ()=>{
  log('Shutting down...');
  for (const [,c] of peerClients) try { c.close(); } catch {}
  io.close(()=>process.exit(0));
});
