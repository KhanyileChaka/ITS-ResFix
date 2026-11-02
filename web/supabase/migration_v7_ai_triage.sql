-- ITSResFix migration v7: AI complaint triage
-- Run in Supabase SQL Editor.

insert into public.complaint_status (status_id, name) values (4, 'Rejected')
on conflict (status_id) do nothing;

alter table public.complaint add column if not exists ai_checked boolean not null default false;
alter table public.complaint add column if not exists ai_note text;
alter table public.complaint add column if not exists original_category_id integer references public.category(category_id);
