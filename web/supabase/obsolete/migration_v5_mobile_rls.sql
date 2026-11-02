-- =============================================================
-- ITSResfix — Migration v5
-- RLS policies + secure RPC functions for mobile app (anon key)
-- Run in Supabase SQL Editor
-- =============================================================

-- ── READ policies for lookup / reference tables ──────────────

-- category (read-only reference data)
alter table category enable row level security;
drop policy if exists "anon can read categories" on category;
create policy "anon can read categories" on category
  for select to anon, authenticated using (true);

-- complaint_status (read-only reference data)
alter table complaint_status enable row level security;
drop policy if exists "anon can read statuses" on complaint_status;
create policy "anon can read statuses" on complaint_status
  for select to anon, authenticated using (true);

-- internal_worker (needed to show worker name on complaints)
alter table internal_worker enable row level security;
drop policy if exists "anon can read workers" on internal_worker;
create policy "anon can read workers" on internal_worker
  for select to anon, authenticated using (true);

-- users (needed to show worker full name)
alter table users enable row level security;
drop policy if exists "anon can read users" on users;
create policy "anon can read users" on users
  for select to anon, authenticated using (true);

-- student (needed to read own profile)
alter table student enable row level security;
drop policy if exists "anon can read students" on student;
create policy "anon can read students" on student
  for select to anon, authenticated using (true);

-- complaint (student reads own complaints)
alter table complaint enable row level security;
drop policy if exists "anon can read complaints" on complaint;
create policy "anon can read complaints" on complaint
  for select to anon, authenticated using (true);

-- complaint_rating (student reads own ratings)
alter table complaint_rating enable row level security;
drop policy if exists "anon can read ratings" on complaint_rating;
create policy "anon can read ratings" on complaint_rating
  for select to anon, authenticated using (true);

-- student_notification (student reads own notifications)
alter table student_notification enable row level security;
drop policy if exists "anon can read notifications" on student_notification;
create policy "anon can read notifications" on student_notification
  for select to anon, authenticated using (true);

-- ── WRITE via SECURITY DEFINER RPCs ──────────────────────────

-- 1. Submit a new complaint
create or replace function submit_student_complaint(
  p_student_id  integer,
  p_category_id integer,
  p_room_number text,
  p_description text,
  p_priority    text,
  p_photo_url   text default null
)
returns integer
language plpgsql
security definer
as $$
declare
  v_id integer;
begin
  insert into complaint (student_id, category_id, room_number, description, priority, status_id, timer_status, photo_url)
  values (p_student_id, p_category_id, p_room_number, p_description, p_priority, 1, 'pending', p_photo_url)
  returning complaint_id into v_id;
  return v_id;
end;
$$;

-- 2. Submit / update a complaint rating
create or replace function submit_complaint_rating(
  p_complaint_id   integer,
  p_rating         smallint,
  p_issue_resolved boolean,
  p_comment        text default null
)
returns void
language plpgsql
security definer
as $$
begin
  insert into complaint_rating (complaint_id, rating, issue_resolved, comment)
  values (p_complaint_id, p_rating, p_issue_resolved, p_comment)
  on conflict (complaint_id) do update
    set rating         = excluded.rating,
        issue_resolved = excluded.issue_resolved,
        comment        = excluded.comment;
end;
$$;

-- 3. Mark all notifications read for a student
create or replace function mark_student_notifications_read(p_student_id integer)
returns void
language plpgsql
security definer
as $$
begin
  update student_notification
  set is_read = true
  where student_id = p_student_id;
end;
$$;

-- Grant execute to anon
grant execute on function submit_student_complaint(integer, integer, text, text, text, text) to anon, authenticated;
grant execute on function submit_complaint_rating(integer, smallint, boolean, text)           to anon, authenticated;
grant execute on function mark_student_notifications_read(integer)                            to anon, authenticated;
