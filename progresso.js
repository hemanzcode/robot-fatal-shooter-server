// Fatal Shooter - progresso da conta: XP, nivel, patente, SHOTS (moeda), skins e PIX.
// Quem decide tudo e o servidor: o jogo so pede ("comprar X", "fim de partida") e recebe o perfil pronto.
// Guarda no Supabase (tabela perfis/pagamentos, so a chave de servico escreve). Sem a chave e com
// ALLOW_GUESTS=1, usa memoria (para testar local).
const crypto = require('crypto');
const PRECOS = require('./precos.json');

const SUPABASE_URL = (process.env.SUPABASE_URL || '').replace(/\/$/, '');
const SERVICE_KEY = process.env.SUPABASE_SERVICE_KEY || '';
const MP_TOKEN = process.env.MP_ACCESS_TOKEN || '';            // Mercado Pago (PIX)
const MP_WEBHOOK_SECRET = process.env.MP_WEBHOOK_SECRET || '';  // assinatura do webhook (opcional, recomendado)
// Periodo de testes: toda conta nova comeca com estes SHOTS (CREDITO_INICIAL=0 no Render desliga).
const CREDITO_INICIAL = +(process.env.CREDITO_INICIAL ?? 10000);
const PUBLIC_URL = (process.env.PUBLIC_URL || 'https://fatalshooter.com.br').replace(/\/$/, '');

// ------------------------------------------------------------------ regras
const XP = { abate: 10, headshot: 5, grande: 30, objetivo: 25, terminar: 50, vitoria: 100, empate: 50, primeiraVitoria: 150 };
const TETO_DIA = 4000, APOS_TETO = 0.25;
const SHOTS_POR_XP = 6, SHOTS_POR_NIVEL = 100;
const DURACAO_MIN = +(process.env.PARTIDA_MIN_S || 120);   // partida mais curta que isso nao rende nada
// Pacotes PIX (valores em reais). Ajuste aqui.
const PACOTES = [
  { id: 'p500', shots: 500, valor: 4.90 },
  { id: 'p1200', shots: 1200, valor: 9.90 },
  { id: 'p2600', shots: 2600, valor: 19.90 },
  { id: 'p7000', shots: 7000, valor: 49.90 },
];
const PATENTES = [[1, 'RECRUTA'], [5, 'SOLDADO'], [10, 'SARGENTO'], [20, 'ELITE'], [35, 'LENDA']];

const xpParaPassar = n => 1000 + 100 * (n - 1);       // XP do nivel n para o n+1
function nivelDe(xpTotal) {
  let n = 1, resto = xpTotal;
  while (resto >= xpParaPassar(n)) { resto -= xpParaPassar(n); n++; }
  return { nivel: n, noNivel: resto, precisa: xpParaPassar(n) };
}
function patente(n) { let p = PATENTES[0][1]; for (const [min, nome] of PATENTES) if (n >= min) p = nome; return p; }
function mult(pos, total) {
  if (pos === 0) return 1.5; if (pos === 1) return 1.35; if (pos === 2) return 1.2;
  return pos < Math.ceil(total / 2) ? 1.1 : 1.0;
}
// Dia no fuso de Brasilia (o teto diario vira a meia-noite).
const hoje = () => new Date(Date.now() - 3 * 3600e3).toISOString().slice(0, 10);

// ------------------------------------------------------------------ armazenamento
const memoria = new Map();      // uid -> perfil (teste local)
const pagMem = new Map();
const usaSupabase = () => !!(SUPABASE_URL && SERVICE_KEY);
const ativo = () => usaSupabase() || process.env.ALLOW_GUESTS === '1';

function novo(uid, nome) {
  return { user_id: uid, nome, xp_total: 0, nivel: 1, shots: CREDITO_INICIAL, xp_hoje: 0, dia: hoje(), vitoria_hoje: false,
    ultima: null, skins: [], equipadas: {}, partidas: 0, vitorias: 0, abates: 0, versao: 0 };
}

async function sb(path, opt = {}) {
  const res = await fetch(SUPABASE_URL + '/rest/v1/' + path, {
    method: opt.method || 'GET',
    headers: { apikey: SERVICE_KEY, Authorization: 'Bearer ' + SERVICE_KEY, 'content-type': 'application/json', Prefer: opt.prefer || 'return=representation' },
    body: opt.body ? JSON.stringify(opt.body) : undefined,
  });
  const txt = await res.text();
  if (!res.ok) throw new Error('supabase ' + res.status + ' ' + txt.slice(0, 200));
  return txt ? JSON.parse(txt) : null;
}

