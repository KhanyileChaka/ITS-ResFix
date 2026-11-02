Database scripts (run in the Supabase SQL Editor, in this order)
================================================================
1. schema.sql                          base tables, seed data, auto-assignment trigger
2. migration_v2.sql                    external company table
3. migration_v3.sql                    student_otp, complaint_rating, student_notification tables
4. migration_v7_ai_triage.sql          Rejected status + AI review columns
5. migration_v8_lockdown.sql           removes all public (anon) access; only the web server can read/write
6. migration_v9_photo_review.sql       photo_note column
7. migration_v10_notification_links.sql  notification -> complaint link, status trigger fix
8. migration_v11_appeals_messages_sms.sql  appeals, complaint_message table, student SMS opt-in
9. migration_v12_push_subscriptions.sql  browser push subscriptions (self-hosted Web Push)
10. migration_v13_must_change_password.sql  force a password change on first login
11. migration_v14_password_reset.sql  staff password reset by SMS link (reset tokens, users.cellphone)
12. migration_v15_worker_features.sql  complaint.scheduled_visit_at (planned visit time)
13. migration_v16_external_assignment.sql  allow assigned_type = 'external' (escalation to the external company)
14. migration_v17_data_protection_keys.sql  sign-in encryption keys stored in the database (needed on hosting with no disk)
15. migration_v18_student_contact_email.sql  optional contact email for students (invites)
16. migration_v19_message_read_receipts.sql  "seen" tracking on conversation messages
17. migration_v20_account_status.sql  suspend / remove / restore people (users.account_status) and auto-assign only to active workers
18. migration_v21_login_throttle.sql  sign-in lockout counters kept in the database (survive restarts)

obsolete/  superseded scripts that must NOT be run (see the README inside).
The student app never talks to the database; it only calls the web server's /api/student endpoints.
