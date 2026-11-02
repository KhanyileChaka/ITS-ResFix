-- ITSResFix migration v14: staff password reset by SMS link.
-- users.cellphone lets managers (who have no worker profile) receive reset links; workers use internal_worker.phone.
alter table public.users add column if not exists cellphone text;

-- Only a hash of each reset token is stored; a token works once and expires after 30 minutes.
create table if not exists public.password_reset (
    id          bigint generated always as identity primary key,
    user_id     integer not null references public.users(user_id) on delete cascade,
    token_hash  text not null unique,
    expires_at  timestamptz not null,
    used_at     timestamptz,
    created_at  timestamptz not null default now()
);
create index if not exists password_reset_user_idx on public.password_reset (user_id);

alter table public.password_reset enable row level security;
revoke all on table public.password_reset from anon, authenticated;
revoke all on sequence public.password_reset_id_seq from anon, authenticated;
