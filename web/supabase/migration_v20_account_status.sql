-- ITS ResFix v20: levels of removing people (suspend, remove, restore).
-- users.account_status: 'active' (can sign in), 'suspended' (blocked for now, can be restored), 'removed' (left; history kept, can be restored).
-- Safe to re-run.
alter table public.users add column if not exists account_status     text not null default 'active';
alter table public.users add column if not exists status_reason      text;
alter table public.users add column if not exists status_changed_at  timestamptz;
alter table public.users add column if not exists status_changed_by  text;

alter table public.users drop constraint if exists users_account_status_check;
alter table public.users add constraint users_account_status_check check (account_status in ('active', 'suspended', 'removed'));

-- New requests are only auto-assigned to a worker whose account is active.
create or replace function public.auto_assign_complaint()
returns trigger
language plpgsql
as $$
declare
    v_worker integer;
begin
    if new.assigned_to is null then
        select r.worker_id into v_worker
        from public.auto_assignment_rule r
        join public.internal_worker w on w.worker_id = r.worker_id
        join public.users u on u.user_id = w.user_id
        where r.category_id = new.category_id
          and u.account_status = 'active';

        if v_worker is not null then
            new.assigned_to   := v_worker;
            new.assigned_type := 'auto';
            new.assigned_at   := now();
            new.deadline      := now() + interval '48 hours';
            new.timer_status  := 'active';
        end if;
    end if;
    return new;
end;
$$;
