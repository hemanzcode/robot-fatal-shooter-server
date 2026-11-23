// Fatal Shooter - servidor de lobby, salas e relay.
// O jogo e "host-autoritativo": o primeiro jogador da sala simula bots, dano e placar;
// este servidor so autentica, monta as salas (ate 10 jogadores, 5 x 5) e repassa as mensagens.
const http = require('http');
const fs = require('fs');
const path = require('path');
const { WebSocketServer } = require('ws');
const prog = require('./progresso');
const amigos = require('./amigos');

const PORT = process.env.PORT || 8080;
const SUPABASE_URL = (process.env.SUPABASE_URL || '').replace(/\/$/, '');
const SUPABASE_ANON_KEY = process.env.SUPABASE_ANON_KEY || '';
const DOWNLOAD_URL = process.env.DOWNLOAD_URL || '';
const ALLOW_GUESTS = process.env.ALLOW_GUESTS === '1';   // so para testes locais
const MAX_PLAYERS = 10, TEAM_SIZE = 5;
const AWAY_MS = +(process.env.AWAY_MS || 60000);
const HOST_SWAP_MS = +(process.env.HOST_SWAP_MS || 6000);   // quem cai (ou sai do app) segura a vaga por 60 s e volta para a mesma sala
const PRESENCA_MIN_MS = +(process.env.PRESENCA_MIN_S || 60) * 1000;   // quem entrou no finzinho nao ganha XP
const MAPS = ['MERCADO', 'NO GELO'];
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
  mudou(p.uid);
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
    players: r.players.map(p => ({ id: p.id, name: p.name, team: p.team, agent: p.agent, away: !!p.away, lvl: p.lvl || 1, equip: p.equip || ['', '', '', '', ''] })) };
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
// side: 0 BAAL, 1 MOLOCH, 2 (ou ausente) automatico. Time cheio cai no outro.
function addToRoom(p, r, side) {
  const c = teamCount(r);
  if ((side === 0 || side === 1) && c[side] < TEAM_SIZE) p.team = side;
  else p.team = c[0] <= c[1] ? 0 : 1;
  p.room = r;
  p.joinedAt = Date.now();
  r.players.push(p);
  broadcastRoom(r);
  mudou(p.uid);
}

function createRoom(p, map, mode, priv, side, bots = true, custom = false) {
  const r = { code: code(), map, mode, priv, bots, custom, host: p.id, players: [], startedAt: Date.now(), lastResult: 0 };
  rooms.set(r.code, r);
  addToRoom(p, r, side);
  return r;
}

function leave(p) {
  const r = p.room;
  if (!r) return;
  p.room = null;
  mudou(p.uid);
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
  return { uid: u.id, email: u.email || '', name: String(meta.username || (u.email || 'jogador').split('@')[0]) };
}

const clean = (s, n) => String(s || '').replace(/[^\w \-\.]/g, '').trim().slice(0, n);
const pick = (v, list) => list.includes(v) ? v : list[0];
const sideOf = m => (m.side === 0 || m.side === 1) ? m.side : 2;

// ---------------------------- Progresso (XP, SHOTS, skins) ----------------------------
async function sendProfile(p) {
  if (!prog.ativo() || !p.uid) return;
  try { applyProfile(p, await prog.carregar(p.uid, p.name)); } catch (e) { console.error('perfil', e.message); }
}
// Perfil novo: manda para o jogador e, se a skin equipada mudou, avisa a sala (os outros veem a arma nova).
function applyProfile(p, perfil) {
  const pub = prog.publico(perfil);
  const before = (p.equip || []).join(',') + '/' + (p.lvl || 1);
  p.equip = pub.equip; p.lvl = pub.lvl;
  send(p, pub);
  if (p.room && before !== p.equip.join(',') + '/' + p.lvl) broadcastRoom(p.room);
}
function creditado(r) {
  for (const q of peers.values()) if (q.uid === r.uid) { applyProfile(q, r.perfil); send(q, { t: 'pixok', shots: r.shots }); }
}
// Fim de partida: so o dono da sala manda o placar. O servidor confere duracao e quem estava na sala.
async function onResult(p, m) {
  const r = p.room;
  if (!r || r.host !== p.id || !prog.ativo() || !Array.isArray(m.players)) return;
  const now = Date.now();
  if (now - r.startedAt < prog.DURACAO_MIN * 1000 || now - r.lastResult < 30000) { r.startedAt = now; return; }
  r.lastResult = now; r.startedAt = now;
  const jogadores = new Map();
  for (const q of r.players) {
    if (!q.authed || q.away || !q.uid || now - (q.joinedAt || now) < PRESENCA_MIN_MS) continue;
    if (String(q.uid).startsWith('guest:') && !ALLOW_GUESTS) continue;
    jogadores.set(q.id, { uid: q.uid, nome: q.name, time: q.team });
  }
  // Regra: XP e SHOTS so em partida online SEM bots (sala "so os amigos" e ninguem controlado pelo computador).
  if (r.bots || m.players.some(e => !e || (e.id | 0) === 0)) {
    for (const id of jogadores.keys()) send(peers.get(id), { t: 'reward', xp: 0, shots: 0, lines: [], err: 'Partidas com bots nao valem XP nem SHOTS. Crie uma sala SO OS AMIGOS (SEM BOTS) para ganhar.' });
    return;
  }
  const vencedor = m.winner === 0 || m.winner === 1 ? m.winner : -1;
  const res = await prog.fimDePartida(m.players, vencedor, jogadores);
  for (const { id, r: out } of res) {
    const q = peers.get(id);
    if (!q || !out.perfil) continue;
    send(q, out.premio);
    applyProfile(q, out.perfil);
  }
}

