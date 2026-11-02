-- ITSResFix migration v17: store the web app's sign-in encryption keys in the database.
-- Needed on hosting without a persistent disk (Render free plan): without it every restart signs all staff out.

create table if not exists public.data_protection_key (
    id            bigint generated always as identity primary key,
    friendly_name text not null,
    xml           text not null,
    created_at    timestamptz not null default now()
);

-- Only the web server (service_role) may read or write it.
alter table public.data_protection_key enable row level security;
revoke all on table public.data_protection_key from anon, authenticated;
revoke all on sequence public.data_protection_key_id_seq from anon, authenticated;