async function carregar(uid, nome) {
  if (!usaSupabase()) {
    if (!memoria.has(uid)) memoria.set(uid, novo(uid, nome));
    const p = memoria.get(uid); if (nome) p.nome = nome; return JSON.parse(JSON.stringify(p));
  }
  let r = await sb('perfis?user_id=eq.' + encodeURIComponent(uid) + '&select=*');
  if (!r.length) {
    await sb('perfis', { method: 'POST', prefer: 'resolution=ignore-duplicates,return=representation', body: novo(uid, nome) });
    r = await sb('perfis?user_id=eq.' + encodeURIComponent(uid) + '&select=*');
  } else if (nome && r[0].nome !== nome) {
    await sb('perfis?user_id=eq.' + encodeURIComponent(uid), { method: 'PATCH', prefer: 'return=minimal', body: { nome } });
    r[0].nome = nome;
  }
  return r[0];
}

// Grava com trava otimista: so escreve se ninguem mudou o perfil desde a leitura (campo versao).
async function gravar(antes, depois) {
  depois.versao = (antes.versao || 0) + 1;
  if (!usaSupabase()) {
    const atual = memoria.get(antes.user_id);
    if (!atual || atual.versao !== antes.versao) return false;
    memoria.set(antes.user_id, JSON.parse(JSON.stringify(depois))); return true;
  }
  const campos = {};
  for (const k of ['nome', 'xp_total', 'nivel', 'shots', 'xp_hoje', 'dia', 'vitoria_hoje', 'ultima', 'skins', 'equipadas', 'partidas', 'vitorias', 'abates', 'versao']) campos[k] = depois[k];
  campos.atualizado = new Date().toISOString();
  const r = await sb('perfis?user_id=eq.' + encodeURIComponent(antes.user_id) + '&versao=eq.' + (antes.versao || 0), { method: 'PATCH', body: campos });
  return Array.isArray(r) && r.length === 1;
}

// Le, aplica a mudanca e grava; repete se outra operacao mexeu no perfil no meio.
async function alterar(uid, nome, fn) {
  for (let i = 0; i < 5; i++) {
    const antes = await carregar(uid, nome);
    const depois = JSON.parse(JSON.stringify(antes));
    const extra = fn(depois);
    if (extra && extra.erro) return extra;
    if (await gravar(antes, depois)) return Object.assign({ perfil: depois }, extra || {});
  }
  return { erro: 'Servidor ocupado, tente de novo.' };
}

// Perfil como o jogo recebe.
function publico(p) {
  const n = nivelDe(p.xp_total);
  return { t: 'profile', xp: p.xp_total, lvl: n.nivel, lvlXp: n.noNivel, lvlNeed: n.precisa, rank: patente(n.nivel),
    shots: p.shots, owned: p.skins || [], equip: [0, 1, 2, 3, 4].map(w => (p.equipadas || {})[w] || ''),
    lastXp: p.ultima ? p.ultima.xp : 0, lastShots: p.ultima ? p.ultima.shots : 0, pix: !!MP_TOKEN,
    packs: PACOTES.map(x => ({ id: x.id, shots: x.shots, price: x.valor.toFixed(2).replace('.', ',') })) };
}

// ------------------------------------------------------------------ loja
async function comprar(uid, nome, skin, w) {
  w = w | 0;
  const preco = PRECOS[skin];
  if (!preco || w < 0 || w > 4) return { erro: 'Skin desconhecida.' };
  const item = skin + '|' + w;
  return alterar(uid, nome, p => {
    if ((p.skins || []).includes(item)) return { erro: 'Voce ja tem essa skin.' };
    if (p.shots < preco) return { erro: 'Faltam ' + (preco - p.shots) + ' SHOTS.' };
    p.shots -= preco; p.skins = (p.skins || []).concat([item]);
    p.equipadas = Object.assign({}, p.equipadas, { [w]: skin });
  });
}

async function equipar(uid, nome, skin, w) {
  w = w | 0;
  if (w < 0 || w > 4) return { erro: 'Arma invalida.' };
  skin = String(skin || '');
  return alterar(uid, nome, p => {
    if (skin && !(p.skins || []).includes(skin + '|' + w)) return { erro: 'Voce nao tem essa skin.' };
    p.equipadas = Object.assign({}, p.equipadas, { [w]: skin });
  });
}

