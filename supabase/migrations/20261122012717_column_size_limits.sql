-- Free-text and JSON columns are user-controlled: cap them so one row can't bloat the table or its sync pages.
-- Each cap is well above what the app writes (ink <= 600 points, notes <= 4000 chars, metric text <= 200 chars,
-- program JSON refused by the builder at 256 KB).
alter table public.completions
  add constraint completions_ink_json_size check (octet_length(ink_json) <= 65536);

alter table public.custom_programs
  add constraint custom_programs_program_json_size check (octet_length(program_json) <= 262144);

alter table public.notes
  add constraint notes_text_size check (octet_length(text) <= 16384);

alter table public.metric_entries
  add constraint metric_entries_text_value_size check (octet_length(text_value) <= 4096);
