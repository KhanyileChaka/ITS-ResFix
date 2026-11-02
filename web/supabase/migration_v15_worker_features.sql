-- ITSResFix migration v15: worker features.
-- scheduled_visit_at: when the worker plans to attend the job (the student is told).
alter table public.complaint add column if not exists scheduled_visit_at timestamptz;
