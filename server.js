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
const AWAY_MS = +(process.env.AWAY_MS || 60000);
const HOST_SWAP_MS = +(process.env.HOST_SWAP_MS || 6000);   // quem cai (ou sai do app) segura a vaga por 60 s e volta para a mesma sala
const MAPS = ['VILA ARIDA'];
const MODES = ['MATA-MATA', 'BOMBA'];

let nextId = 1;
const peers = new Map();   // id -> peer
const rooms = new Map();   // code -> room

function send(p, o) { if (p.ws && p.ws.readyState === 1) p.ws.send(JSON.stringify(o)); }

// Conexao caiu: se estava numa sala, vira "ausente" e guarda a vaga; senao sai de vez.
function drop(p) {
  if (!peers.has(p.id) || p.away) return;
  if (p.room && p.authed && !p.kicked) {
    p.away = true; p.awayAt = Date.now(); p.ws = null;
    broadcastRoom(p.room);
  } else { leave(p); peers.delete(p.id); }
}

function code() {
  const A = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  for (;;) {
    let c = '';
    for (let i = 0; i < 5; i++) c += A[Math.floor(Math.random() * A.length)];
    if (!rooms.has(c)) return c;
  }
}

function roomInfo(r) {
  return { t: 'room', code: r.code, map: r.map, mode: r.mode, priv: r.priv, bots: r.bots, host: r.host,
    players: r.players.map(p => ({ id: p.id, name: p.name, team: p.team, agent: p.agent, away: !!p.away })) };
}
function broadcastRoom(r) { const m = roomInfo(r); r.players.forEach(p => send(p, m)); }

function summary() {
  const list = [];
  for (const map of MAPS) for (const mode of MODES) {
    let players = 0, n = 0;
    for (const r of rooms.values()) if (!r.priv && !r.custom && r.map === map && r.mode === mode) { n++; players += r.players.length; }
    list.push({ map, mode, rooms: n, players });
  }
  // Salas abertas (publicas) para o jogador escolher em qual entrar.
  const open = [];
  for (const r of rooms.values()) {
    if (r.priv) continue;
    const h = r.players.find(q => q.id === r.host);
    open.push({ code: r.code, map: r.map, mode: r.mode, host: h ? h.name : '?', players: r.players.length, bots: r.bots, custom: !!r.custom });
  }
  open.sort((a, b) => (b.custom - a.custom) || (b.players - a.players));
  return { t: 'list', rooms: list, open: open.slice(0, 20), online: peers.size };
}

function teamCount(r) { const c = [0, 0]; r.players.forEach(q => c[q.team]++); return c; }
// side: 0 BALL, 1 MOLOCH, 2 (ou ausente) automatico. Time cheio cai no outro.
function addToRoom(p, r, side) {
  const c = teamCount(r);
  if ((side === 0 || side === 1) && c[side] < TEAM_SIZE) p.team = side;
  else p.team = c[0] <= c[1] ? 0 : 1;
  p.room = r;
  r.players.push(p);
  broadcastRoom(r);
}

function createRoom(p, map, mode, priv, side, bots = true, custom = false) {
  const r = { code: code(), map, mode, priv, bots, custom, host: p.id, players: [] };
  rooms.set(r.code, r);
  addToRoom(p, r, side);
  return r;
}

function leave(p) {
  const r = p.room;
  if (!r) return;
  p.room = null;
  r.players = r.players.filter(q => q !== p);
  if (r.players.length === 0) { rooms.delete(r.code); return; }
  // O dono saiu: outro jogador assume a partida (a sala nao morre).
  if (r.host === p.id) r.host = pickHost(r);
  broadcastRoom(r);
}

