-- ITS ResFix v21: keep the sign-in "too many attempts" counters in the database.
-- Without this every restart (free hosting sleeps and wakes, and every deploy restarts) forgets who was locked out.
-- The key is a keyed hash of the phone / email / address being guarded, so no personal data is stored here.
-- Safe to re-run.
create table if not exists public.login_throttle (
    key_hash       text primary key,
    failures       integer     not null,
    first_failure  timestamptz not null,
    locked_until   timestamptz,
    updated_at     timestamptz not null default now()
);

create index if not exists login_throttle_first_idx on public.login_throttle (first_failure);

-- Only the web server (service_role) may read or write it.
alter table public.login_throttle enable row level security;
revoke all on table public.login_throttle from anon, authenticated;
