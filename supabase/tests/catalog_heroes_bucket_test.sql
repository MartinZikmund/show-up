-- The hero image bucket: public, small, images only, and writable by nobody but the publish job.
begin;
create extension if not exists pgtap with schema extensions;

select plan(8);

select is(
  (select public from storage.buckets where id = 'catalog-heroes'), true,
  'catalog-heroes is a public bucket');
select is(
  (select file_size_limit from storage.buckets where id = 'catalog-heroes'), 2097152::bigint,
  'catalog-heroes has a 2 MiB size limit');
select is(
  (select array(select unnest(allowed_mime_types) order by 1) from storage.buckets where id = 'catalog-heroes'),
  array['image/jpeg', 'image/png', 'image/webp'],
  'catalog-heroes only accepts webp, png and jpeg');

select is_empty($$
  select policyname from pg_policies
  where schemaname = 'storage' and tablename = 'objects'
    and (roles && array['anon', 'authenticated']::name[] or roles && array['public']::name[])
$$, 'no storage.objects policy for anon or authenticated (no writes, no listing)');

set local role anon;
select set_config('request.jwt.claims', json_build_object('role', 'anon')::text, true);
select throws_ok(
  $$insert into storage.objects (bucket_id, name) values ('catalog-heroes', 'x.png')$$,
  '42501'::char(5), null::text, 'anon cannot upload a hero');

reset role;
set local role authenticated;
select set_config('request.jwt.claims',
  json_build_object('sub', '11111111-1111-1111-1111-111111111111', 'role', 'authenticated')::text, true);
select throws_ok(
  $$insert into storage.objects (bucket_id, name) values ('catalog-heroes', 'x.png')$$,
  '42501'::char(5), null::text, 'authenticated cannot upload a hero');
select is((select count(*)::int from storage.objects where bucket_id = 'catalog-heroes'), 0,
  'authenticated cannot list the bucket');

reset role;
set local role anon;
select is((select count(*)::int from storage.objects where bucket_id = 'catalog-heroes'), 0,
  'anon cannot list the bucket');

select * from finish();
rollback;
