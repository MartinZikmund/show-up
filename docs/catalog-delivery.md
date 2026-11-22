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
4. `CatalogService.ApplyAsync` validates and caches, then `DataChangeAnnouncer.Announce()` tells the pages.

Everything is in the background; the first page never waits for it. Offline or any failure keeps the catalog the app
has, with a debug log and no user-facing error. A failed check does not use up the 12 hours.

## Validation

- **Programs:** each one goes through `CatalogParser.TryParseProgram` and `CatalogValidator` against the workouts
  that would be current, and its row id and revision must match the JSON. An invalid one is skipped with a warning;
  the installed (bundled or cached) version of that program stays.
- **Workouts:** the list replaces the current one only if every id known today is still in it (ids are append-only,
  see `catalog/workout-ids.lock`) and it validates. Otherwise it is rejected as a whole and the previous workouts
  stay; the programs of that version are still checked against them.
- Custom programs never come from the server.

## Storage

Two SQLite tables from migration v6, in the app database (so WebAssembly persists them like the rest):

| Table | Holds |
|---|---|
| `catalog_cache_state` | one row: newest downloaded version and the workouts array (null while the bundled ones are current) |
| `catalog_cache_program` | `(ProgramId, Revision)` primary key, the version it came from, the program JSON |

SQLite over files because the repo already persists it on every head, and one transaction swaps the version, workouts
and programs together. A failure while downloading or writing leaves the previous cache and the in-memory catalog
untouched. Programs are only added, never deleted.

On load the bundled catalog is read first, then the cache is laid over it and re-validated, so a cached program that
no longer validates is ignored.

## Pinned revisions

`GetProgramAsync(id)` returns the newest revision and the Programs page lists one card per program.
`GetProgramAsync(id, revision)` also resolves older revisions from an archive of everything bundled plus everything
ever cached, so an enrollment keeps its schedule after a newer revision arrives. If a pinned revision is somehow in
neither, the enrollment falls back to the latest revision (see `ActiveEnrollmentService`) and is skipped only if the
program is gone entirely; nothing throws.

## Hero images

A program's `heroImage` may be an `https` URL (Storage public URL). `ProgramHero` shows the accent gradient first and
swaps the image in when it opens; if it never loads the gradient stays. Images are not downloaded ahead of time.
