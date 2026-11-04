# byteshelf.client

The ByteShelf web UI: React, TypeScript, Vite, Tailwind and TanStack Query, built on the house
template. Everyone browses the shelves they reach, searches a folder and everything below it by
name or description, uploads (dropping files on a directory works too), opens and downloads files,
describes, shares by a link, moves, renames and deletes them, encrypts files and folders with a
password, and restores them from the trash or downloads a deleted file from it without restoring
it (a replica's trash too). Admins
also create shelves and users, set quotas, give access from a shelf or from a user, make keys, and on
the Replication page pair replicas, see how far behind each is, remove one, and promote a replica.

`src/ui` is the UI library: buttons, fields, cards, dialogs, menus, page layout and the sidebar, with
the colour, type and icon systems and the Tailwind preset they need. It was copied from the house
component library, trimmed to what ByteShelf uses; pages import from `src/ui`. `src/components` holds
what the library lacks: app-specific pieces (the form dialog, the password dialog, the quota field)
and a few generic ones written for this app (`CardList`, `MiddleTruncatedName`). The file picker is a
hook, `hooks/useFilePicker.tsx`.

## Working on it

```bash
npm ci
npm run dev        # Vite on port 44903, forwarding /api, /mcp, /llms.txt and /swagger to the server (ASPNETCORE_URLS, else :5190)
npm run lint
npm test           # Vitest
npm run test:e2e   # Playwright: builds the UI, starts the server on a root of its own, runs e2e/
npm run build      # into dist/, which a published server serves
```

Before the first `npm run test:e2e`, install its browser with `npx playwright install chromium`.

`src/lib/encryption/interop.test.ts` checks typage (`age-encryption`) decrypts `dotnet.age`, an age
file ByteShelfClient wrote, and matches `dotnet-check.txt`, a folder check it made; ByteShelfClient's
tests check the other way round.

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
  tab renewed to. Signing out ends the session on the server too. The access token also rides in an
  `HttpOnly` cookie on `/api/open` only, which the server sets and clears; the app never reads it.
- **Files:** uploads up to 8 MiB go in one request, larger ones as a resumable upload in 8 MiB chunks
  (`services/uploadService.ts`); a file whose name is taken is replaced only when asked. "Copy link"
  in a file's menu copies one that works for a day, for someone without access; where the browser
  offers no clipboard (plain HTTP), the toast shows it to copy by hand.
  A folder's rows select as in Finder (`hooks/useSelection.ts`): a click selects one, Cmd or Ctrl
  adds or takes one away, Shift selects a range, Space toggles the focused row, Cmd or Ctrl+A selects
  them all, and Escape or a click in empty space clears it; a double-click or Enter opens a row. While
  any are selected, a bar over the search row (the top bar, on a phone) downloads, moves or deletes
  them. A selection belongs to its folder, and a row that leaves the list leaves it for good.
  A row, or the selection it is in, moves by dragging it onto a folder's row or breadcrumb crumb
  (`hooks/useEntryDragAndDrop.ts`, dnd-kit on pointer events, so it never meets the browser's own
  drag events that dropping files to upload uses). A toast that names a file cuts a long name in the
  middle (`components/MiddleTruncatedName.tsx`).
- **Opening and saving** (`components/fileOpening.tsx`): an unencrypted file opens or saves by navigating to
  `/api/open/...`, where the session's token rides in a cookie the server sets at sign-in and renewal
  (a plain navigation cannot send the header), so no public link is made. The session is renewed
  first when its token is about to run out, since the cookie lives as long as it, or when the
  session holds no record that the server set that cookie. A browser keeps that `Secure` cookie only over HTTPS or on
  localhost, so elsewhere (plain HTTP) the file is read with the token and shown or saved from a
  local blob as an encrypted one is, which holds it whole in memory. One rule picks the way,
  `opensByNavigation` (`isSecureContext`), in `services/contentService.ts`. The same opening serves
  a file in the trash: a `ContentSource` says whether a file is read live or from one deletion
  (`deletedContent`), and which encrypted folder's password opens it: for a live file the one at its
  path, for a deleted one the folder it was stored in, as the deletion's listing names it. The
  Trash page's Download saves a deleted file through it; a deleted folder downloads whole from the
  CLI or an agent.
- **Encryption** (`hooks/useEncryption.tsx`, `lib/encryption/`): a file's or folder's menu encrypts
  it with a password, typed twice and then a warning that a lost one cannot be recovered; typage
  encrypts in the browser at age's work factor, about a second a file, and the file's contents are
  swapped in place: it keeps its id, and the plain contents are gone at once, never to the trash. A
  file changed since it was read is refused and left as it is. Inside an encrypted folder, a file is encrypted with the folder's password and a folder
  cannot be encrypted on its own; files uploaded into one are encrypted with its password. A folder
  holding a file encrypted with another password is refused before anything changes. Removing a
  folder's encryption lets plain files in again and decrypts every file under it. A file that fails
  is named and the rest go on; a folder's menu offers Encrypt or Remove encryption again while its
  files show the run unfinished, and that finishes it. An encrypted file is read with the token and
  decrypted here, never through a link, and shown from a local blob: in a tab for images, PDFs, plain
  text, audio and video, saved for every other type, since a blob runs as the app. A password is
  asked for once and kept in the tab's `sessionStorage` (an encrypted folder's under the folder),
  taken out of any tab the app opens, and forgotten on sign-in and sign-out, and when the app starts
  with no session (a tab the browser restores after a sign-out). A plain folder's Remove encryption
  leaves out the files of an encrypted folder below it, which has its own.
- **Pages and layout:** a shelf's settings are tabs that are routes, `/shelves/:shelf/settings/:tab`
  (`SHELF_SETTINGS_TABS` in `lib/shelfPaths.ts` lists them; the page picks the tab from the route and
  sends any other to General). Below the `md` width the sidebar becomes a drawer behind a top bar, and
  a page puts its own controls in that bar through `MobileTopBarActions`, or covers it with
  `MobileTopBarOverlay` (`components/layout/MobileTopBar.tsx`).
- **Live updates:** `hooks/useInvalidationStream.ts` holds the server's event stream open and
  refetches what each event makes stale (`lib/invalidationMap.ts`), so a change made elsewhere shows up
  within about a second. A test checks the map covers every resource the server publishes. The one
  exception is the Replication page's status, polled every 5 seconds: a replica's lag changes
  without anything being published.
