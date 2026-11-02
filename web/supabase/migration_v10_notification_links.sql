-- ITSResFix migration v10: link each student notification to its complaint so the app can open the details.

alter table public.student_notification
  add column if not exists complaint_id integer references public.complaint(complaint_id) on delete cascade;

-- Backfill existing notifications from the "C0xx" code in their text (only where that complaint still exists).
update public.student_notification n
set complaint_id = c.complaint_id
from public.complaint c
where n.complaint_id is null
  and n.message ~ 'C0*[0-9]+'
  and c.complaint_id = (regexp_match(n.message, 'C0*([0-9]+)'))[1]::int;

-- Status-change notifications now carry the complaint id. Rejected (4) and re-accepted (1) changes are skipped
-- because the server already writes a specific message for those (this removes the duplicate "status updated" notice).
create or replace function public.notify_student_on_status_change()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  v_msg text;
begin
  if old.status_id is distinct from new.status_id and new.status_id in (2, 3) then
    v_msg := case new.status_id
      when 2 then 'Your complaint C' || lpad(new.complaint_id::text, 3, '0') || ' is now In Progress.'
      else        'Your complaint C' || lpad(new.complaint_id::text, 3, '0') || ' has been Resolved. Please rate the repair.'
    end;
    insert into public.student_notification (student_id, complaint_id, message)
    values (new.student_id, new.complaint_id, v_msg);
  end if;
  return new;
end;
$$;
