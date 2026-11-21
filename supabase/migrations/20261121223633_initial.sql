-- Show Up: server mirror of the local SQLite tables (storage v4) plus the published catalog.
-- Column mapping and the security model: docs/supabase.md.

-- Trigger functions live outside the exposed schemas, so the Data API can't call them.
create schema if not exists private;
revoke all on schema private from public, anon, authenticated;

-- The pull cursor: every write gets the server's clock, whatever the client sent.
create function private.set_server_updated_at()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
  new.server_updated_at := now();
  return new;
end;
$$;

-- Last writer wins on updated_at: a stale device can't overwrite a newer row (skipped, not an error).
create function private.keep_newest_row()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
  if new.updated_at < old.updated_at then
    return null;
  end if;
  return new;
end;
$$;

revoke all on function private.set_server_updated_at() from public, anon, authenticated;
revoke all on function private.keep_newest_row() from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- User tables. Every row: client-generated id, owner, device, client timestamps,
-- tombstone (deleted_at) and the server cursor. Day numbers are DateOnly.DayNumber
-- (days since 0001-01-01), exactly as stored locally.
-- No foreign keys between user tables: rows of different tables sync independently.
-- ---------------------------------------------------------------------------

create table public.enrollments (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  program_id text not null,
  program_revision int not null,
  track_id text not null,
  start_day_number int not null,
  ended_at timestamptz,
  -- EnrollmentEndReason: 0 Finished, 1 Abandoned.
  end_reason int
);

create table public.completions (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  enrollment_id uuid not null,
  day_index int not null,
  session_index int not null,
  completed_on_day_number int not null,
  completed_at timestamptz not null,
  -- InkCodec JSON kept as text so it round-trips byte for byte; null = the generated marker X.
  ink_json text
);

create table public.schedule_shifts (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  enrollment_id uuid not null,
  from_day_number int not null,
  days int not null
);

create table public.bonus_workouts (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  workout_id text,
  title text not null,
  day_number int not null,
  duration_minutes int
);

create table public.notes (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  enrollment_id uuid not null,
  day_index int,
  session_index int,
  subject_key text not null,
  text text not null
);

create table public.metric_entries (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  -- A scheduled session (enrollment, day, session) or a bonus workout.
  enrollment_id uuid,
  day_index int,
  session_index int,
  bonus_workout_id uuid,
  subject_key text not null,
  workout_id text not null,
  metric_id text not null,
  value double precision,
  text_value text
);

create table public.reminders (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  minutes_of_day int not null check (minutes_of_day between 0 and 1439),
  -- Bit 0 = Monday ... bit 6 = Sunday.
  days_mask int not null check (days_mask between 1 and 127),
  enabled boolean not null
);

create table public.custom_programs (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  subject_key text not null,
  revision int not null,
  program_json text not null
);

create table public.synced_settings (
  id uuid primary key,
  user_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  device_id text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null,
  deleted_at timestamptz,
  server_updated_at timestamptz not null default now(),
  subject_key text not null,
  key text not null,
  value text not null
);

-- Natural keys are merged last-writer-wins by the clients, so these are deliberately NOT unique:
-- two offline devices can each create a row for the same key.
create index notes_user_id_subject_key_idx on public.notes (user_id, subject_key);
create index metric_entries_user_id_subject_key_idx on public.metric_entries (user_id, subject_key, metric_id);
create index custom_programs_user_id_subject_key_idx on public.custom_programs (user_id, subject_key, revision);
create index synced_settings_user_id_subject_key_idx on public.synced_settings (user_id, subject_key);

-- Per table: RLS (owner only, no DELETE: tombstones), pull index, triggers, grants.
-- The pull index ends in id so a page boundary inside one timestamp can resume on (server_updated_at, id).
-- Triggers fire in name order: keep_newest_row can skip the update before the cursor moves.

-- enrollments
alter table public.enrollments enable row level security;

