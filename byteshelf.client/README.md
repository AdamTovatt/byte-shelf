# byteshelf.client

The ByteShelf web UI: React, TypeScript, Vite, Tailwind and TanStack Query, built on the house
template. Everyone browses the shelves they reach, searches a folder and everything below it by
name, description, or the title or camera read from its contents (the options at the search
field's end narrow it: "Duplicates only" to the files with the same contents, "No description" to
the files still to describe), uploads (dropping files on a directory works too), opens and
downloads files, previews them over the page (images, video, audio, PDFs and text), shows a file's
details with what the server read from its contents (`FileDetailsDialog`: when and where a photo was
taken, the camera, a video's length, a PDF's title and pages), describes, shares by a link, moves,
renames and deletes them, encrypts files and folders with a password, and restores them from the
trash or previews or downloads a deleted file from it without restoring it (a replica's trash too).
Admins also create shelves and users, set quotas, give access from a shelf or from a user, make keys
and give a key other shelves (a key row's Shelves control in shelf settings), and on the Replication
page pair replicas, see how far behind each is, remove one, and promote a replica; their Settings list
the server's plugins, and why one was skipped or its part in the web UI failed.

`src/ui` is the UI library: buttons, fields, cards, dialogs, menus, page layout and the sidebar, with
the colour, type and icon systems and the Tailwind preset they need. A dialog takes the focus, keeps Tab
in it and gives the focus back. It was copied from the house
component library, trimmed to what ByteShelf uses; pages import from `src/ui`. `src/components` holds
what the library lacks: app-specific pieces (the form dialog, the password dialog, the quota field)
and a few generic ones written for this app (`CardList`, `MiddleTruncatedName`). The file picker is a
hook, `hooks/useFilePicker.tsx`.

## Working on it

```bash
npm ci
npm run dev        # Vite on port 44903, forwarding /api, /plugins, /mcp, /llms.txt and /swagger to the server (ASPNETCORE_URLS, else :5190)
npm run lint
npm test           # Vitest
npm run test:e2e   # Playwright: builds the UI and the Markdown plugin, starts the server on a root of its own, runs e2e/
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
- **Passkeys:** the sign-in page has a passkey button and, where the browser offers it, the saved
  passkeys as suggestions in the email field (`hooks/usePasskeySignIn.ts`), asking the server for a
  new ceremony before the one offered runs out. Settings adds, renames and removes them
  (`components/PasskeysSection.tsx`); a new one is named after the browser and device
  (`lib/deviceName.ts`). The ceremonies go through the browser's own WebAuthn JSON helpers
  (`lib/webauthn.ts`); a browser without them gets no passkeys.
- **Files:** uploads up to 8 MiB go in one request, larger ones as a resumable upload in 8 MiB chunks
  (`services/uploadService.ts`); a file whose name is taken is replaced only when asked. One the browser
  has no type for is sent without one, so the server types it by its name. "Copy link"
  in a file's menu copies one that works for a day, for someone without access; where the browser
  offers no clipboard (plain HTTP), the toast shows it to copy by hand. A shelf's settings show
  everyone a Links tab listing the share and upload links that have not run out (their own, or every one
  for someone who may change the shelf's files) and revoking them; only admins see the other tabs.
  A folder's rows select as in Finder (`hooks/useSelection.ts`): a click selects one, Cmd or Ctrl
  adds or takes one away, Shift selects a range, Space toggles the focused row, Cmd or Ctrl+A selects
  them all, and Escape or a click in empty space clears it; a double-click or Enter opens a row, a file in
  the preview when a viewer takes it and this browser can show it there, and else in a new tab. While
  any are selected, a bar over the search row (the top bar, on a phone) downloads, moves or deletes
  them. A selection belongs to its folder, and a row that leaves the list leaves it for good.
  A row, or the selection it is in, moves by dragging it onto a folder's row or breadcrumb crumb
  (`hooks/useEntryDragAndDrop.ts`, dnd-kit on pointer events, so it never meets the browser's own
  drag events that dropping files to upload uses). A toast that names a file cuts a long name in the
  middle (`components/MiddleTruncatedName.tsx`).
- **Preview** (`components/preview/FilePreview.tsx`): a file over the page in the viewer that takes its
  type, its place in the URL (`?preview=<file id>`, `hooks/usePreviewParam.ts`), so Back closes it and a
  reload keeps it. Previous, next and the arrow keys go through the files a viewer takes, in the order of
  the listing or the search's results (`hooks/useShelfPreview.ts`; past the last result so far, next fetches
  the pages that follow until one holds a file a viewer takes); a file outside them, as after a reload of a
  search, is previewed alone. A preview whose file is gone closes with a toast. In the trash
  (`hooks/useTrashPreview.ts`) it is of the one file a file deletion took, by the deletion's id, without
  neighbours; Open shows it there whatever its type, saying so of one no viewer takes.
  - *Viewers* (`components/preview/viewers/`) are `{ id, mediaTypes, extensions, component, unavailable? }`,
    held by a registry that never changes (`lib/preview/viewerRegistry.ts`) and is read through
    `hooks/useViewers.ts` alone: the built-in ones, listed in `BUILT_IN_VIEWERS` (`viewers/index.ts`), and
    those of the plugins loaded so far. A preview waits for plugins still loading, at most 3 seconds
    (`usePluginsAwaited`), and until they are loaded a file that no viewer shows in this browser opens in the
    preview, not in a tab: one of theirs may. An exact type beats a family (`image/*`), a plugin's viewer a
    built-in one, and the first plugin by id the next; extensions count only for a file stored as
    `application/octet-stream`. The built-in ones render nothing as HTML: HTML shows as its source, and SVG
    through an `<img>`, where its scripts do not run. Text shows its first 1 MiB, decoded as a tab would; an
    image zooms with the wheel, a pinch or a double-click and moves with a drag.
  - *Contents* reach a viewer through a `ContentReader` (`lib/content/contentReader.ts`): `url()`, `blob()`
    and `range()`, whatever they come from. A blob-backed file (`isBlobBacked`) is read whole into the page
    first (`lib/preview/fileContents.ts`), over 100 MiB only once Load, Download or Open in new tab is
    pressed, and let go as the preview moves on. It is read and unlocked once for the preview and for a
    Download or Open in new tab, which goes on as the preview moves on. Its URL is a blob: one of its own
    type only where a tab may show that (`elementUrl` in `lib/localBlobs.ts`): the browser's own "Open image
    in new tab" would run a blob: SVG as the app. An image, text, XML or JSON type gets a data: URL instead,
    any other a blob: one of untyped bytes. What the page reads itself goes past the browser's cache, so
    nothing of it is left there, and a cached part is never taken for the whole of contents swapped since
    (`requestBytes` in `services/contentService.ts`).
  - *A viewer that cannot:* one that throws while rendering is caught and Download offered. A file that
    could not be read says why, and an image, video or recording the browser cannot decode says so, each
    with Try again: a connection lost as the browser loaded it reads as either. A viewer that cannot show
    files in this browser says why (`unavailable`), and the next that takes the type shows them. With none
    left, as for a PDF where the browser shows none inside a page, the file's row opens it in a tab; reached
    otherwise, its preview says why, with Open in new tab and Download.
- **Opening and saving** (`components/fileOpening.tsx`): an unencrypted file opens or saves by navigating to
  `/api/open/...`, where the session's token rides in a cookie the server sets at sign-in and renewal
  (a plain navigation cannot send the header), so no public link is made. The session is renewed
  first when its token is about to run out, since the cookie lives as long as it, or when the
  session holds no record that the server set that cookie. A browser keeps that `Secure` cookie only over HTTPS or on
  localhost, so elsewhere (plain HTTP) the file is read with the token and shown or saved from a
  local blob as an encrypted one is, which holds it whole in memory. One rule picks the way for opening,
  saving and previewing alike, `isBlobBacked` (encrypted, or not `isSecureContext`), in
  `services/contentService.ts`. "Open in new tab" is offered only for what a tab shows (`opensInTab`). The
  same opening serves a file in the trash: a `ContentSource` says whether a file is read live or from one
  deletion (`deletedContent`), and which encrypted folder's password opens it: for a live file the one at
  its path, for a deleted one the folder it was stored in, as the deletion's listing names it. The Trash
  page's Download, and its preview's Download and Open in new tab, go through it; a deleted folder downloads
  whole from the CLI or an agent.
- **Encryption** (`hooks/useEncryption.tsx`, `hooks/usePasswordPrompt.tsx`, `lib/encryption/`): a file's or
  folder's menu encrypts it with a password, typed twice and then a warning that a lost one cannot be
  recovered; typage encrypts in the browser at age's work factor, about a second a file, and the file's
  contents are swapped in place: it keeps its id, and the server's plain contents are gone at once, never to
  the trash (a browser that showed them may keep them in its own cache). A file changed since it was read is
  refused and left as it is. Inside an encrypted folder, a file is encrypted with the folder's password and
  a folder cannot be encrypted on its own; files uploaded into one are encrypted with its password. A folder
  holding a file encrypted with another password is refused before anything changes. Removing a folder's
  encryption lets plain files in again and decrypts every file under it. A file that fails is named and the
  rest go on; a folder's menu offers Encrypt or Remove encryption again while its files show the run
  unfinished, and that finishes it. An encrypted file is read with the token and decrypted here, never
  through a link, and shown in the preview; one no viewer takes, or opened in a tab, is shown from a local
  blob: in a tab for images, PDFs, plain text, audio and video, saved for every other type, since a blob
  runs as the app. Opening one asks for its password; reached by previous, next or a reload it shows Unlock
  instead. Reading a file asks for no password and answers that it is locked; unlocking it asks, and answers
  the contents (`Decryption` in `services/fileReading.ts`). A password is asked for once and kept in the
  tab's `sessionStorage` (an encrypted folder's under the folder), taken out of any tab the app opens, and
  forgotten on sign-in and sign-out, and when the app starts with no session (a tab the browser restores
  after a sign-out); a page that finds another user signed in from another tab starts anew, holding nothing
  it decrypted for the one before. A plain folder's Remove encryption leaves out the files of an encrypted
  folder below it, which has its own.
- **Pages and layout:** a shelf's settings are tabs that are routes, `/shelves/:shelf/settings/:tab`
  (`SHELF_SETTINGS_TABS` in `lib/shelfPaths.ts` lists them; the page picks the tab from the route and
  sends any other to the first tab the user sees: General for an admin, Links for anyone else).
  `components/KnownRole.tsx` holds back a page that differs for admins until the signed-in user is
  known, and says why when that fails, rather than treating them as no admin. Below the `md` width the sidebar becomes a drawer behind a top bar, and
  a page puts its own controls in that bar through `MobileTopBarActions`, or covers it with
  `MobileTopBarOverlay` (`components/layout/MobileTopBar.tsx`).
- **Plugins:** once signed in, the app loads the server's plugins into the page
  (`lib/plugins/pluginHost.ts`, followed with `hooks/usePluginHost.ts`), each failing alone. What they
  import as `@byteshelf/plugin` is `src/plugin/index.ts`; `build/sharedModules.ts` makes that and the
  packages plugins share an entry each of the build, named in `index.html`'s import map, and in the dev
  server too. `build/buildPlugin.ts` builds the plugins in [plugins](../plugins/README.md). What they
  may use is a promise: breaking it raises `PluginApi.Version` on the server. A plugin adds viewers to
  the preview and actions to the menus of live files: in a listing, a search's results and the preview,
  an encrypted file's too, whose password is asked for once the action reads it
  (`hooks/useFileActions.tsx`). Each viewer, menu item and dialog is drawn in an error boundary.
- **Live updates:** `hooks/useInvalidationStream.ts` holds the server's event stream open and
  refetches what each event makes stale (`lib/invalidationMap.ts`), so a change made elsewhere shows up
  within about a second. A test checks the map covers every resource the server publishes. The
  exceptions are the Replication page's status, polled every 5 seconds, as a replica's lag changes
  without anything being published, and the plugins, which change only with a restart of the server:
  they are read at sign-in, and again once that is stale as a shelf, a preview or Settings opens.
