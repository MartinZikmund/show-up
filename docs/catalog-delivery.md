# Catalog delivery (over the air)

You want a corrected schedule or a new program to reach installed apps without a store release. The app
ships a verified catalog ([catalog.md](./catalog.md)); newer versions published to Supabase
([supabase.md](./supabase.md), `catalog_*` tables) are picked up in the background.

## Flow

`CatalogUpdateService` (Core) runs on app start and on resume, at most every 12 hours. Skipped entirely when
`SupabaseOptions.IsConfigured` is false: no request is made.

1. Read `max(catalog_versions.version)` with the publishable key only. Catalog tables are public read, so this works
   signed out.
2. If it is not greater than `max(bundled, cached)` stop. Nothing is downloaded.
3. Otherwise page through `catalog_workouts` and `catalog_programs` with `version=lte.N` (200 rows a page, ordered by
   primary key). Each table holds one row per workout and per program revision, so this is the full catalog up to `N`.
   A table longer than 50 pages (10,000 rows) fails the check instead of being downloaded.
4. Read the newest version again. If it moved while downloading, the rows may be a mix: nothing is applied and the
   next start or resume tries again.
5. `CatalogService.ApplyAsync` validates and caches, swaps the already-built catalog into memory, then
   `DataChangeAnnouncer.Announce()` tells the pages.

Everything is in the background; the first page never waits for it. Offline or any failure keeps the catalog the app
has, with a debug log and no user-facing error. A failed check does not use up the 12 hours.

## Validation

- **Programs:** each one goes through `CatalogParser.TryParseProgram` and `CatalogValidator` against the workouts
  that would be current, and its row id and revision must match the JSON. An invalid one is skipped with a warning;
  the installed (bundled or cached) version of that program stays.
- **Workouts:** the list replaces the current one only if every id known today is still in it (ids are append-only,
  see `catalog/workout-ids.lock`) and it validates. Otherwise it is rejected as a whole and the previous workouts
  stay; the programs of that version are still checked against them (one that needs a new workout is skipped). The
  version is recorded only when the workouts were accepted, so a corrected publish is looked at again.
- **Hero images:** an absolute `heroImage` must be `https` on the host of `SupabaseOptions.Url` (Storage public
  URLs). Anything else is dropped with a warning, the program stays, and it takes the hero an earlier revision of the
  same program had (the bundled art). A plain relative path (`Assets/heroes/x.webp`: no `:`, not starting with `/` or `\`) is a bundled asset and passes;
  everything else must be an absolute `https` URL on that host, so `ms-appx:` URIs, `https:/host/x`, `//host/x` and
  rooted paths are all refused.
- **Same revision, different content:** a `(id, revision)` the app already holds is never replaced. A fix to a
  published program needs a new revision number (the publish job, Task 6, must bump it).
- Custom programs never come from the server.

## Storage

Two SQLite tables from migration v6, in the app database (so WebAssembly persists them like the rest):

| Table | Holds |
|---|---|
| `catalog_cache_state` | one row: newest downloaded version and the workouts array (null while the bundled ones are current) |
| `catalog_cache_program` | `(ProgramId, Revision)` primary key, the version it came from, the program JSON |

SQLite over files because the repo already persists it on every head, and one transaction swaps the version, workouts
and programs together. A failure while downloading or writing (the latter logged as a warning) leaves the previous
cache and the in-memory catalog untouched; memory is swapped only after the write commits. Programs are only added, never deleted.

On load the bundled catalog is read first, then the cache is laid over it and re-validated, so a cached program that
no longer validates is ignored.

## Pinned revisions

`GetProgramAsync(id)` returns the newest revision and the Programs page lists one card per program.
`GetProgramAsync(id, revision)` also resolves older revisions from an archive of everything bundled plus everything
ever cached, so an enrollment keeps its schedule after a newer revision arrives. If a pinned revision is somehow in
neither, the enrollment falls back to the latest revision (see `ActiveEnrollmentService`) and is skipped only if the
program is gone entirely; nothing throws.

## Hero images

A program's `heroImage` may be an `https` URL on the Supabase host (see Validation); `ProgramHero` checks the same
rule again (`HeroImagePolicy`, host set at startup). It shows the accent gradient first, swaps the image in on
`BitmapImage.ImageOpened`, and goes back to the gradient on `ImageFailed`. Images are not downloaded ahead of time.
