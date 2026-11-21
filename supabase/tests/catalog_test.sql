-- The published catalog: anyone reads, only the service role (publish job) writes.
begin;
create extension if not exists pgtap with schema extensions;

select plan(11);

insert into public.catalog_versions (version) values (1);
insert into public.catalog_workouts (id, json, version) values ('plyo-cardio-circuit', '{}', 1);
insert into public.catalog_programs (id, revision, json, version) values ('insanity', 1, '{}', 1);

set local role anon;
select set_config('request.jwt.claims', json_build_object('role', 'anon')::text, true);

select is((select count(*)::int from public.catalog_versions), 1, 'anon can read catalog_versions');
select is((select count(*)::int from public.catalog_workouts), 1, 'anon can read catalog_workouts');
select is((select count(*)::int from public.catalog_programs), 1, 'anon can read catalog_programs');
select throws_ok(
  $$insert into public.catalog_workouts (id, json, version) values ('x', '{}', 2)$$,
  '42501'::char(5), null::text, 'anon cannot write the catalog');

reset role;
set local role authenticated;
select set_config('request.jwt.claims',
  json_build_object('sub', '11111111-1111-1111-1111-111111111111', 'role', 'authenticated')::text, true);

select is((select count(*)::int from public.catalog_versions), 1, 'authenticated can read catalog_versions');
select is((select count(*)::int from public.catalog_workouts), 1, 'authenticated can read catalog_workouts');
select is((select count(*)::int from public.catalog_programs), 1, 'authenticated can read catalog_programs');
select throws_ok(
  $$insert into public.catalog_versions (version) values (2)$$,
  '42501'::char(5), null::text, 'authenticated cannot insert into the catalog');
select throws_ok(
  $$update public.catalog_programs set json = '{"hacked":true}'$$,
  '42501'::char(5), null::text, 'authenticated cannot update the catalog');
select throws_ok(
  $$delete from public.catalog_workouts$$,
  '42501'::char(5), null::text, 'authenticated cannot delete from the catalog');

reset role;
set local role service_role;

select lives_ok(
  $$insert into public.catalog_versions (version) values (2)$$,
  'service role (publish job) can write the catalog');

select * from finish();
rollback;