// Novo dono: o primeiro jogador conectado (se todos estiverem fora, o primeiro da lista).
function pickHost(r) {
  const on = r.players.find(q => !q.away);
  return (on || r.players[0]).id;
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
const sideOf = m => (m.side === 0 || m.side === 1) ? m.side : 2;

async function onMessage(c, m) {
  const p = c.p;
  if (m.t === 'hello') {
    let who = null;
    try { who = m.token ? await verify(m.token) : null; } catch (e) { who = null; }
    if (!who && ALLOW_GUESTS) who = { uid: 'guest:' + (clean(m.name, 16) || p.id), name: clean(m.name, 16) || 'Convidado' + p.id };
    if (!who) { send(p, { t: 'error', err: 'Login invalido ou expirado. Entre de novo.' }); return; }
    // Voltando de uma queda: assume o jogador "ausente" (mesma conta e mesma ficha) e continua na sala.
    if (m.resume) {
      for (const g of peers.values()) {
        if (g === p || !g.away || g.resume !== m.resume || g.uid !== who.uid) continue;
        peers.delete(p.id);
        g.ws = p.ws; g.alive = true; g.away = false; c.p = g;
        send(g, { t: 'welcome', id: g.id, name: g.name, resume: g.resume, resumed: true });
        if (g.room) broadcastRoom(g.room);
        else send(g, { t: 'closed', err: 'A sala fechou enquanto voce estava fora.' });
        return;
      }
    }
    // Uma conexao por conta: a mais nova derruba a antiga.
    for (const q of peers.values()) {
      if (q === p || q.uid !== who.uid) continue;
      if (q.away) { leave(q); peers.delete(q.id); continue; }
      q.kicked = true; send(q, { t: 'error', err: 'Sua conta entrou em outro lugar.' }); q.ws.close();
    }
    p.uid = who.uid; p.name = clean(who.name, 16).toUpperCase() || 'JOGADOR';
    p.agent = Math.max(0, Math.min(11, m.agent | 0));
    p.authed = true;
    p.resume = require('crypto').randomBytes(12).toString('hex');
    send(p, { t: 'welcome', id: p.id, name: p.name, resume: p.resume, resumed: false });
    send(p, summary());
    return;
  }
  if (!p.authed) { send(p, { t: 'error', err: 'Faca login primeiro.' }); return; }
  switch (m.t) {
    case 'list': send(p, summary()); break;
    case 'agent': p.agent = Math.max(0, Math.min(11, m.agent | 0)); break;
    case 'quick': {
      leave(p);
      const map = pick(m.map, MAPS), mode = pick(m.mode, MODES), side = sideOf(m);
      // Prefere a sala mais cheia que ainda tenha vaga no time escolhido.
      let best = null, bestFits = false;
      for (const r of rooms.values()) {
        if (r.priv || r.custom || r.map !== map || r.mode !== mode || r.players.length >= MAX_PLAYERS) continue;
        const fits = side > 1 || teamCount(r)[side] < TEAM_SIZE;
        if (!best || (fits && !bestFits) || (fits === bestFits && r.players.length > best.players.length)) { best = r; bestFits = fits; }
      }
      if (best && bestFits) addToRoom(p, best, side); else if (best && side > 1) addToRoom(p, best, side); else createRoom(p, map, mode, false, side);
      break;
    }
    case 'create': {
      // priv: so entra com o codigo; bots: false = so os jogadores convidados, sem bots completando.
      leave(p);
      const priv = m.priv !== false, bots = m.bots !== false;
      createRoom(p, pick(m.map, MAPS), pick(m.mode, MODES), priv, sideOf(m), bots, true);
      break;
    }
    case 'join': {
      const r = rooms.get(clean(m.code, 5).toUpperCase());
      if (!r) { send(p, { t: 'error', err: 'Sala nao encontrada.' }); break; }
      if (r.players.length >= MAX_PLAYERS) { send(p, { t: 'error', err: 'Sala cheia.' }); break; }
      leave(p); addToRoom(p, r, sideOf(m));
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
        if (to === 0 || (to === -1 && q.id === r.host) || to === q.id) if (q.ws && q.ws.readyState === 1) q.ws.send(out);
      }
      break;
    }
  }
}

