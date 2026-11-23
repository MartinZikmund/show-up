-- Size caps on user-controlled text/JSON columns: at the limit passes, one byte over is a check violation (23514).
begin;
create extension if not exists pgtap with schema extensions;

select plan(8);

insert into auth.users (id, aud, role, email)
values ('11111111-1111-1111-1111-111111111111', 'authenticated', 'authenticated', 'a@example.com');

create function pg_temp.ins(t text, size int)
returns void
language plpgsql
as $fn$
declare
  base text := '(id, user_id, device_id, created_at, updated_at, ';
  vals text := format('(%L, %L, ''dev'', now(), now(), ', gen_random_uuid(), '11111111-1111-1111-1111-111111111111');
  body text := repeat('x', size);
begin
  if t = 'completions' then
    execute format('insert into public.completions %s enrollment_id, day_index, session_index, completed_on_day_number, completed_at, ink_json) values %s %L, 0, 0, 1, now(), %L)', base, vals, gen_random_uuid(), body);
  elsif t = 'custom_programs' then
    execute format('insert into public.custom_programs %s subject_key, revision, program_json) values %s ''c:x'', 1, %L)', base, vals, body);
  elsif t = 'notes' then
    execute format('insert into public.notes %s enrollment_id, subject_key, text) values %s %L, ''s'', %L)', base, vals, gen_random_uuid(), body);
  else
    execute format('insert into public.metric_entries %s subject_key, workout_id, metric_id, text_value) values %s ''s'', ''w'', ''m'', %L)', base, vals, body);
  end if;
end
$fn$;

select lives_ok($$select pg_temp.ins('completions', 65536)$$, 'completions.ink_json: 64 KB is fine');
select throws_ok($$select pg_temp.ins('completions', 65537)$$, '23514'::char(5), null::text, 'completions.ink_json: over 64 KB is refused');
select lives_ok($$select pg_temp.ins('custom_programs', 262144)$$, 'custom_programs.program_json: 256 KB is fine');
select throws_ok($$select pg_temp.ins('custom_programs', 262145)$$, '23514'::char(5), null::text, 'custom_programs.program_json: over 256 KB is refused');
select lives_ok($$select pg_temp.ins('notes', 16384)$$, 'notes.text: 16 KB is fine');
select throws_ok($$select pg_temp.ins('notes', 16385)$$, '23514'::char(5), null::text, 'notes.text: over 16 KB is refused');
select lives_ok($$select pg_temp.ins('metric_entries', 4096)$$, 'metric_entries.text_value: 4 KB is fine');
select throws_ok($$select pg_temp.ins('metric_entries', 4097)$$, '23514'::char(5), null::text, 'metric_entries.text_value: over 4 KB is refused');

select * from finish();
rollback;
