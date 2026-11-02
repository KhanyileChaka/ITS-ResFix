-- ITSResFix migration v11: appeals, follow-up messages, SMS opt-in.

-- 1. Appeals on rejected requests
alter table public.complaint add column if not exists appeal_status text
    check (appeal_status in ('pending', 'approved', 'declined'));
alter table public.complaint add column if not exists appeal_reason text;
alter table public.complaint add column if not exists appeal_response text;
alter table public.complaint add column if not exists appealed_at timestamptz;

-- 2. Conversation between the student and staff on a request
create table if not exists public.complaint_message (
    id           bigint generated always as identity primary key,
    complaint_id integer not null references public.complaint(complaint_id) on delete cascade,
    sender       text not null check (sender in ('student', 'staff')),
    sender_name  text,
    body         text not null,
    photo_url    text,
    created_at   timestamptz not null default now()
);
create index if not exists complaint_message_complaint_idx on public.complaint_message (complaint_id, created_at);

-- Only the web server (service_role) may touch it; the public key gets nothing.
alter table public.complaint_message enable row level security;
revoke all on table public.complaint_message from anon, authenticated;
revoke all on sequence public.complaint_message_id_seq from anon, authenticated;

-- 3. Students choose whether to get SMS alerts (off by default: SMS costs money)
alter table public.student add column if not exists sms_alerts boolean not null default false;
