// Robot Fatal Shooter - servidor de lobby, salas e relay.
// O jogo e "host-autoritativo": o primeiro jogador da sala simula bots, dano e placar;
// este servidor so autentica, monta as salas (ate 10 jogadores, 5 x 5) e repassa as mensagens.
const http = require('http');
const fs = require('fs');
const path = require('path');
const { WebSocketServer } = require('ws');

const PORT = process.env.PORT || 8080;
const SUPABASE_URL = (process.env.SUPABASE_URL || '').replace(/\/$/, '');
const SUPABASE_ANON_KEY = process.env.SUPABASE_ANON_KEY || '';
const DOWNLOAD_URL = process.env.DOWNLOAD_URL || '';
const ALLOW_GUESTS = process.env.ALLOW_GUESTS === '1';   // so para testes locais
const MAX_PLAYERS = 10, TEAM_SIZE = 5;
const MAPS = ['VILA ARIDA'];
const MODES = ['MATA-MATA', 'BOMBA'];

let nextId = 1;
const peers = new Map();   // id -> peer
const rooms = new Map();   // code -> room

function send(p, o) { if (p.ws.readyState === 1) p.ws.send(JSON.stringify(o)); }

function code() {
  const A = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  for (;;) {
    let c = '';
    for (let i = 0; i < 5; i++) c += A[Math.floor(Math.random() * A.length)];
    if (!rooms.has(c)) return c;
  }
}

function roomInfo(r) {
  return { t: 'room', code: r.code, map: r.map, mode: r.mode, priv: r.priv, host: r.host,
    players: r.players.map(p => ({ id: p.id, name: p.name, team: p.team, agent: p.agent })) };
}
function broadcastRoom(r) { const m = roomInfo(r); r.players.forEach(p => send(p, m)); }

function summary() {
  const list = [];
  for (const map of MAPS) for (const mode of MODES) {
    let players = 0, n = 0;
    for (const r of rooms.values()) if (!r.priv && r.map === map && r.mode === mode) { n++; players += r.players.length; }
    list.push({ map, mode, rooms: n, players });
  }
  return { t: 'list', rooms: list, online: peers.size };
}

function addToRoom(p, r) {
  const c = [0, 0];
  r.players.forEach(q => c[q.team]++);
  p.team = c[0] <= c[1] ? 0 : 1;
  p.room = r;
  r.players.push(p);
  broadcastRoom(r);
}

function createRoom(p, map, mode, priv) {
  const r = { code: code(), map, mode, priv, host: p.id, players: [] };
  rooms.set(r.code, r);
  addToRoom(p, r);
  return r;
}

function leave(p) {
  const r = p.room;
  if (!r) return;
  p.room = null;
  r.players = r.players.filter(q => q !== p);
  if (r.players.length === 0 || r.host === p.id) {
    // Sem o host nao ha quem simule a partida: a sala fecha.
    r.players.forEach(q => { q.room = null; send(q, { t: 'closed', err: 'O dono da sala saiu.' }); });
    rooms.delete(r.code);
  } else broadcastRoom(r);
}

async function verify(token) {
  if (!SUPABASE_URL) return null;
  const res = await fetch(SUPABASE_URL + '/auth/v1/user', { headers: { apikey: SUPABASE_ANON_KEY, Authorization: 'Bearer ' + token } });
  if (!res.ok) return null;
  const u = await res.json();
  if (!u || !u.id) return null;
  const meta = u.user_metadata || {};
  return { uid: u.id, name: String(meta.username || (u.email || 'jogador').split('@')[0]) };
}

const clean = (s, n) => String(s || '').replace(/[^\w \-\.]/g, '').trim().slice(0, n);
const pick = (v, list) => list.includes(v) ? v : list[0];

