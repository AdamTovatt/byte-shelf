# byteshelf.client

The ByteShelf web UI: React, TypeScript, Vite, Tailwind and TanStack Query, built on the house
template. Everyone browses the shelves they reach, searches a folder and everything below it by
name or description, uploads (dropping files on a directory works too), opens and downloads files,
describes, shares by a link, moves, renames and deletes them, and restores them from the trash. Admins
also create shelves and users, set quotas, give access from a shelf or from a user, make keys, and on
the Replication page pair replicas, see how far behind each is, remove one, and promote a replica.

`src/ui` is the UI library: buttons, fields, cards, dialogs, menus, page layout and the sidebar, with
the colour, type and icon systems and the Tailwind preset they need. It was copied from the house
component library, trimmed to what ByteShelf uses; pages import from `src/ui`, and app-specific
pieces (the form dialog, the file button, the quota field) live in `src/components`.

## Working on it

```bash
npm ci
npm run dev        # Vite on port 44903, forwarding /api to the server (ASPNETCORE_URLS, else :5190)
npm run lint
npm test           # Vitest
npm run test:e2e   # Playwright: builds the UI, starts the server on a root of its own, runs e2e/
npm run build      # into dist/, which a published server serves
```

Before the first `npm run test:e2e`, install its browser with `npx playwright install chromium`.

`src/types/api.ts` is generated from the server's OpenAPI document, never edited by hand. After an API
change, start the server in schema-only mode (it needs no root or database) where codegen reads it,
then generate:

```bash
ASPNETCORE_URLS=http://localhost:5190 dotnet run --project ../ByteShelf.Server --no-launch-profile -- --schema-only
npm run codegen    # in a second terminal
```

## How it fits together

- **Calls:** `services/apiClient.ts` wraps `openapi-fetch` over the generated types. It sends the
  session's access token, renews it when it is about to run out, and renews and resends once on a 401
  that challenges the token; a 401 without a challenge (a wrong current password) is an answer.
- **The session** is kept in `localStorage`, shared by every tab. A refresh token works once, so
  renewals run one at a time across tabs (the Web Locks API), and each first takes up a token another
  tab renewed to. Signing out ends the session on the server too.
- **Files:** uploads up to 8 MiB go in one request, larger ones as a resumable upload in 8 MiB chunks
  (`services/uploadService.ts`); a file whose name is taken is replaced only when asked. Opening or
  downloading a file asks the server for a five-minute link first, since a plain link cannot carry the
  token. "Copy link" in a file's menu copies one that works for a day, for someone without access;
  where the browser offers no clipboard (plain HTTP), the toast shows it to copy by hand.
- **Live updates:** `hooks/useInvalidationStream.ts` holds the server's event stream open and
  refetches what each event makes stale (`lib/invalidationMap.ts`), so a change made elsewhere shows up
  within about a second. A test checks the map covers every resource the server publishes. The one
  exception is the Replication page's status, polled every 5 seconds: a replica's lag changes
  without anything being published.
