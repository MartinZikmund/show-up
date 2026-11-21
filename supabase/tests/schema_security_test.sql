-- Guards that catch a future migration weakening the security model.
begin;
create extension if not exists pgtap with schema extensions;

select plan(10);

select tables_are('public', array[
  'enrollments', 'completions', 'schedule_shifts', 'bonus_workouts', 'notes', 'metric_entries',
  'reminders', 'custom_programs', 'synced_settings',
  'catalog_versions', 'catalog_workouts', 'catalog_programs'
], 'public has exactly the synced and catalog tables');

select is_empty($$
  select c.relname from pg_class c
  where c.relnamespace = 'public'::regnamespace and c.relkind in ('r', 'p') and not c.relrowsecurity
$$, 'RLS is enabled on every public table');

select is_empty($$
  select tablename || ': ' || policyname from pg_policies
  where schemaname = 'public' and cmd in ('DELETE', 'ALL')
$$, 'no DELETE (or ALL) policies: rows are tombstoned, never deleted');

select is_empty($$
  select c.relname from pg_class c
  where c.relnamespace = 'public'::regnamespace and c.relkind in ('r', 'p')
    and c.relname not like 'catalog\_%'
    and (has_table_privilege('anon', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
      or has_table_privilege('service_role', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE'))
$$, 'anon and service_role have no privileges on user tables');

select is_empty($$
  select c.relname from pg_class c
  where c.relnamespace = 'public'::regnamespace and c.relkind in ('r', 'p')
    and has_table_privilege('authenticated', c.oid, 'DELETE, TRUNCATE')
$$, 'authenticated cannot DELETE or TRUNCATE any public table');

select is_empty($$
  select p.proname from pg_proc p
  where p.pronamespace = 'public'::regnamespace and p.prosecdef
$$, 'no SECURITY DEFINER functions in public');

select is(
  (select count(distinct t.tgrelid)::int from pg_trigger t
   where t.tgname = 'set_server_updated_at' and not t.tgisinternal),
  9, 'every user table has the server_updated_at trigger');

select is_empty($$
  select pol.tablename || ': ' || pol.policyname from pg_policies pol
  where pol.schemaname = 'public' and pol.tablename not like 'catalog\_%'
    and pol.roles <> array['authenticated']::name[]
$$, 'user table policies are TO authenticated only');

select is_empty($$
  select pol.tablename || ': ' || pol.policyname from pg_policies pol
  where pol.schemaname = 'public' and pol.tablename not like 'catalog\_%'
    and pol.cmd in ('SELECT', 'UPDATE') and coalesce(pol.qual, '') not like '%auth.uid()%'
$$, 'user table SELECT and UPDATE policies check ownership in USING');

select is_empty($$
  select pol.tablename || ': ' || pol.policyname from pg_policies pol
  where pol.schemaname = 'public' and pol.tablename not like 'catalog\_%'
    and pol.cmd in ('INSERT', 'UPDATE') and coalesce(pol.with_check, '') not like '%auth.uid()%'
$$, 'user table INSERT and UPDATE policies check ownership in WITH CHECK');

select * from finish();
rollback;