// ---------------------------- Amigos ----------------------------
// Cada jogador conectado guarda a propria lista (p.fr). Quando alguem entra, sai ou troca de sala,
// os amigos online recebem a lista atualizada (agrupado a cada meio segundo).
function peerDe(uid) { for (const q of peers.values()) if (q.authed && q.uid === uid && !q.kicked) return q; return null; }
function estado(uid) {
  const q = peerDe(uid);
  if (!q) return { st: 0 };
  if (q.room) return { st: 2, code: q.room.code, map: q.room.map, mode: q.room.mode, n: q.room.players.length };
  return { st: q.away ? 0 : 1 };
}
function sendFriends(p) {
  if (!p.fr) return;
  const fr = [];
  for (const [uid, i] of p.fr.acc) fr.push(Object.assign({ uid, name: i.nome, lvl: i.nivel || 1 }, estado(uid)));
  fr.sort((a, b) => (b.st - a.st) || a.name.localeCompare(b.name));
  const lista = m => [...m].map(([uid, i]) => ({ uid, name: i.nome, lvl: i.nivel || 1 }));
  send(p, { t: 'friends', fr, inc: lista(p.fr.inc), sent: lista(p.fr.sent) });
}
async function loadFriends(p) {
  if (!amigos.ativo() || !p.uid) return;
  try { p.fr = await amigos.carregar(p.uid); sendFriends(p); } catch (e) { console.error('amigos', e.message); }
}
async function reloadUid(uid) { const q = peerDe(uid); if (q) await loadFriends(q); }
const sujos = new Set(); let flushT = null;
function mudou(uid) {
  if (!uid) return;
  sujos.add(uid);
  if (!flushT) flushT = setTimeout(() => {
    flushT = null;
    const s = new Set(sujos); sujos.clear();
    for (const q of peers.values()) if (q.fr && !q.away) for (const u of s) if (q.fr.acc.has(u)) { sendFriends(q); break; }
  }, 500);
}
async function onFriend(p, m) {
  if (!amigos.ativo()) { send(p, { t: 'fmsg', err: 'Amigos indisponivel no momento.' }); return; }
  const uid = String(m.uid || '');
  switch (m.t) {
    case 'friends': await loadFriends(p); break;
    case 'fadd': {
      const nome = clean(m.name, 16).toUpperCase();
      if (!nome) { send(p, { t: 'fmsg', err: 'Digite o nome do jogador.' }); return; }
      if (nome === p.name) { send(p, { t: 'fmsg', err: 'Esse e voce :)' }); return; }
      const on = [...peers.values()].find(q => q.authed && q.name === nome);
      const alvo = on ? on.uid : await amigos.acharPorNome(nome);
      if (!alvo) { send(p, { t: 'fmsg', err: 'Jogador ' + nome + ' nao encontrado.' }); return; }
      const r = await amigos.pedir(p.uid, alvo);
      if (r.erro) { send(p, { t: 'fmsg', err: r.erro }); return; }
      send(p, { t: 'fmsg', ok: true, err: r.ok === 'amigos' ? 'Agora voces sao amigos!' : 'Pedido enviado para ' + nome + '.' });
      await loadFriends(p); await reloadUid(alvo);
      const q = peerDe(alvo);
      if (q && r.ok === 'pedido') send(q, { t: 'freq', name: p.name });
      break;
    }
    case 'fok': {
      const r = await amigos.aceitar(p.uid, uid);
      if (r.erro) { send(p, { t: 'fmsg', err: r.erro }); return; }
      await loadFriends(p); await reloadUid(uid);
      const q = peerDe(uid);
      if (q) send(q, { t: 'fmsg', ok: true, err: p.name + ' aceitou seu pedido de amizade!' });
      break;
    }
    case 'fno': {
      await amigos.remover(p.uid, uid);
      await loadFriends(p); await reloadUid(uid);
      break;
    }
    case 'finv': {
      if (!p.fr || !p.fr.acc.has(uid)) { send(p, { t: 'fmsg', err: 'So da para convidar amigos.' }); return; }
      if (!p.room) { send(p, { t: 'fmsg', err: 'Entre ou crie uma sala para convidar.' }); return; }
      const q = peerDe(uid);
      if (!q || q.away) { send(p, { t: 'fmsg', err: 'Seu amigo esta offline.' }); return; }
      if (q.room === p.room) { send(p, { t: 'fmsg', err: q.name + ' ja esta na sua sala.' }); return; }
      const now = Date.now();
      p.invAt = p.invAt || {};
      if (now - (p.invAt[uid] || 0) < 10000) { send(p, { t: 'fmsg', err: 'Aguarde um pouco para convidar de novo.' }); return; }
      p.invAt[uid] = now;
      send(q, { t: 'finvite', name: p.name, code: p.room.code, map: p.room.map, mode: p.room.mode });
      send(p, { t: 'fmsg', ok: true, err: 'Convite enviado para ' + q.name + '.' });
      break;
    }
  }
}

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
        g.ws = p.ws; g.alive = true; g.away = false; c.p = g; mudou(g.uid);
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
    p.email = who.email || '';
    send(p, { t: 'welcome', id: p.id, name: p.name, resume: p.resume, resumed: false });
    send(p, summary());
    sendProfile(p);
    amigos.lembrarNome(p.uid, p.name);
    loadFriends(p).then(() => mudou(p.uid));
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
    case 'profile': sendProfile(p); break;
    case 'buy': {
      if (!prog.ativo()) { send(p, { t: 'error', err: 'Loja indisponivel no momento.' }); break; }
      const r = await prog.comprar(p.uid, p.name, String(m.skin || ''), m.w);
      if (r.erro) { send(p, { t: 'shoperr', err: r.erro }); break; }
      applyProfile(p, r.perfil);
      break;
    }
    case 'equip': {
      if (!prog.ativo()) break;
      const r = await prog.equipar(p.uid, p.name, String(m.skin || ''), m.w);
      if (r.erro) { send(p, { t: 'shoperr', err: r.erro }); break; }
      applyProfile(p, r.perfil);
      break;
    }
    case 'result': await onResult(p, m); break;
    case 'friends': case 'fadd': case 'fok': case 'fno': case 'finv': await onFriend(p, m); break;
    case 'pix': {
      const r = await prog.criarPix(p.uid, p.name, p.email, String(m.pack || ''));
      if (r.erro) send(p, { t: 'shoperr', err: r.erro }); else send(p, r.pix);
      break;
    }
    case 'pixcheck': {
      // O jogo pergunta de tempos em tempos enquanto o QR esta na tela (caso o webhook demore).
      const r = await prog.conferirPagamento(String(m.id || '')).catch(() => null);
      if (r) creditado(r);
      break;
    }
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

