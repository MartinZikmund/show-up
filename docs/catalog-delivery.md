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
  published program needs a new revision number (the publisher refuses to overwrite one, see Publishing).
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

## Publishing

`tools/publish-catalog` (a .NET console app that references `ShowUp.Core`, so it uses the app's parser and
`CatalogValidator`) pushes `catalog/` to Supabase. `.github/workflows/catalog-publish.yml` runs it on pushes to
`main` that touch `catalog/**` and on demand, after the unit tests pass. It needs the `SUPABASE_URL` and
`SUPABASE_SECRET_KEY` secrets (environment `catalog`, setup in [supabase.md](./supabase.md#going-live)); without
them it logs a notice and succeeds. Only one publish runs at a time.

```bash
SUPABASE_URL=https://<ref>.supabase.co SUPABASE_SECRET_KEY=sb_secret_...   dotnet run --project tools/publish-catalog -- --dry-run      # drop --dry-run to publish
```

What it does, in order:

1. Validates the whole folder (exit 1 on any error, nothing is sent).
2. Reads the server: newest `catalog_versions`, every workout and every program revision.
3. Refuses (exit 1, lists every problem) when
   - a workout id is gone from `workouts.json` but is in `workout-ids.lock` or already on the server;
   - a published `(id, revision)` has different JSON (bump `revision`; JSON is compared structurally, so key order
     and number spelling don't count);
   - a program's revision is lower than one already published.
4. If nothing differs from the server, stops: no version bump.
5. Otherwise, with `N` the newest version: uploads new hero images, writes the **new and changed** workouts at version `N + 1` (one
   request, so one transaction; unchanged rows keep their old version and stay in every client's `<=` view), inserts the new program revisions at `N + 1` with `ignore-duplicates` (an existing
   revision is never touched), and **last** inserts `catalog_versions(N + 1)`. A client therefore never sees a
   version without its rows, and two overlapping publishes fail on the version primary key.

Requests carry the secret key in the `apikey` header only (secret keys are not JWTs: no bearer token), and the key is
never printed. Exit codes: 0 published or nothing to do, 1 refused or invalid, 2 request failed.

**Heroes.** The first publish that carries a hero is the first real test of the Storage upload (unit tests use a fake
server), so check the URL opens. The host of the app's `Supabase:Url` must equal the host of `SUPABASE_URL`, or the
app drops the image. `catalog/heroes/` holds the images (generated, see [`tools/hero-art`](../tools/hero-art/README.md)), at most 2 MiB each, webp, png or jpeg; the bundled set is JPEG and kept under 150 KB each. A program whose
`heroImage` is a bundled path such as `Assets/heroes/t25.jpg` with a file of that name in `catalog/heroes/` is published
with `heroImage` rewritten to the public Storage URL
`https://<ref>.supabase.co/storage/v1/object/public/catalog-heroes/t25-<hash>.jpg` (the hash is of the file content, so
an object never changes and caches never go stale). The JSON in `catalog/programs/` stays as authored, which keeps the
bundled fallback. Changing a published hero is a program JSON change and needs a revision bump like any other.

**Known window.** A changed workout's row moves to `N + 1` before the version row is inserted, so for a moment a
client that checks `N` finds that one workout missing. It rejects the set (a known id is gone) and tries again next
start; nothing is applied half way. New workouts and unchanged ones don't open the window.
