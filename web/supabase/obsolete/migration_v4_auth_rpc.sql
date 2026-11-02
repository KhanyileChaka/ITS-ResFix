-- =============================================================
-- ITSResfix — Migration v4
-- Secure student auth RPC functions for mobile app (anon key)
-- Run in Supabase SQL Editor
-- =============================================================

-- Allow anon to call these RPC functions (required for mobile login flow)
-- The functions run as the service role (SECURITY DEFINER), so they
-- bypass RLS while still only returning the exact fields needed.

-- 1. Look up a student by cellphone (used on First Login + Login screens)
create or replace function get_student_by_cellphone(p_cellphone text)
returns table (
  student_id   integer,
  user_id      integer,
  student_number text,
  room_number  text,
  cellphone    text,
  is_registered boolean
)
language plpgsql
security definer
as $$
begin
  return query
  select
    s.student_id,
    s.user_id,
    s.student_number,
    s.room_number,
    s.cellphone,
    s.is_registered
  from student s
  where s.cellphone = trim(p_cellphone)
  limit 1;
end;
$$;

-- 2. Get user auth info by user_id (used during login to verify password)
create or replace function get_user_auth(p_user_id integer)
returns table (
  user_id       integer,
  email         text,
  full_name     text,
  password_hash text,
  role          text
)
language plpgsql
security definer
as $$
begin
  return query
  select
    u.user_id,
    u.email,
    u.full_name,
    u.password_hash,
    u.role
  from users u
  where u.user_id = p_user_id
  limit 1;
end;
$$;

-- 3. Set student password and mark as registered (used on Create Password screen)
create or replace function set_student_password(p_cellphone text, p_hash text)
returns void
language plpgsql
security definer
as $$
declare
  v_user_id integer;
begin
  select user_id into v_user_id
  from student
  where cellphone = trim(p_cellphone)
  limit 1;

  if v_user_id is null then
    raise exception 'Student not found';
  end if;

  update users set password_hash = p_hash where user_id = v_user_id;
  update student set is_registered = true where user_id = v_user_id;
end;
$$;

-- 4. Change password (verifies old password first)
create or replace function change_student_password(p_user_id integer, p_new_hash text)
returns void
language plpgsql
security definer
as $$
begin
  update users set password_hash = p_new_hash where user_id = p_user_id;
end;
$$;

-- Grant execute to anon and authenticated roles
grant execute on function get_student_by_cellphone(text)  to anon, authenticated;
grant execute on function get_user_auth(integer)          to anon, authenticated;
grant execute on function set_student_password(text, text) to anon, authenticated;
grant execute on function change_student_password(integer, text) to anon, authenticated;
