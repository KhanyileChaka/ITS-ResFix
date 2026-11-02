-- ITSResFix migration v13: force a new password on first login (set for workers created with a one-time password).
alter table public.users add column if not exists must_change_password boolean not null default false;
