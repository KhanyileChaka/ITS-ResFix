-- ITSResFix migration v9: AI photo review. Stores why a student's photo was removed.
alter table public.complaint add column if not exists photo_note text;
