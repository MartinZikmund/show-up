# Store listing

The text that goes on the store pages. The release pipeline does not upload it: paste it into Google Play Console,
App Store Connect and Partner Center when a listing changes.

- `listing/en-US.md`, `listing/cs-CZ.md`: one file per language, with fixed headings.

| Heading | Limit | Goes to |
|---|---|---|
| `# Name` (the first line) | 30 characters | app name / title |
| `## Short description` | 80 characters, one line | Play short description, App Store subtitle or promotional text |
| `## Description` | 4000 characters | full description |
| `## Keywords` | 7 at most, 100 characters, comma separated | App Store keywords, Microsoft Store search terms |
| `## What's new` | | release notes for this version |

The limits are the tightest of the three stores, so one text fits all of them. `StoreListingTests` fails the build
when a limit is exceeded, a heading is missing, or the description does not end with the app's own disclaimer
(`AboutDisclaimer` in the language's `Resources.resw`).

## Rules

- **The disclaimer is the last paragraph of every description**, word for word as the app shows it.
- **Program names appear only to say which schedules the app has.** Never as a keyword, never in the name or the
  short description, never with a logo or artwork from the publisher. A store reviewer may still object to the one
  sentence that lists them; if so, shorten that sentence rather than dropping the disclaimer.
- **Say only what the build does.** Cloud sync is not in the listing: it exists, but it is switched on by the
  Supabase project, which is not live yet. Add a Pro line for it when it is.
- Category: Health & Fitness. Not a medical app; no health claims.

## Before the first submission

These are not done, and each one blocks a store:

1. **Deleting an account from inside the app.** Apple and Google both require it for an app that lets you create an
   account. The app has sign-in and sign-out, and no delete (#95).
2. **Privacy policy and terms at a public URL**, signed off. `docs/privacy.md` and `docs/terms.md` are drafts written
   from what the code does; they have a contact address to fill in.
3. **Screenshots.** None are captured yet. They should come from a seeded database, not from hand-made data: phone
   (390 × 844) and desktop (1280 × 800) for Today, Calendar mid-program, the cross moment, Programs and Statistics (#96).
4. **The app icon** and an age rating questionnaire per store.
