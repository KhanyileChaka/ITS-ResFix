# ITS ResFix — Staff Web App

ASP.NET Core MVC (.NET 8) portal for Residence Managers and Internal Workers. It shares a Supabase database with the Flutter student app.

## Setup

1. In the Supabase SQL Editor, run `supabase/schema.sql`. It creates the tables, the auto-assign trigger and the expiry function.
2. Store the Supabase credentials as user secrets (never commit the service_role key):

   ```
   dotnet user-secrets set "Supabase:Url" "https://<project-ref>.supabase.co"
   dotnet user-secrets set "Supabase:ServiceRoleKey" "<service_role key>"
   ```
3. `dotnet run`, then open https://localhost:7180

There are no default logins. Create the first manager with the SQL shown at the end of `supabase/schema.sql`, then add workers and students from the manager portal.

## How it works

- **Login** (`AccountController`): one form for all staff. It verifies the bcrypt `password_hash` in `users`, refuses `student`, and redirects `manager` to `/Manager/Dashboard` and `worker` to `/Worker/Dashboard`. A worker's `worker_id` is stored in the auth cookie.
- **48-hour timer**: the `complaint_auto_assign` trigger assigns new complaints using `auto_assignment_rule` and sets `deadline = now() + 48h`. When a manager reassigns an expired complaint, a new 48-hour window starts.
- **Students added by the manager** get `users.email = <student_number>@<Students:EmailDomain>` and an unusable random password. They activate their account with First Login (an SMS code) in the mobile app, which sets their own password and `student.is_registered = true`.

## Schema notes vs. the original spec

- The `User` table is named `users`, because `user` is reserved in PostgreSQL.
- Columns added: `users.full_name`, plus `complaint.photo_url`, `worker_notes`, `resolved_at` and `created_at`.
- Lookup tables added: `category` and `complaint_status` (1 Submitted, 2 In Progress, 3 Resolved).
- RLS is enabled on every table. The Flutter app needs its own policies or RPCs.