const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.svg': 'image/svg+xml', '.webmanifest': 'application/manifest+json', '.json': 'application/json' };
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

// Servidores ICE da voz do time. STUN do Google sempre; TURN (para redes que bloqueiam conexao direta) se configurado:
// Cloudflare (CF_TURN_KEY_ID + CF_TURN_TOKEN, credenciais de 24 h) ou um TURN fixo (TURN_URL, TURN_USER, TURN_PASS).
let iceCache = null, iceAt = 0;
async function iceServers() {
  const base = [{ urls: ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302'] }];
  if (process.env.CF_TURN_KEY_ID && process.env.CF_TURN_TOKEN) {
    if (iceCache && Date.now() - iceAt < 3600e3) return iceCache;
    try {
      const r = await fetch('https://rtc.live.cloudflare.com/v1/turn/keys/' + process.env.CF_TURN_KEY_ID + '/credentials/generate-ice-servers', {
        method: 'POST', headers: { authorization: 'Bearer ' + process.env.CF_TURN_TOKEN, 'content-type': 'application/json' }, body: JSON.stringify({ ttl: 86400 })
      });
      const j = await r.json();
      if (j && j.iceServers) { iceCache = base.concat(j.iceServers); iceAt = Date.now(); return iceCache; }
    } catch (e) {}
  }
  if (process.env.TURN_URL) base.push({ urls: process.env.TURN_URL.split(','), username: process.env.TURN_USER || '', credential: process.env.TURN_PASS || '' });
  return base;
}
function serveIce(res) {
  iceServers().then(list => { res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' }); res.end(JSON.stringify({ iceServers: list })); });
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
  if (url === '/ranking.json') {
    prog.ranking().then(list => { res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'max-age=30', 'access-control-allow-origin': '*' }); res.end(JSON.stringify(list)); })
      .catch(e => { console.error('ranking', e.message); res.writeHead(503); res.end('[]'); });
    return;
  }
  if (url === '/pix/webhook') {
    // Mercado Pago avisa aqui; respondemos 200 sempre e conferimos o pagamento direto na API deles.
    let body = '';
    req.on('data', d => { if (body.length < 20000) body += d; });
    req.on('end', () => {
      res.writeHead(200); res.end('ok');
      let j = {}; try { j = JSON.parse(body || '{}'); } catch (e) {}
      const q = Object.fromEntries(new URLSearchParams(req.url.split('?')[1] || ''));
      prog.webhookPix(req.headers, q, j).then(r => { if (r) creditado(r); }).catch(e => console.error('pix webhook', e.message));
    });
    return;
  }
  if (url === '/voice/ice') { serveIce(res); return; }
  // Keep Unity's large data under GitHub's 50 MB recommendation while
  // streaming it as the single file path expected by the WebGL loader.
  if (url === '/play/Build/play.data.unityweb') {
    const dir = path.join(__dirname, 'public', 'play', 'Build');
    fs.readdir(dir, (readErr, names) => {
      const legacyParts = readErr ? [] : names.filter(n => /^play\.data\.unityweb\.part\d+$/.test(n)).sort();
      const v2Dir = path.join(dir, 'data-v2');
      fs.readdir(v2Dir, (v2Err, v2Names) => {
        const v2Parts = v2Err ? [] : v2Names.filter(n => /^play\.data\.unityweb\.part\d+$/.test(n)).sort();
        const useV2 = v2Parts.length > 0 && v2Parts.every((name, i) => name === `play.data.unityweb.part${String(i).padStart(2, '0')}`);
        const parts = useV2 ? v2Parts : legacyParts;
        const partsDir = useV2 ? v2Dir : dir;
        if (!parts.length) { res.writeHead(404); res.end('build WebGL indisponivel'); return; }
        Promise.all(parts.map(name => fs.promises.stat(path.join(partsDir, name)))).then(stats => {
          const size = stats.reduce((total, stat) => total + stat.size, 0);
          const tag = '"webgl-' + (useV2 ? 'v2-' : 'legacy-') + stats.map(stat => stat.size.toString(16) + '-' + Math.floor(stat.mtimeMs).toString(16)).join('-') + '"';
          const headers = { 'content-type': 'application/octet-stream', 'cache-control': 'no-cache', etag: tag, 'content-length': size };
          if (req.headers['if-none-match'] === tag) { res.writeHead(304, headers); res.end(); return; }
          res.writeHead(200, headers);
          if (req.method === 'HEAD') { res.end(); return; }
          let i = 0;
          const sendPart = () => {
            if (i >= parts.length) { res.end(); return; }
            const stream = fs.createReadStream(path.join(partsDir, parts[i++]));
            stream.on('error', err => { console.error('WebGL data stream', err); res.destroy(err); });
            stream.on('end', sendPart);
            stream.pipe(res, { end: false });
          };
          sendPart();
        }).catch(err => { console.error('WebGL data parts', err); if (!res.headersSent) res.writeHead(500); res.end(); });
      });
    });
    return;
  }
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

loadPromo().catch(e => console.error('promo', e.message));   // ja deixa o video na memoria (o primeiro jogador nao espera)
server.listen(PORT, () => console.log('Fatal Shooter server na porta ' + PORT + (SUPABASE_URL ? '' : ' (sem Supabase configurado)')));

