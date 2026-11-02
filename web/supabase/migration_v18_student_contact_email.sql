-- ITSResFix migration v18: an optional contact email for each student.
-- Students sign in with their cellphone number, so this is only used to email them (for example the invite to get started).
alter table public.student add column if not exists contact_email text;
