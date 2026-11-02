-- =============================================================
-- ITSResfix — Migration v6
-- Auto-assignment on complaint submission
-- Run in Supabase SQL Editor
-- =============================================================

-- Replace submit_student_complaint with auto-assignment logic.
-- When a matching auto_assignment_rule exists for the complaint's category,
-- the complaint is immediately assigned to that worker with a 48-hour deadline.

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
  v_complaint_id integer;
  v_worker_id    integer;
  v_deadline     timestamptz;
begin
  -- Check if there is an auto-assignment rule for this category
  select worker_id into v_worker_id
  from auto_assignment_rule
  where category_id = p_category_id
  limit 1;

  if v_worker_id is not null then
    -- Assign immediately with 48-hour deadline
    v_deadline := now() + interval '48 hours';

    insert into complaint (
      student_id, category_id, room_number, description, priority,
      status_id, timer_status, photo_url,
      assigned_to, assigned_type, assigned_at, deadline
    ) values (
      p_student_id, p_category_id, p_room_number, p_description, p_priority,
      2, 'active', p_photo_url,
      v_worker_id, 'auto', now(), v_deadline
    )
    returning complaint_id into v_complaint_id;
  else
    -- No rule: leave unassigned for manager to handle
    insert into complaint (
      student_id, category_id, room_number, description, priority,
      status_id, timer_status, photo_url
    ) values (
      p_student_id, p_category_id, p_room_number, p_description, p_priority,
      1, 'pending', p_photo_url
    )
    returning complaint_id into v_complaint_id;
  end if;

  return v_complaint_id;
end;
$$;

grant execute on function submit_student_complaint(integer, integer, text, text, text, text) to anon, authenticated;
