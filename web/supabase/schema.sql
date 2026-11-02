-- ============================================================
-- ITS ResFix â€” shared Supabase schema (Staff web app + Student Flutter app)
-- Run in the Supabase SQL Editor. Safe to re-run (IF NOT EXISTS / ON CONFLICT).
-- ============================================================

create extension if not exists pgcrypto with schema extensions;

-- ---------- Users (the "User" entity; "user" is reserved in PostgreSQL) ----------
create table if not exists public.users (
    user_id       integer generated always as identity primary key,
    email         text not null unique,
    password_hash text not null,                          -- bcrypt
    role          text not null check (role in ('student', 'manager', 'worker')),
    full_name     text not null default '',
    created_at    timestamptz not null default now()
);

create table if not exists public.internal_worker (
    worker_id      integer generated always as identity primary key,
    user_id        integer not null unique references public.users(user_id) on delete cascade,
    specialisation text,
    is_available   boolean not null default true,
    phone          text,
    assigned_accommodation text
);

-- For databases created from an earlier version of this script.
alter table public.internal_worker add column if not exists phone text;
alter table public.internal_worker add column if not exists assigned_accommodation text;

create table if not exists public.student (
    student_id     integer generated always as identity primary key,
    user_id        integer not null unique references public.users(user_id) on delete cascade,
    student_number text not null unique,
    room_number    text not null,
    cellphone      text not null,
    is_registered  boolean not null default false         -- true once activated in the mobile app
);

-- ---------- Lookups ----------
create table if not exists public.category (
    category_id integer generated always as identity primary key,
    name        text not null unique
);

create table if not exists public.complaint_status (
    status_id integer primary key,
    name      text not null unique
);

insert into public.complaint_status (status_id, name) values
    (1, 'Submitted'), (2, 'In Progress'), (3, 'Resolved')
on conflict (status_id) do nothing;

insert into public.category (name) values
    ('Plumbing'), ('Electrical'), ('Furniture'), ('Doors & Locks'),
    ('Windows'), ('Pest Control'), ('Internet / Wi-Fi'), ('Other')
on conflict (name) do nothing;

-- ---------- Complaints ----------
create table if not exists public.complaint (
    complaint_id  integer generated always as identity primary key,
    student_id    integer not null references public.student(student_id) on delete cascade,
    category_id   integer not null references public.category(category_id),
    room_number   text not null,
    description   text not null,
    status_id     integer not null default 1 references public.complaint_status(status_id),
    priority      text not null default 'Normal' check (priority in ('Normal', 'Emergency')),
    assigned_to   integer references public.internal_worker(worker_id) on delete set null,
    assigned_type text check (assigned_type in ('auto', 'manual', 'external')),
    assigned_at   timestamptz,
    deadline      timestamptz,
    timer_status  text not null default 'pending'
                  check (timer_status in ('pending', 'active', 'expired', 'completed')),
    photo_url     text,                                    -- public URL in the complaint-photos bucket
    worker_notes  text,
    resolved_at   timestamptz,
    created_at    timestamptz not null default now()
);

create index if not exists complaint_assigned_to_idx on public.complaint(assigned_to);
create index if not exists complaint_deadline_idx on public.complaint(deadline);

-- ---------- Auto-assignment rules (one worker per category) ----------
create table if not exists public.auto_assignment_rule (
    rule_id     integer generated always as identity primary key,
    category_id integer not null unique references public.category(category_id) on delete cascade,
    worker_id   integer not null references public.internal_worker(worker_id) on delete cascade
);

-- ---------- Auto-assign on insert (applies to complaints from the Flutter app) ----------
create or replace function public.auto_assign_complaint()
returns trigger
language plpgsql
as $$
declare
    v_worker integer;
begin
    if new.assigned_to is null then
        select worker_id into v_worker
        from public.auto_assignment_rule
        where category_id = new.category_id;

        if v_worker is not null then
            new.assigned_to   := v_worker;
            new.assigned_type := 'auto';
            new.assigned_at   := now();
            new.deadline      := now() + interval '48 hours';
            new.timer_status  := 'active';
        end if;
    end if;
    return new;
end;
$$;

drop trigger if exists complaint_auto_assign on public.complaint;
create trigger complaint_auto_assign
    before insert on public.complaint
    for each row execute function public.auto_assign_complaint();

-- ---------- Expire overdue complaints (called by the staff app; can also run via pg_cron) ----------
create or replace function public.mark_expired_complaints()
returns integer
language sql
as $$
    with updated as (
        update public.complaint
        set timer_status = 'expired'
        where timer_status = 'active'
          and status_id <> 3
          and deadline < now()
        returning 1
    )
    select count(*)::integer from updated;
$$;

-- ---------- Storage bucket for complaint photos ----------
insert into storage.buckets (id, name, public)
values ('complaint-photos', 'complaint-photos', true)
on conflict (id) do nothing;

-- ---------- Row Level Security ----------
-- The staff web app uses the service_role key, which bypasses RLS.
-- With RLS enabled and no policies, the anon key cannot read password hashes or other students' data.
-- IMPORTANT for the Flutter team: the student app needs its own policies or RPC functions to access these tables.
alter table public.users                enable row level security;
alter table public.internal_worker      enable row level security;
alter table public.student              enable row level security;
alter table public.complaint            enable row level security;
alter table public.auto_assignment_rule enable row level security;
alter table public.category             enable row level security;
alter table public.complaint_status     enable row level security;

-- Lookups are harmless to read.
drop policy if exists "Anyone can read categories" on public.category;
create policy "Anyone can read categories" on public.category for select using (true);
drop policy if exists "Anyone can read statuses" on public.complaint_status;
create policy "Anyone can read statuses" on public.complaint_status for select using (true);

-- ============================================================
-- FIRST MANAGER
-- No default accounts are created. After running this script, create your first manager once
-- (choose your own email, name, phone and a strong password), then add workers and students from the manager portal:
--
--   insert into public.users (email, password_hash, role, full_name, cellphone)
--   values ('you@example.com', extensions.crypt('a-strong-password', extensions.gen_salt('bf', 11)), 'manager', 'Your Name', '0710000000');
-- ============================================================