create index enrollments_user_id_server_updated_at_idx on public.enrollments (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.enrollments
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.enrollments
  for each row execute function private.set_server_updated_at();

create policy "enrollments: owner can read" on public.enrollments
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "enrollments: owner can insert" on public.enrollments
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "enrollments: owner can update" on public.enrollments
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.enrollments from public, anon, authenticated, service_role;
grant select, insert, update on table public.enrollments to authenticated;

-- completions
alter table public.completions enable row level security;

create index completions_user_id_server_updated_at_idx on public.completions (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.completions
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.completions
  for each row execute function private.set_server_updated_at();

create policy "completions: owner can read" on public.completions
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "completions: owner can insert" on public.completions
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "completions: owner can update" on public.completions
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.completions from public, anon, authenticated, service_role;
grant select, insert, update on table public.completions to authenticated;

-- schedule_shifts
alter table public.schedule_shifts enable row level security;

create index schedule_shifts_user_id_server_updated_at_idx on public.schedule_shifts (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.schedule_shifts
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.schedule_shifts
  for each row execute function private.set_server_updated_at();

create policy "schedule_shifts: owner can read" on public.schedule_shifts
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "schedule_shifts: owner can insert" on public.schedule_shifts
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "schedule_shifts: owner can update" on public.schedule_shifts
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.schedule_shifts from public, anon, authenticated, service_role;
grant select, insert, update on table public.schedule_shifts to authenticated;

-- bonus_workouts
alter table public.bonus_workouts enable row level security;

create index bonus_workouts_user_id_server_updated_at_idx on public.bonus_workouts (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.bonus_workouts
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.bonus_workouts
  for each row execute function private.set_server_updated_at();

create policy "bonus_workouts: owner can read" on public.bonus_workouts
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "bonus_workouts: owner can insert" on public.bonus_workouts
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "bonus_workouts: owner can update" on public.bonus_workouts
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.bonus_workouts from public, anon, authenticated, service_role;
grant select, insert, update on table public.bonus_workouts to authenticated;

-- notes
alter table public.notes enable row level security;

create index notes_user_id_server_updated_at_idx on public.notes (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.notes
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.notes
  for each row execute function private.set_server_updated_at();

create policy "notes: owner can read" on public.notes
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "notes: owner can insert" on public.notes
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "notes: owner can update" on public.notes
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.notes from public, anon, authenticated, service_role;
grant select, insert, update on table public.notes to authenticated;

-- metric_entries
alter table public.metric_entries enable row level security;

create index metric_entries_user_id_server_updated_at_idx on public.metric_entries (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.metric_entries
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.metric_entries
  for each row execute function private.set_server_updated_at();

create policy "metric_entries: owner can read" on public.metric_entries
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "metric_entries: owner can insert" on public.metric_entries
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "metric_entries: owner can update" on public.metric_entries
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.metric_entries from public, anon, authenticated, service_role;
grant select, insert, update on table public.metric_entries to authenticated;

-- reminders
alter table public.reminders enable row level security;

create index reminders_user_id_server_updated_at_idx on public.reminders (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.reminders
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.reminders
  for each row execute function private.set_server_updated_at();

create policy "reminders: owner can read" on public.reminders
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "reminders: owner can insert" on public.reminders
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "reminders: owner can update" on public.reminders
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.reminders from public, anon, authenticated, service_role;
grant select, insert, update on table public.reminders to authenticated;

-- custom_programs
alter table public.custom_programs enable row level security;

create index custom_programs_user_id_server_updated_at_idx on public.custom_programs (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.custom_programs
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.custom_programs
  for each row execute function private.set_server_updated_at();

create policy "custom_programs: owner can read" on public.custom_programs
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "custom_programs: owner can insert" on public.custom_programs
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "custom_programs: owner can update" on public.custom_programs
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.custom_programs from public, anon, authenticated, service_role;
grant select, insert, update on table public.custom_programs to authenticated;

-- synced_settings
alter table public.synced_settings enable row level security;

create index synced_settings_user_id_server_updated_at_idx on public.synced_settings (user_id, server_updated_at, id);

create trigger keep_newest_row before update on public.synced_settings
  for each row execute function private.keep_newest_row();
create trigger set_server_updated_at before insert or update on public.synced_settings
  for each row execute function private.set_server_updated_at();

create policy "synced_settings: owner can read" on public.synced_settings
  for select to authenticated
  using ((select auth.uid()) = user_id);
create policy "synced_settings: owner can insert" on public.synced_settings
  for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy "synced_settings: owner can update" on public.synced_settings
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.synced_settings from public, anon, authenticated, service_role;
grant select, insert, update on table public.synced_settings to authenticated;

-- ---------------------------------------------------------------------------
-- Published catalog: readable by everyone, written only by the publish job (service role).
-- ---------------------------------------------------------------------------

create table public.catalog_versions (
  version int primary key,
  published_at timestamptz not null default now()
);

create table public.catalog_workouts (
  id text primary key,
  json jsonb not null,
  version int not null
);

-- Every revision is kept: enrollments pin the revision they started on.
create table public.catalog_programs (
  id text not null,
  revision int not null,
  json jsonb not null,
  version int not null,
  primary key (id, revision)
);

create index catalog_workouts_version_idx on public.catalog_workouts (version);
create index catalog_programs_version_idx on public.catalog_programs (version);

alter table public.catalog_versions enable row level security;
alter table public.catalog_workouts enable row level security;
alter table public.catalog_programs enable row level security;

create policy "catalog_versions: anyone can read" on public.catalog_versions
  for select to anon, authenticated
  using (true);
create policy "catalog_workouts: anyone can read" on public.catalog_workouts
  for select to anon, authenticated
  using (true);
create policy "catalog_programs: anyone can read" on public.catalog_programs
  for select to anon, authenticated
  using (true);

revoke all on table public.catalog_versions, public.catalog_workouts, public.catalog_programs
  from public, anon, authenticated, service_role;
grant select on table public.catalog_versions, public.catalog_workouts, public.catalog_programs
  to anon, authenticated;
grant select, insert, update, delete on table public.catalog_versions, public.catalog_workouts, public.catalog_programs
  to service_role;
