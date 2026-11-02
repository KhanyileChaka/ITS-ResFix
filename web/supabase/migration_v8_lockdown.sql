-- ITSResFix migration v8: lock the database down.
-- After this, the public (anon) key that ships in the student app can do NOTHING directly:
-- all student access goes through the web server's /api/student endpoints (service_role key, server side only).
-- Run in the Supabase SQL Editor AFTER the new student app + web server are deployed.

-- 1. Remove every row-level-security policy in the public schema (the old "using (true)" read policies).
do $$
declare r record;
begin
  for r in select schemaname, tablename, policyname from pg_policies where schemaname = 'public' loop
    execute format('drop policy %I on %I.%I', r.policyname, r.schemaname, r.tablename);
  end loop;
end $$;

-- 2. Make sure RLS is ON for every table (no policy = no access for anon/authenticated; service_role bypasses RLS).
do $$
declare r record;
begin
  for r in select tablename from pg_tables where schemaname = 'public' loop
    execute format('alter table public.%I enable row level security', r.tablename);
  end loop;
end $$;

-- 3. Drop the old mobile-app RPC functions (they exposed password hashes and let anyone set passwords).
drop function if exists public.get_student_by_cellphone(text);
drop function if exists public.get_user_auth(integer);
drop function if exists public.set_student_password(text, text);
drop function if exists public.change_student_password(integer, text);
drop function if exists public.submit_student_complaint(integer, integer, text, text, text, text);
drop function if exists public.submit_complaint_rating(integer, smallint, boolean, text);
drop function if exists public.mark_student_notifications_read(integer);
drop function if exists public.generate_student_otp(text);
drop function if exists public.verify_student_otp(text, text);

-- 4. Nobody but the server may call remaining functions or touch tables/sequences.
revoke execute on all functions in schema public from public, anon, authenticated;
revoke all on all tables    in schema public from anon, authenticated;
revoke all on all sequences in schema public from anon, authenticated;
grant  execute on all functions in schema public to service_role;
grant  all on all tables    in schema public to service_role;
grant  all on all sequences in schema public to service_role;

alter default privileges in schema public revoke execute on functions from public, anon, authenticated;
alter default privileges in schema public revoke all on tables    from anon, authenticated;
alter default privileges in schema public revoke all on sequences from anon, authenticated;

-- 5. Photo storage: uploads now go through the server. Remove any anon/authenticated storage policies.
--    (The bucket stays public-read so photo links keep working; nobody can upload or list with the public key.)
do $$
declare r record;
begin
  for r in select policyname from pg_policies where schemaname = 'storage' and tablename = 'objects' loop
    execute format('drop policy %I on storage.objects', r.policyname);
  end loop;
end $$;

-- 6. Purge leftover one-time codes (old rows stored the code in plain text).
delete from public.student_otp;
