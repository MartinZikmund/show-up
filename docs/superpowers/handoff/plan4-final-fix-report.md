# Plan 4 final fix wave report

Branch fix/plan4-final-wave, 18 commits (fix then test per bug; docs and ci separate), not pushed.

1. Partial-publish recovery: ServerState.MaxRowVersion; orphan rows above the recorded version make the plan recover that version and always insert catalog_versions (Describe says "recovering version N"). Planner + publisher tests.
2. docs/supabase.md: `{{ .Token }}` in Magic Link and Confirm signup, verify by signing up a fresh address.
3. AccountService: store failures logged, Announce in finally. Test with InvalidOperationException store.
4. SyncService: Gate() re-checked before Idle at pass end. Test fails without the fix (verified).
5. Planner starts at max(server.Version, BundledCatalogVersion)+1. Existing empty-server test expectation moved 1 -> 2.
6. CatalogService.Invalidate clears _pinned. Test (FakeCustomProgramRepository.Clear added).
7. AccountService.SessionEnded (set when the server ends a session, not on user sign-out; cleared on sign-in or Dismiss) + Settings InfoBar, en+cs strings, VM HasSessionEnded/DismissSessionEndedCommand. Service and VM tests. Not covered: revocation arriving mid sign-in.
8. Size limits: migration 20261122012717_column_size_limits.sql (ink 64 KB, program 256 KB, notes 16 KB, metric text 4 KB, octet_length) + supabase/tests/column_size_limits_test.sql (at-limit ok, over -> 23514; NOT run, no Docker, CI database job runs it). Client limits all below: InkCodec 600 points, notes 4000 chars, metric text 200 chars. Custom program JSON had no limit: added CustomProgramProblem.TooLarge (256 KB) to Validate, builder message (en+cs), test.
9. Docs: entitlements, accounts, sync-classification, storage v6 row (v5 already there), supabase.md hand-off folded into sync.md (serialize-every-key note; the rest was already there), "both migrations" -> "every migration", csproj comment, README index label.
10. catalog-publish.yml test-job checkout pinned to the publish job SHA.

Verification: dotnet test 1168 total, 1167 passed, 1 skipped (pre-existing). Heads built with 0 errors: desktop, windows10.0.26100, browserwasm, android, ios. tools/publish-catalog builds.
