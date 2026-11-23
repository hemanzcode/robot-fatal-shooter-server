// Fatal Shooter - amigos: pedidos, lista e convites.
// Guarda no Supabase (tabela amizades; so a chave de servico escreve). Sem a chave, usa memoria (teste local).
// Uma linha por par: de -> para. status 'pendente' (pedido) ou 'aceito' (amigos).
const SUPABASE_URL = (process.env.SUPABASE_URL || '').replace(/\/$/, '');
const SERVICE_KEY = process.env.SUPABASE_SERVICE_KEY || '';
const MAX_AMIGOS = 100;

const usaSupabase = () => !!(SUPABASE_URL && SERVICE_KEY);
const memoria = [];          // { de, para, status }
const nomesMem = new Map();  // uid -> { nome, nivel }

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
const valido = uid => /^[0-9a-f-]{36}$/i.test(uid) || /^guest:[\w \-\.]{1,24}$/.test(uid);
const enc = encodeURIComponent;

// Todas as ligacoes de uma conta.
async function ligacoes(uid) {
  if (!usaSupabase()) return memoria.filter(l => l.de === uid || l.para === uid);
  return sb('amizades?or=(de.eq.' + enc(uid) + ',para.eq.' + enc(uid) + ')&select=de,para,status&limit=300');
}
async function nomes(uids) {
  const out = new Map();
  if (!uids.length) return out;
  if (!usaSupabase()) { for (const u of uids) out.set(u, nomesMem.get(u) || { nome: u.replace('guest:', ''), nivel: 1 }); return out; }
  const r = await sb('perfis?user_id=in.(' + uids.map(enc).join(',') + ')&select=user_id,nome,nivel');
  for (const x of r) out.set(x.user_id, { nome: x.nome || 'JOGADOR', nivel: x.nivel || 1 });
  return out;
}

// { acc: Map uid->info, inc: Map, sent: Map }
async function carregar(uid) {
  const ls = await ligacoes(uid);
  const outros = [...new Set(ls.map(l => l.de === uid ? l.para : l.de))];
  const ns = await nomes(outros);
  const f = { acc: new Map(), inc: new Map(), sent: new Map() };
  for (const l of ls) {
    const o = l.de === uid ? l.para : l.de;
    const info = ns.get(o) || { nome: 'JOGADOR', nivel: 1 };
    if (l.status === 'aceito') f.acc.set(o, info);
    else if (l.para === uid) f.inc.set(o, info);
    else f.sent.set(o, info);
  }
  return f;
}

function lembrarNome(uid, nome, nivel) { if (!usaSupabase()) nomesMem.set(uid, { nome, nivel: nivel || 1 }); }

async function acharPorNome(nome) {
  nome = String(nome || '').toUpperCase();
  if (!usaSupabase()) { for (const [u, i] of nomesMem) if (i.nome === nome) return u; return null; }
  const r = await sb('perfis?nome=eq.' + enc(nome) + '&select=user_id&limit=1');
  return r.length ? r[0].user_id : null;
}

// Pede amizade. Se o outro ja tinha pedido, vira amizade direto.
async function pedir(uid, alvo) {
  if (!valido(alvo) || alvo === uid) return { erro: 'Jogador invalido.' };
  const ls = await ligacoes(uid);
  if (ls.filter(l => l.status === 'aceito').length >= MAX_AMIGOS) return { erro: 'Limite de ' + MAX_AMIGOS + ' amigos.' };
  const l = ls.find(x => (x.de === uid && x.para === alvo) || (x.de === alvo && x.para === uid));
  if (l && l.status === 'aceito') return { erro: 'Voces ja sao amigos.' };
  if (l && l.de === uid) return { erro: 'Pedido ja enviado. Aguarde a resposta.' };
  if (l) return aceitar(uid, alvo);
  if (!usaSupabase()) memoria.push({ de: uid, para: alvo, status: 'pendente' });
  else await sb('amizades', { method: 'POST', prefer: 'resolution=ignore-duplicates,return=minimal', body: { de: uid, para: alvo, status: 'pendente' } });
  return { ok: 'pedido' };
}

async function aceitar(uid, de) {
  if (!valido(de)) return { erro: 'Jogador invalido.' };
  if (!usaSupabase()) {
    const l = memoria.find(x => x.de === de && x.para === uid);
    if (!l) return { erro: 'Pedido nao encontrado.' };
    l.status = 'aceito'; return { ok: 'amigos' };
  }
  const r = await sb('amizades?de=eq.' + enc(de) + '&para=eq.' + enc(uid), { method: 'PATCH', body: { status: 'aceito', atualizado: new Date().toISOString() } });
  return r && r.length ? { ok: 'amigos' } : { erro: 'Pedido nao encontrado.' };
}

// Recusa, cancela ou desfaz a amizade (apaga a ligacao nos dois sentidos).
async function remover(uid, outro) {
  if (!valido(outro)) return { erro: 'Jogador invalido.' };
  if (!usaSupabase()) {
    for (let i = memoria.length - 1; i >= 0; i--) {
      const l = memoria[i];
      if ((l.de === uid && l.para === outro) || (l.de === outro && l.para === uid)) memoria.splice(i, 1);
    }
    return { ok: 'removido' };
  }
  await sb('amizades?or=(and(de.eq.' + enc(uid) + ',para.eq.' + enc(outro) + '),and(de.eq.' + enc(outro) + ',para.eq.' + enc(uid) + '))', { method: 'DELETE', prefer: 'return=minimal' });
  return { ok: 'removido' };
}

module.exports = { carregar, pedir, aceitar, remover, acharPorNome, lembrarNome, ativo: () => usaSupabase() || process.env.ALLOW_GUESTS === '1' };
