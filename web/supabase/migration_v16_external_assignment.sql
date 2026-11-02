-- ITSResFix migration v16: allow 'external' in complaint.assigned_type.
-- Escalating a complaint to the external company stores assigned_type = 'external', which the original check rejected.
alter table public.complaint drop constraint if exists complaint_assigned_type_check;
alter table public.complaint add constraint complaint_assigned_type_check check (assigned_type in ('auto', 'manual', 'external'));
