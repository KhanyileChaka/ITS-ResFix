-- ============================================================
-- ITS ResFix — Migration v2: External Company table
-- Run in Supabase SQL Editor AFTER schema.sql
-- ============================================================

create table if not exists public.external_company (
    id            integer generated always as identity primary key,
    company_name  text not null default '',
    api_endpoint  text,
    contact_email text not null default '',
    contact_phone text not null default '',
    is_active     boolean not null default true
);

alter table public.external_company enable row level security;

-- Seed a placeholder row
insert into public.external_company (company_name, contact_email, contact_phone)
values ('[Company Name]', 'info@company.co.za', '061 123 4567')
on conflict do nothing;
