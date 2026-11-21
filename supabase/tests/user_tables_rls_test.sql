-- Per-user isolation on every synced table: two users, A acts, B's rows must stay invisible and untouched.
begin;
create extension if not exists pgtap with schema extensions;

select plan(9 * 15);

insert into auth.users (id, aud, role, email)
values
  ('11111111-1111-1111-1111-111111111111', 'authenticated', 'authenticated', 'a@example.com'),
  ('22222222-2222-2222-2222-222222222222', 'authenticated', 'authenticated', 'b@example.com');

create schema tests;

-- Runs the whole matrix for one table. cols/vals: the table's own non-null columns with sample values.
create function tests.user_table_rls(t text, cols text, vals text)
returns setof text
language plpgsql
as $fn$
declare
  user_a uuid := '11111111-1111-1111-1111-111111111111';
  user_b uuid := '22222222-2222-2222-2222-222222222222';
  row_a uuid := gen_random_uuid();
  row_b uuid := gen_random_uuid();
  row_new uuid := gen_random_uuid();
  row_stale uuid := gen_random_uuid();
  stale timestamptz := '2000-01-01T00:00:00Z';
  ins text := format(
    'insert into public.%I (id, user_id, device_id, created_at, updated_at, %s) values (%%L, %%L, %%L, now(), now(), %s)',
    t, cols, vals);
  ins_with_cursor text := format(
    'insert into public.%I (id, user_id, device_id, created_at, updated_at, server_updated_at, %s) values (%%L, %%L, %%L, now(), now(), %%L, %s)',
    t, cols, vals);
  n int;
  txt text;
  ts timestamptz;
begin
  execute format(ins, row_a, user_a, 'dev-a');
  execute format(ins, row_b, user_b, 'dev-b');

  -- As user A.
  set local role authenticated;
  perform set_config('request.jwt.claims', json_build_object('sub', user_a, 'role', 'authenticated')::text, true);

  execute format('select count(*) from public.%I where id = $1', t) into n using row_a;
  return next is(n, 1, t || ': owner sees own row');

  execute format('select count(*) from public.%I where id = $1', t) into n using row_b;
  return next is(n, 0, t || ': cannot see another user''s row');

  execute format('update public.%I set device_id = %L where id = $1', t, 'hacked') using row_b;
  get diagnostics n = row_count;
  return next is(n, 0, t || ': cannot update another user''s row');

  return next throws_ok(
    format(ins, row_new, user_b, 'dev-a'),
    '42501'::char(5), null::text,
    t || ': cannot insert a row owned by another user');

  return next throws_ok(
    format(ins || ' on conflict (id) do update set device_id = excluded.device_id', row_b, user_a, 'hacked'),
    '42501'::char(5), null::text,
    t || ': upsert cannot take over another user''s row id');

  return next lives_ok(format(ins, row_new, user_a, 'dev-a'), t || ': owner can insert own row');

  return next lives_ok(
    format(ins_with_cursor, row_stale, user_a, 'dev-a', stale),
    t || ': owner can insert a row carrying a client server_updated_at');

  return next throws_ok(
    format('update public.%I set user_id = %L where id = %L', t, user_b, row_a),
    '42501'::char(5), null::text,
    t || ': update cannot reassign user_id');

  return next throws_ok(
    format('delete from public.%I where id = %L', t, row_a),
    '42501'::char(5), null::text,
    t || ': delete is denied (tombstones only)');

  -- As anon.
  reset role;
  set local role anon;
  perform set_config('request.jwt.claims', json_build_object('role', 'anon')::text, true);

  return next throws_ok(
    format('select * from public.%I', t),
    '42501'::char(5), null::text,
    t || ': anon cannot read');

  return next throws_ok(
    format(ins, gen_random_uuid(), user_a, 'dev-anon'),
    '42501'::char(5), null::text,
    t || ': anon cannot insert');

  -- As the owner of the database, check what actually happened.
  reset role;

  execute format('select device_id from public.%I where id = $1', t) into txt using row_b;
  return next is(txt, 'dev-b'::text, t || ': another user''s row is unchanged');

  execute format('select server_updated_at from public.%I where id = $1', t) into ts using row_stale;
  return next is(ts, now(), t || ': insert sets server_updated_at to the server clock');

  execute format('alter table public.%I disable trigger set_server_updated_at', t);
  execute format('update public.%I set server_updated_at = $1 where id = $2', t) using stale, row_a;
  execute format('alter table public.%I enable trigger set_server_updated_at', t);

  set local role authenticated;
  perform set_config('request.jwt.claims', json_build_object('sub', user_a, 'role', 'authenticated')::text, true);
  execute format('update public.%I set device_id = %L where id = $1', t, 'dev-a2') using row_a;
  execute format('update public.%I set device_id = %L, updated_at = updated_at - interval ''1 day'' where id = $1', t, 'stale-write') using row_new;
  reset role;

  execute format('select server_updated_at from public.%I where id = $1', t) into ts using row_a;
  return next is(ts, now(), t || ': update advances server_updated_at');

  execute format('select device_id from public.%I where id = $1', t) into txt using row_new;
  return next is(txt, 'dev-a'::text, t || ': an older updated_at does not overwrite a newer row');
end;
$fn$;

select * from tests.user_table_rls('enrollments',
  'program_id, program_revision, track_id, start_day_number',
  '''insanity'', 1, ''main'', 739580');
select * from tests.user_table_rls('completions',
  'enrollment_id, day_index, session_index, completed_on_day_number, completed_at, ink_json',
  'gen_random_uuid(), 0, 0, 739580, now(), ''{"strokes":[]}''');
select * from tests.user_table_rls('schedule_shifts',
  'enrollment_id, from_day_number, days',
  'gen_random_uuid(), 739580, 2');
select * from tests.user_table_rls('bonus_workouts',
  'workout_id, title, day_number, duration_minutes',
  '''ab-ripper'', ''Ab Ripper X'', 739580, 16');
select * from tests.user_table_rls('notes',
  'enrollment_id, subject_key, text',
  'gen_random_uuid(), ''n:p:insanity'', ''Felt strong''');
select * from tests.user_table_rls('metric_entries',
  'subject_key, workout_id, metric_id, value',
  '''m:s:x:0:0'', ''fit-test'', ''suicide-jumps'', 18');
select * from tests.user_table_rls('reminders',
  'minutes_of_day, days_mask, enabled',
  '420, 127, true');
select * from tests.user_table_rls('custom_programs',
  'subject_key, revision, program_json',
  '''c:custom-1'', 1, ''{}''');
select * from tests.user_table_rls('synced_settings',
  'subject_key, key, value',
  '''k:MarkerColor'', ''MarkerColor'', ''#D9372B''');

select * from finish();
rollback;
