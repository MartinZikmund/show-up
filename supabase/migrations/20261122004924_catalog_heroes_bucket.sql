-- Program hero images. Public bucket: files are served by URL without a policy, which also means
-- nobody can list it. Deliberately no storage.objects policies: only the publish job (secret key,
-- bypasses RLS) can write, anon and authenticated can't.
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'catalog-heroes',
  'catalog-heroes',
  true,
  2097152, -- 2 MiB
  array['image/webp', 'image/png', 'image/jpeg']
)
on conflict (id) do update
set public = excluded.public,
    file_size_limit = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;
