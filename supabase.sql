-- Fatal Shooter: progresso da conta (XP, nivel, SHOTS, skins) e pagamentos PIX.
-- Rodar uma vez no SQL Editor do Supabase. Pode rodar de novo sem problema (if not exists).
-- Escrita: so o servidor (chave de servico, que ignora RLS). O jogo/navegador so pode LER o proprio perfil.

create table if not exists public.perfis (
  user_id      uuid primary key references auth.users(id) on delete cascade,
  nome         text,
  xp_total     bigint  not null default 0,
  nivel        int     not null default 1,
  shots        bigint  not null default 0 check (shots >= 0),
  xp_hoje      int     not null default 0,
  dia          date,
  vitoria_hoje boolean not null default false,
  ultima       jsonb,
  skins        text[]  not null default '{}',
  equipadas    jsonb   not null default '{}'::jsonb,
  partidas     int     not null default 0,
  vitorias     int     not null default 0,
  abates       int     not null default 0,
  versao       int     not null default 0,
  criado       timestamptz not null default now(),
  atualizado   timestamptz not null default now()
);
create index if not exists perfis_xp on public.perfis (xp_total desc);

alter table public.perfis enable row level security;
drop policy if exists "ler o proprio perfil" on public.perfis;
create policy "ler o proprio perfil" on public.perfis for select using (auth.uid() = user_id);

create table if not exists public.pagamentos (
  id         text primary key,                 -- id do pagamento no Mercado Pago
  user_id    uuid not null references auth.users(id) on delete cascade,
  pacote     text not null,
  shots      int  not null,
  valor      numeric(10,2) not null,
  status     text not null default 'pendente',
  creditado  boolean not null default false,
  criado     timestamptz not null default now(),
  atualizado timestamptz not null default now()
);
create index if not exists pagamentos_user on public.pagamentos (user_id, criado desc);

alter table public.pagamentos enable row level security;
drop policy if exists "ver os proprios pagamentos" on public.pagamentos;
create policy "ver os proprios pagamentos" on public.pagamentos for select using (auth.uid() = user_id);

-- Amigos: uma linha por par (quem pediu -> quem recebeu). status: 'pendente' ou 'aceito'.
create table if not exists public.amizades (
  de         uuid not null references auth.users(id) on delete cascade,
  para       uuid not null references auth.users(id) on delete cascade,
  status     text not null default 'pendente' check (status in ('pendente', 'aceito')),
  criado     timestamptz not null default now(),
  atualizado timestamptz not null default now(),
  primary key (de, para),
  check (de <> para)
);
create index if not exists amizades_para on public.amizades (para);
create index if not exists perfis_nome on public.perfis (nome);

alter table public.amizades enable row level security;
drop policy if exists "ver as proprias amizades" on public.amizades;
create policy "ver as proprias amizades" on public.amizades for select using (auth.uid() = de or auth.uid() = para);