const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.webmanifest': 'application/manifest+json', '.json': 'application/json' };
// Video do menu (feito no Higgsfield): o servidor busca uma vez e guarda na memoria; o navegador guarda em cache.
const PROMO_URL = process.env.PROMO_URL || 'https://d2ol7oe51mr4n9.cloudfront.net/user_3LzTm4Qk1kcTVaub1FgPtg30zAP/170f7c08-41c9-4df9-837a-c246c671c166.mp4';
let promoBuf = null, promoLoading = null;
function loadPromo() {
  if (promoBuf) return Promise.resolve(promoBuf);
  if (!promoLoading) promoLoading = fetch(PROMO_URL).then(r => { if (!r.ok) throw new Error('promo ' + r.status); return r.arrayBuffer(); })
    .then(b => (promoBuf = Buffer.from(b))).finally(() => { promoLoading = null; });
  return promoLoading;
}
function servePromo(req, res) {
  loadPromo().then(buf => {
    const total = buf.length;
    const h = { 'content-type': 'video/mp4', 'accept-ranges': 'bytes', 'cache-control': 'public, max-age=604800', 'access-control-allow-origin': '*' };
    const m = /bytes=(\d*)-(\d*)/.exec(req.headers.range || '');
    if (m) {
      let start = m[1] ? +m[1] : total - +m[2], end = m[1] && m[2] ? +m[2] : total - 1;
      if (!m[1]) end = total - 1;
      if (start >= total || start < 0 || end < start) { res.writeHead(416, { 'content-range': 'bytes */' + total }); res.end(); return; }
      end = Math.min(end, total - 1);
      h['content-range'] = 'bytes ' + start + '-' + end + '/' + total; h['content-length'] = end - start + 1;
      res.writeHead(206, h); res.end(buf.subarray(start, end + 1)); return;
    }
    h['content-length'] = total; res.writeHead(200, h); res.end(buf);
  }).catch(() => { res.writeHead(502); res.end('video indisponivel'); });
}

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
  if (url === '/promo/menu.mp4') { servePromo(req, res); return; }
  const file = path.join(__dirname, 'public', url === '/' ? 'index.html' : path.normalize(url).replace(/^([\\/]|\.\.)+/, ''));
  // sem cache "as cegas": o navegador sempre confere se o arquivo mudou (evita misturar build velho com novo)
  let target = file;
  try {
    if (fs.statSync(target).isDirectory()) {
      if (!url.endsWith('/')) { res.writeHead(301, { location: url + '/' }); res.end(); return; }
      target = path.join(target, 'index.html');
    }
  } catch (e) {}
  fs.stat(target, (err, st) => {
    const file = target;
    if (err || !st.isFile()) { res.writeHead(404); res.end('nao encontrado'); return; }
    const etag = '"' + st.size.toString(16) + '-' + Math.floor(st.mtimeMs).toString(16) + '"';
    const headers = { 'content-type': MIME[path.extname(file)] || 'application/octet-stream', 'cache-control': 'no-cache', etag };
    if (req.headers['if-none-match'] === etag) { res.writeHead(304, headers); res.end(); return; }
    fs.readFile(file, (err2, data) => {
      if (err2) { res.writeHead(404); res.end('nao encontrado'); return; }
      res.writeHead(200, headers);
      res.end(data);
    });
  });
});

const wss = new WebSocketServer({ server, path: '/ws', maxPayload: 128 * 1024 });
wss.on('connection', ws => {
  // c.p pode trocar: ao voltar de uma queda, a conexao nova assume o jogador antigo.
  const c = { p: { id: nextId++, ws, name: '', team: 0, agent: 0, room: null, authed: false, alive: true } };
  peers.set(c.p.id, c.p);
  ws.on('pong', () => { c.p.alive = true; });
  ws.on('message', raw => {
    let m; try { m = JSON.parse(raw.toString()); } catch (e) { return; }
    if (m && typeof m.t === 'string') onMessage(c, m).catch(e => console.error('msg', e));
  });
  ws.on('close', () => { if (c.p.ws === ws) drop(c.p); });
  ws.on('error', () => {});
});
// Derruba conexoes mortas (aba fechada, celular sem sinal) em ate ~10 s:
// o proxy do Render nem sempre repassa o fechamento, entao o servidor pergunta "ainda esta ai?" a cada 5 s.
setInterval(() => {
  const now = Date.now();
  // Dono fora ha mais de 6 s e ha outro jogador conectado: troca o dono.
  for (const r of rooms.values()) {
    const h = r.players.find(q => q.id === r.host);
    if (h && h.away && now - h.awayAt > HOST_SWAP_MS && r.players.some(q => !q.away)) { r.host = pickHost(r); broadcastRoom(r); }
  }
  for (const p of peers.values()) {
    if (p.away) { if (now - p.awayAt > AWAY_MS) { leave(p); peers.delete(p.id); } continue; }
    if (!p.alive) { const w = p.ws; drop(p); try { w.terminate(); } catch (e) {} continue; }
    p.alive = false; try { p.ws.ping(); } catch (e) {}
  }
}, 5000);

server.listen(PORT, () => console.log('Robot Fatal Shooter server na porta ' + PORT + (SUPABASE_URL ? '' : ' (sem Supabase configurado)')));