// ------------------------------------------------------------------ fim de partida
// lista: [{ id (0 = bot), k, d, hs, big, obj }], vencedor: 0/1, -1 empate.
// jogadores: Map id -> { uid, nome, time, desde } (so contas logadas e conectadas no fim).
async function fimDePartida(lista, vencedor, jogadores) {
  const n = (v, max) => Math.max(0, Math.min(max, v | 0));
  const limpa = lista.slice(0, 20).map(e => ({ id: e.id | 0, k: n(e.k, 80), d: n(e.d, 200), hs: 0, big: n(e.big, 20), obj: n(e.obj, 20) }))
    .map((e, i) => Object.assign(e, { hs: Math.min(n(lista[i].hs, 80), e.k) }));
  const ordem = limpa.slice().sort((a, b) => (b.k - a.k) || (a.d - b.d));
  const res = [];
  for (const [id, j] of jogadores) {
    const e = limpa.find(x => x.id === id);
    if (!e) continue;
    const pos = ordem.indexOf(e);
    const combate = e.k * XP.abate + e.hs * XP.headshot + e.big * XP.grande;
    const obj = e.obj * XP.objetivo;
    const m = mult(pos, ordem.length);
    const venceu = vencedor === j.time, empate = vencedor === -1;
    let base = Math.round((combate + obj + XP.terminar) * m) + (venceu ? XP.vitoria : empate ? XP.empate : 0);
    const r = await alterar(j.uid, j.nome, p => {
      if (p.dia !== hoje()) { p.dia = hoje(); p.xp_hoje = 0; p.vitoria_hoje = false; }
      const linhas = [['ABATES x' + e.k, e.k * XP.abate]];
      if (e.hs) linhas.push(['TIROS NA CABECA x' + e.hs, e.hs * XP.headshot]);
      if (e.big) linhas.push(['MECA / DINOSSAURO x' + e.big, e.big * XP.grande]);
      if (e.obj) linhas.push(['OBJETIVOS x' + e.obj, obj]);
      linhas.push(['TERMINOU A PARTIDA', XP.terminar]);
      linhas.push([(pos + 1) + 'o LUGAR  x' + m.toFixed(2).replace('.', ','), Math.round((combate + obj + XP.terminar) * (m - 1))]);
      if (venceu) linhas.push(['VITORIA', XP.vitoria]); else if (empate) linhas.push(['EMPATE', XP.empate]);
      let xp = base;
      if (venceu && !p.vitoria_hoje) { xp += XP.primeiraVitoria; p.vitoria_hoje = true; linhas.push(['PRIMEIRA VITORIA DO DIA', XP.primeiraVitoria]); }
      if (p.xp_hoje >= TETO_DIA) { const corte = Math.round(xp * (1 - APOS_TETO)); xp -= corte; linhas.push(['TETO DIARIO (25%)', -corte]); }
      const antes = nivelDe(p.xp_total).nivel;
      p.xp_total += xp; p.xp_hoje += xp;
      const depois = nivelDe(p.xp_total).nivel;
      const shots = Math.max(5, Math.floor(xp / SHOTS_POR_XP)) + SHOTS_POR_NIVEL * (depois - antes);
      p.shots += shots; p.partidas++; if (venceu) p.vitorias++; p.abates += e.k;
      p.ultima = { xp, shots };
      return { premio: { t: 'reward', xp, shots, lines: linhas.map(l => l[0] + '|' + l[1]), lvlUp: depois - antes } };
    });
    res.push({ id, r });
  }
  return res;
}

// ------------------------------------------------------------------ ranking
async function ranking() {
  let lista;
  if (!usaSupabase()) lista = [...memoria.values()];
  else lista = await sb('perfis?select=nome,xp_total,partidas,vitorias,abates&order=xp_total.desc&limit=50');
  return lista.sort((a, b) => b.xp_total - a.xp_total).slice(0, 50).map(p => {
    const n = nivelDe(p.xp_total).nivel;
    return { nome: p.nome || '?', nivel: n, patente: patente(n), xp: p.xp_total, partidas: p.partidas || 0, vitorias: p.vitorias || 0, abates: p.abates || 0 };
  });
}

