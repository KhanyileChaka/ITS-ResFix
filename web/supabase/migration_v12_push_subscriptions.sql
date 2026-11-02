-- ITSResFix migration v12: browser push subscriptions (self-hosted Web Push, no Firebase).
-- One row per browser/device a student turned notifications on for.

create table if not exists public.push_subscription (
    id           bigint generated always as identity primary key,
    student_id   integer not null references public.student(student_id) on delete cascade,
    endpoint     text not null unique,
    p256dh       text not null,
    auth         text not null,
    user_agent   text,
    created_at   timestamptz not null default now()
);
create index if not exists push_subscription_student_idx on public.push_subscription (student_id);

-- Only the web server (service_role) may read or write it.
alter table public.push_subscription enable row level security;
revoke all on table public.push_subscription from anon, authenticated;
revoke all on sequence public.push_subscription_id_seq from anon, authenticated;
