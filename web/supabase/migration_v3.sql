-- =============================================================
-- ITSResfix — Migration v3
-- Student Mobile App: OTP, Ratings, Notifications
-- Run this in Supabase SQL Editor (Dashboard → SQL Editor → New query)
-- =============================================================

-- 1. Student OTP table
-- -----------------------------------------------------------
create table if not exists student_otp (
  id          bigint generated always as identity primary key,
  cellphone   text not null,
  otp_code    text not null,
  expires_at  timestamptz not null,
  used        boolean not null default false,
  created_at  timestamptz not null default now()
);

create index if not exists student_otp_cellphone_idx on student_otp (cellphone);

-- 2. Complaint Rating table
-- -----------------------------------------------------------
create table if not exists complaint_rating (
  id             bigint generated always as identity primary key,
  complaint_id   integer not null references complaint(complaint_id) on delete cascade,
  rating         smallint not null check (rating between 1 and 5),
  issue_resolved boolean not null default true,
  comment        text,
  created_at     timestamptz not null default now(),
  unique (complaint_id)
);

-- 3. Student Notification table
-- -----------------------------------------------------------
create table if not exists student_notification (
  id          bigint generated always as identity primary key,
  student_id  integer not null references student(student_id) on delete cascade,
  message     text not null,
  is_read     boolean not null default false,
  created_at  timestamptz not null default now()
);

create index if not exists student_notification_student_idx on student_notification (student_id, created_at desc);

-- =============================================================
-- RPC Functions
-- =============================================================

-- 4. generate_student_otp
--    Called by the mobile app when a student requests an OTP.
--    Generates a 6-digit code valid for 5 minutes.
-- -----------------------------------------------------------
create or replace function generate_student_otp(p_cellphone text)
returns void
language plpgsql
security definer
as $$
declare
  v_code text;
begin
  -- Invalidate any previous unused OTPs for this number
  update student_otp set used = true
  where cellphone = p_cellphone and used = false;

  -- Generate a random 6-digit code
  v_code := lpad(floor(random() * 1000000)::text, 6, '0');

  insert into student_otp (cellphone, otp_code, expires_at)
  values (p_cellphone, v_code, now() + interval '5 minutes');
end;
$$;

-- 5. verify_student_otp
--    Returns true if the OTP is valid (correct, unused, not expired).
--    Marks it as used on success.
-- -----------------------------------------------------------
create or replace function verify_student_otp(p_cellphone text, p_code text)
returns boolean
language plpgsql
security definer
as $$
declare
  v_id bigint;
begin
  select id into v_id
  from student_otp
  where cellphone = p_cellphone
    and otp_code  = p_code
    and used      = false
    and expires_at > now()
  order by created_at desc
  limit 1;

  if v_id is null then
    return false;
  end if;

  update student_otp set used = true where id = v_id;
  return true;
end;
$$;

-- 6. Helper: notify student when their complaint status changes
--    Trigger function — fire from complaint UPDATE trigger if desired.
-- -----------------------------------------------------------
create or replace function notify_student_on_status_change()
returns trigger
language plpgsql
security definer
as $$
declare
  v_student_id integer;
  v_msg text;
begin
  if old.status_id is distinct from new.status_id then
    v_student_id := new.student_id;
    v_msg := case new.status_id
      when 2 then 'Your complaint C' || lpad(new.complaint_id::text, 3, '0') || ' is now In Progress.'
      when 3 then 'Your complaint C' || lpad(new.complaint_id::text, 3, '0') || ' has been Resolved. Please rate the repair.'
      else         'Your complaint C' || lpad(new.complaint_id::text, 3, '0') || ' status has been updated.'
    end;

    insert into student_notification (student_id, message)
    values (v_student_id, v_msg);
  end if;
  return new;
end;
$$;

-- Attach trigger to complaint table
drop trigger if exists trg_complaint_status_notify on complaint;
create trigger trg_complaint_status_notify
  after update on complaint
  for each row execute function notify_student_on_status_change();