// ------------------------------------------------------------------ PIX (Mercado Pago)
// Fluxo: o jogo pede um pacote -> criamos a cobranca PIX e devolvemos o QR e o "copia e cola" ->
// o Mercado Pago avisa no webhook -> conferimos o pagamento na API deles -> creditamos uma vez so.
async function criarPix(uid, nome, email, pacoteId) {
  const pac = PACOTES.find(x => x.id === pacoteId);
  if (!pac) return { erro: 'Pacote invalido.' };
  if (!MP_TOKEN) return { erro: 'Pagamento por PIX ainda nao esta ativo.' };
  const ref = uid + '|' + pac.id + '|' + crypto.randomBytes(6).toString('hex');
  const res = await fetch('https://api.mercadopago.com/v1/payments', {
    method: 'POST',
    headers: { Authorization: 'Bearer ' + MP_TOKEN, 'content-type': 'application/json', 'X-Idempotency-Key': ref },
    body: JSON.stringify({
      transaction_amount: pac.valor, description: pac.shots + ' SHOTS - Fatal Shooter', payment_method_id: 'pix',
      payer: { email: email || 'jogador@fatalshooter.com.br' }, external_reference: ref,
      notification_url: PUBLIC_URL + '/pix/webhook',
      date_of_expiration: new Date(Date.now() + 30 * 60e3).toISOString().replace('Z', '-00:00'),
    }),
  });
  const j = await res.json().catch(() => ({}));
  if (!res.ok || !j.id) { console.error('pix criar', res.status, JSON.stringify(j).slice(0, 300)); return { erro: 'Nao foi possivel gerar o PIX agora.' }; }
  const td = (j.point_of_interaction || {}).transaction_data || {};
  const pag = { id: String(j.id), user_id: uid, pacote: pac.id, shots: pac.shots, valor: pac.valor, status: 'pendente', creditado: false };
  if (usaSupabase()) await sb('pagamentos', { method: 'POST', prefer: 'return=minimal', body: pag }); else pagMem.set(pag.id, pag);
  return { pix: { t: 'pix', pid: pag.id, code: td.qr_code || '', qr: td.qr_code_base64 || '', price: pac.valor.toFixed(2).replace('.', ','), shots: pac.shots } };
}

// Confere a assinatura do webhook (x-signature: ts=...,v1=...) quando MP_WEBHOOK_SECRET esta configurado.
function assinaturaOk(headers, dataId) {
  if (!MP_WEBHOOK_SECRET) return true;
  const sig = String(headers['x-signature'] || ''), reqId = String(headers['x-request-id'] || '');
  const parts = Object.fromEntries(sig.split(',').map(s => s.trim().split('=')));
  if (!parts.ts || !parts.v1) return false;
  const manifest = 'id:' + String(dataId).toLowerCase() + ';request-id:' + reqId + ';ts:' + parts.ts + ';';
  const h = crypto.createHmac('sha256', MP_WEBHOOK_SECRET).update(manifest).digest('hex');
  return h.length === parts.v1.length && crypto.timingSafeEqual(Buffer.from(h), Buffer.from(parts.v1));
}

// Webhook: devolve { uid, perfil } quando creditou agora (para avisar o jogador conectado).
async function webhookPix(headers, query, corpo) {
  if (!MP_TOKEN) return null;
  const id = String((corpo && corpo.data && corpo.data.id) || query['data.id'] || query.id || '');
  const tipo = (corpo && (corpo.type || corpo.topic)) || query.type || query.topic || '';
  if (!id || (tipo && tipo !== 'payment')) return null;
  if (!assinaturaOk(headers, id)) { console.error('pix webhook: assinatura invalida'); return null; }
  return conferirPagamento(id);
}

async function conferirPagamento(id) {
  const res = await fetch('https://api.mercadopago.com/v1/payments/' + encodeURIComponent(id), { headers: { Authorization: 'Bearer ' + MP_TOKEN } });
  if (!res.ok) return null;
  const pg = await res.json();
  let pag = usaSupabase() ? (await sb('pagamentos?id=eq.' + encodeURIComponent(id) + '&select=*'))[0] : pagMem.get(String(id));
  if (!pag) return null;
  if (pg.status !== 'approved') {
    if (usaSupabase()) await sb('pagamentos?id=eq.' + encodeURIComponent(id), { method: 'PATCH', prefer: 'return=minimal', body: { status: pg.status } });
    return null;
  }
  if (Math.abs(+pg.transaction_amount - +pag.valor) > 0.009) { console.error('pix: valor diferente', id); return null; }
  // Marca como creditado primeiro (so um pedido ganha essa corrida) e depois soma os SHOTS.
  if (usaSupabase()) {
    const r = await sb('pagamentos?id=eq.' + encodeURIComponent(id) + '&creditado=eq.false', { method: 'PATCH', body: { creditado: true, status: 'aprovado', atualizado: new Date().toISOString() } });
    if (!r.length) return null;
  } else { if (pag.creditado) return null; pag.creditado = true; }
  const r = await alterar(pag.user_id, null, p => { p.shots += pag.shots; });
  return r.perfil ? { uid: pag.user_id, perfil: r.perfil, shots: pag.shots } : null;
}

module.exports = { ativo, carregar, publico, comprar, equipar, fimDePartida, ranking, criarPix, webhookPix, conferirPagamento, DURACAO_MIN, PACOTES, nivelDe, patente };
