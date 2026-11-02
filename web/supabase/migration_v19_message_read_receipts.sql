-- ITS ResFix v19: "seen" tracking for the conversation between staff and the student.
-- read_at is set when the other side opens the conversation. Safe to re-run.
alter table public.complaint_message add column if not exists read_at timestamptz;