async function onMessage(p, m) {
  if (m.t === 'hello') {
    let who = null;
    try { who = m.token ? await verify(m.token) : null; } catch (e) { who = null; }
    if (!who && ALLOW_GUESTS) who = { uid: 'guest' + p.id, name: clean(m.name, 16) || 'Convidado' + p.id };
    if (!who) { send(p, { t: 'error', err: 'Login invalido ou expirado. Entre de novo.' }); return; }
    // Uma conexao por conta: a mais nova derruba a antiga.
    for (const q of peers.values()) if (q !== p && q.uid === who.uid) { send(q, { t: 'error', err: 'Sua conta entrou em outro lugar.' }); q.ws.close(); }
    p.uid = who.uid; p.name = clean(who.name, 16).toUpperCase() || 'JOGADOR';
    p.agent = Math.max(0, Math.min(11, m.agent | 0));
    p.authed = true;
    send(p, { t: 'welcome', id: p.id, name: p.name });
    send(p, summary());
    return;
  }
  if (!p.authed) { send(p, { t: 'error', err: 'Faca login primeiro.' }); return; }
  switch (m.t) {
    case 'list': send(p, summary()); break;
    case 'agent': p.agent = Math.max(0, Math.min(11, m.agent | 0)); break;
    case 'quick': {
      leave(p);
      const map = pick(m.map, MAPS), mode = pick(m.mode, MODES);
      let best = null;
      for (const r of rooms.values())
        if (!r.priv && r.map === map && r.mode === mode && r.players.length < MAX_PLAYERS && (!best || r.players.length > best.players.length)) best = r;
      if (best) addToRoom(p, best); else createRoom(p, map, mode, false);
      break;
    }
    case 'create': leave(p); createRoom(p, pick(m.map, MAPS), pick(m.mode, MODES), true); break;
    case 'join': {
      const r = rooms.get(clean(m.code, 5).toUpperCase());
      if (!r) { send(p, { t: 'error', err: 'Sala nao encontrada.' }); break; }
      if (r.players.length >= MAX_PLAYERS) { send(p, { t: 'error', err: 'Sala cheia.' }); break; }
      leave(p); addToRoom(p, r);
      break;
    }
    case 'leave': leave(p); send(p, summary()); break;
    case 'g': {
      // Mensagem de jogo: to = -1 host, 0 todos os outros, >0 um jogador.
      const r = p.room;
      if (!r || typeof m.d !== 'string' || m.d.length > 60000) break;
      const out = JSON.stringify({ t: 'g', from: p.id, d: m.d });
      const to = m.to | 0;
      for (const q of r.players) {
        if (q === p) continue;
        if (to === 0 || (to === -1 && q.id === r.host) || to === q.id) if (q.ws.readyState === 1) q.ws.send(out);
      }
      break;
    }
  }
}

const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png', '.jpg': 'image/jpeg' };
const server = http.createServer((req, res) => {
  const url = req.url.split('?')[0];
  if (url === '/health') { res.writeHead(200); res.end('ok'); return; }
  if (url === '/status') {
    res.writeHead(200, { 'content-type': 'application/json', 'access-control-allow-origin': '*' });
    res.end(JSON.stringify({ online: peers.size, rooms: rooms.size, download: DOWNLOAD_URL, teamSize: TEAM_SIZE }));
    return;
  }
  if (url === '/download') {
    if (!DOWNLOAD_URL) { res.writeHead(503, { 'content-type': 'text/plain; charset=utf-8' }); res.end('Download ainda nao publicado.'); return; }
    res.writeHead(302, { location: DOWNLOAD_URL }); res.end(); return;
  }
  const file = path.join(__dirname, 'public', url === '/' ? 'index.html' : path.normalize(url).replace(/^([\\/]|\.\.)+/, ''));
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end('nao encontrado'); return; }
    res.writeHead(200, { 'content-type': MIME[path.extname(file)] || 'application/octet-stream' });
    res.end(data);
  });
});

const wss = new WebSocketServer({ server, path: '/ws', maxPayload: 128 * 1024 });
wss.on('connection', ws => {
  const p = { id: nextId++, ws, name: '', team: 0, agent: 0, room: null, authed: false, alive: true };
  peers.set(p.id, p);
  ws.on('pong', () => { p.alive = true; });
  ws.on('message', raw => {
    let m; try { m = JSON.parse(raw.toString()); } catch (e) { return; }
    if (m && typeof m.t === 'string') onMessage(p, m).catch(e => console.error('msg', e));
  });
  ws.on('close', () => { leave(p); peers.delete(p.id); });
  ws.on('error', () => {});
});
// Derruba conexoes mortas (aba fechada, celular sem sinal) em ate ~10 s:
// o proxy do Render nem sempre repassa o fechamento, entao o servidor pergunta "ainda esta ai?" a cada 5 s.
setInterval(() => {
  for (const p of peers.values()) {
    if (!p.alive) { p.ws.terminate(); leave(p); peers.delete(p.id); continue; }
    p.alive = false; try { p.ws.ping(); } catch (e) {}
  }
}, 5000);

server.listen(PORT, () => console.log('Robot Fatal Shooter server na porta ' + PORT + (SUPABASE_URL ? '' : ' (sem Supabase configurado)')));
