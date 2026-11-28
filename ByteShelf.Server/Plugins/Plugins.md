# Plugins

Folders that add to the web UI, the server, or both, loaded at startup without rebuilding either. A
server without plugins carries none of their dependencies.

Plugins run in the app and the server with **full trust**: a web plugin can do what the signed-in user
can and reads what the page holds (session, unlocked passwords), a server plugin can do what the
server can. Writing and installing one is at one's own risk; there is no sandbox.

## A plugin folder

```
plugin.json
ui/plugin.js        its part in the web UI, an ES module (optional)
ui/plugin.css       its styles (optional, beside plugin.js)
ui/...              whatever plugin.js loads
<server assembly>   named in plugin.json (optional), with its own packages and deps.json
```

`plugin.json`:

| Field | Meaning |
|---|---|
| `id` | Lowercase letters, digits and hyphens. What the plugin is called everywhere. The folder's name means nothing, and of two folders with one id neither is loaded; two entries that lead to one folder are one plugin. |
| `name`, `version` | For people to read. |
| `api` | `PluginApi.Version` of the servers it is for (see Versions). Another number is not loaded. |
| `serverAssembly` | The path of its server assembly in the folder; left out when it has none. |
| `fields` | The fields it declares in what it keeps about files ([Metadata](../Metadata/Metadata.md)), below; left out when it has none. |

Other fields are ignored. An entry of `fields` needs the first four below, and one that is not as this says,
such as of another `kind`, refuses the plugin:

| Field | Meaning |
|---|---|
| `name` | The property of the document, at its top: a letter, then letters, digits, hyphens and underscores, 64 characters at most. No two have one name. |
| `label` | For people to read. |
| `kind` | `text`, `number`, `date` or `flag`. |
| `document` | `file` for the document about the file, `contents` for the one about its contents. |
| `searched` | `true` for a text that a search's free text matches; `false` when left out. |
| `listed` | `true` for one listings carry; `false` when left out. |

## Loading (`PluginDiscovery`, `PluginLoader`)

`BYTESHELF_PLUGINS` names a folder; every folder directly under it, and every link, is taken for a
plugin, so a link that leads nowhere is listed as skipped. Links are resolved once, at startup,
since updaemon changes them under a running server; a change takes a restart. `IPluginCatalog` is
what was loaded and what was skipped, and does not change while the server runs.

A problem with a plugin never stops the server: one that is malformed, for another `api`, whose
assembly fails to load, whose registration throws or whose controllers cannot be built
(`PluginEndpoints` builds every endpoint once, as the server does when it starts) is logged, skipped
and listed for admins with the reason. A loaded plugin can stop it: services that break building the
host and hosted services that throw are its full trust.

`--check-plugin <folder>` (`PluginCheckCommand`) loads one plugin as the server does, among the same
registrations of the server's own (`Program.AddServerServices`), which are thrown away. The check
and the server can still answer differently where the plugin's registration depends on the values in
those, which are the defaults whatever the environment sets (the root, the secret, the public
address, settings), on the email sender, which is the one that only logs, on what the machine
configures (`appsettings.json`, `ASPNETCORE_*`), or on another plugin's registrations. It sees that
one folder, so not another plugin with the same id. Its arguments, exit codes and what it may need
are a contract with updaemon 1.1 or later, which runs it before switching versions, as the server's user, with
only `PATH` and `HOME`, from any folder. Of the server, only itself and the processes it starts for plugins
([Processes](Processes/Processes.md)) load them; `--schema-only`, `--migrate-only` and the root commands do not.
A plugin's entry point is made and its registration run in each of those processes too, with the defaults
`--check-plugin` has and without the server's environment: one more reason for neither to have effects.

## The server assembly

It is written against `ByteShelf.Plugins`, which like `ByteShelfCommon` is MIT-licensed
([LICENSE](../../ByteShelf.Plugins/LICENSE)). It holds one public class implementing
`IByteShelfPlugin`, which registers what it adds through an `IPluginBuilder`: its services, in a
copy of the server's collection, taken over only once the plugin can be loaded, so one that fails
leaves nothing registered and no controller behind. The exception is what a registration does to an
object already registered: `AddHttpClient<T>` keeps the names of typed clients in one, so a typed
client needs a class name no other plugin's has, and a failed plugin's stays taken until the server
restarts. A copy rather than an empty collection, because the framework's own registrations
(`AddHttpClient`, `AddLogging`) add to what is already there, and a second set beside the server's
breaks or resets it. There is no access to shelves or files in the contract, but for the reader's and
the maker's, below, to contents.

What the server shares with a plugin must be no older than the plugin was built against, or it is
refused before any of it runs, as one for another api is. That goes for its server assembly and for
those in its folder that the assembly's references lead to, not for one it loads by name.

A plugin that reads files' contents adds one reader, `builder.AddReader<TReader>(version, mediaTypes)`: an
`IContentDocumentReader`, which the server runs in the background, once per version, over every object that a
live plain file of those types holds, and whose answers it keeps as the plugin's documents about contents
([Metadata](../Metadata/Metadata.md)). A second reader, or a type that is none, refuses the plugin. The
reader is made with no services (`new()`), in a process of the plugin's own, which loads the plugin anew and
shares no state with the rest of it ([Processes](Processes/Processes.md)); not by `--check-plugin`.
`IPluginReaders` is the one place that says which readers there are, and how each is run. One that cannot be
made reads nothing until it can: it is logged, and admins see why on the Settings page (`PluginProblems`), as they
see why a maker could not run ([Derived files](../Derived/Derived.md)). What it
would have read stays to be read. Its plugin's controllers and web UI go on, and so does its maker when the
reader's constructor throws; one that crashes or takes over 60 seconds keeps the whole process from loading,
and so the maker from running, as a maker's does the reader. A read that takes over 60 seconds, throws,
or answers more than a document holds (64 KiB, nested 32 deep) is recorded as saying nothing, and is not tried
again by that version. **A reader keeps what it reads nowhere else**, where it would outlive the plaintext.

A plugin that makes derived files, such as thumbnails, adds one maker, `builder.AddMaker<TMaker>(...)`, with each
kind it makes, at a version, from media types: an `IDerivedFileMaker`, made and run as a reader is, whose files
the server keeps and serves ([Derived files](../Derived/Derived.md)). Its `agent` files are shown to agents only
when they are a PNG, JPEG, GIF or WebP of at most 1 MiB ([Mcp](../Mcp/Mcp.md)). Its `stream` files are made in a
run of their own, in a process of their own beside its others, which may take 24 hours at the lowest CPU priority
([Processes](Processes/Processes.md)); every other run takes 60 seconds at most. Why one could not run is in the server's
log alone, and what it would have made is not there when asked for. A second maker, a kind twice, or a type
that is none refuses the plugin. It too keeps what it reads and makes nowhere else.

Each plugin has an `AssemblyLoadContext` of its own (`PluginLoadContext`): its packages and native
libraries come from its folder, so it may use another version of a package than the server does.
`ByteShelf.Plugins`, `ByteShelfCommon` and the framework always come from the server
(`HostAssemblies`), whatever the plugin brings, so the two agree on their types and rules. A plugin
references the first two without copying them (`Private="false" ExcludeAssets="runtime"`) and sets
`EnableDynamicLoading`, as `plugins/images/server` does.

Its controllers answer under `/api/plugins/{id}/` whatever routes they declare
(`PluginRouteProvider`), so none can take an address of the server's, and are left out of the
OpenAPI document. Controllers are the only endpoints the contract has: one a plugin maps some other
way, such as in middleware of its own, is its full trust. They are guarded by `[Authorize]`, which
takes a signed-in user and no shelf key, are open to anyone without it, and refuse writes on a
replica.

## The web UI part

`GET /api/plugins` lists the loaded plugins with the addresses of their module and stylesheet, for
any signed-in user or shelf key, and for an admin those skipped and why a plugin's reader could not run
when it last had something to read (`readerProblem`). The `ui` folder, and nothing else
of the plugin, is served at `/plugins/{id}/{hash}/...` (`UsePluginAssets`), to anyone, as a browser
loads modules without the session's token. The hash is of every file in the folder, so each may be
cached for good, and is in the deploy id ([Version](../Version/Version.md)), so open tabs are told
of a new version. It is taken once, at startup, so a plugin's folder must not change under a running
server.

The app asks for the list once someone is signed in and imports each plugin's module
(`lib/plugins/pluginHost.ts`), showing it once its stylesheet is there too. One whose module or
stylesheet fails, whose default export is not made with `definePlugin`, or whose viewers, file
actions or folder types lack what each must have (`partsOf` in `lib/plugins/pluginDefinition.ts`), adds nothing to
the page and leaves no styles in it; the others are not affected, nor is its own server part, and
admins see the reason on the Settings page. `index.html`'s import map
(`byteshelf.client/build/sharedModules.ts`) has the browser resolve the modules plugins share with
the app, so there is one React and one of each context.

A module's default export is `definePlugin({ viewers, fileActions, folderTypes })`, from `@byteshelf/plugin`:

- **Viewers** show files in the preview, as the app's own do: `{ id, mediaTypes, extensions, component,
  unavailable?, clearOfBars? }`. `extensions` count only for files stored without a type
  (`application/octet-stream`). The component, which may be a `lazy` one, gets the file's record and a
  content reader, which answers plain contents wherever they come from, until the preview closes or
  moves on. It gets a reader of what plugins made from the file (`derived`, [Derived files](../Derived/Derived.md))
  too: `has(kind)` says whether its record names one, so its URL is known at once, and `url(kind, signal)` answers
  the URL an `<img>` loads it from, waiting while one its record does not name is made, or null when there is
  none, as of an encrypted file. One its record names may have gone since: when its URL does not load,
  `forget(kind)` has `url` ask the server, and wait. `loadDerived(reader, kind, load, signal)` does that: it hands
  the URL to `load`, and once more after `forget` when that fails. `loadImage(image, url, signal)` is such a `load`
  for an `<img>`: it loads and decodes the image, so it is drawn whole at once.
  `neighbours` are the files the arrow keys move to, the one before it and the one after, each with such a
  reader, to load ahead what they will show: only files a viewer takes, none in the trash or of a file
  previewed alone, and none after the last of those fetched so far.
  A plugin's beats a built-in one for the same type; between two plugins the first id does.
  `unavailable` says why this browser cannot show the files, and the next viewer that takes them does.
  The preview is the whole screen, with its controls floating over the file in bars at the top and, on a
  phone, the bottom, that fade when nothing happens. A viewer is drawn under them, edge to edge, unless it is
  `clearOfBars`, for controls of its own they would cover, as a PDF's toolbar: then it is drawn between them.
  The component also gets `Fallback`, a component without props, for a file it finds it has nothing to show of, or
  nothing better than the next viewer: it shows the file in the viewer that would show it were there not this one,
  by the order above and passing over those that are unavailable, or, when none is left, says why the first that is
  unavailable cannot, or that no other viewer shows the file.
  `useZoom` zooms and moves an element of the plugin's as the app's image viewer does, with the wheel, a pinch, a
  click or a double tap, and a drag: it gives `frame`, to spread on a `div` that fills the space and centres what it
  holds, and `zoomed`, the ref and transform of the element in it. A click with the mouse on that element zooms in
  where it is, and once zoomed in one anywhere zooms out; one beside it, fitted, closes the preview, as its Close
  does. A tap shows or hides the preview's controls instead. Zoomed in, a finger's drag moves the element, and one
  that goes on as far as a swipe past where the element stops at its edge swipes.
  A finger's swipe over a viewer moves to the file beside when it goes across and closes the preview when it goes
  down, unless it starts on what moves with a drag of its own that way, which it is left to: a player, a field, a
  row that scrolls sideways (across), a pane scrolled down from its top (down), what `useZoom` zooms while zoomed
  in, and an element of the plugin's given `{ [DRAGS_ITSELF]: true }`, or anything in one (across and down).
  `mediaTypeOf(contentType)` is a file's type as viewers' `mediaTypes` are matched against it: lowercase, without
  parameters such as `; charset=utf-8`.
- **File actions** are items in the menus of live files: `{ id, label, icon?, showsFor, run }`.
  `showsFor` is asked as a menu is drawn. `run` gets the shelf, the file, its content reader and
  `showDialog`, which shows a dialog of the plugin's until it calls the `close` it is given. The contents
  can be read until `run` is done and no dialog is open; an encrypted file's ask for the password as they
  are first read. What `run` throws is shown to the person, but for an `AbortError`, which says they gave
  up, as at that prompt. As the person leaves the shelf's files, its dialogs close and a read fails with
  an `AbortError`; `run` is not stopped, but a dialog it shows after that never appears.
- **Folder types** are folders whose names end in a dot and a suffix: `{ suffix, label, icon, component
  }`. `suffix` is at least two lowercase letters and digits, matched whatever their case; a name that is
  only the suffix (`.album`) is a plain folder, and the first plugin by id has a suffix two declare. They
  stay ordinary folders to everything else, and have a listing in the web UI too, which is shown instead
  of a viewer that throws, and offers it again; it is tried again too as the folder is next opened. An
  encrypted one's viewer shows once the folder is unlocked. The component, which may be a `lazy` one, as
  the icon may, has everything under the breadcrumbs, in a flex column that fills what the page, at least
  as tall as the window, has left there. It has no padding; `PageLayout` gives it the app's. It is
  unmounted, losing what it holds unsaved, as the person leaves the folder or shows its contents, as the
  folder is deleted, moved or renamed, as its encryption or its password changes, or another takes its
  place. It gets the shelf, the folder, whether the person may write, and the folder's contents
  (`FolderContents`), the same for as long as it is mounted: listing what is below it, storing and moving
  files to paths in it, and, of the files it lists, deleting one, reading one, and opening one in the
  preview, alone, or in a tab when nothing previews it. A read asks for no password, and of a file that is
  locked fails with a `LockedError`, until the viewer asks to unlock it. A query whose key starts with
  `queryKey` is asked again as anything on the shelf changes; **what is read of a file must not go into a
  query**, which keeps its answer after the viewer is gone, but through `useLoaded`. A file dropped on the
  viewer is refused unless it calls `preventDefault` on the `dragover` and the `drop`.
  `byteshelf.client/e2e/albumPlugin/ui/plugin.js` is a small one, whole.

A file a folder's viewer stores over moves to the trash. What is stored in an encrypted folder is encrypted
with its password, and over a file with a password of its own with that one, which is asked for when none
kept opens it; with none given, storing fails with an `AbortError`. It fails, sending nothing, once the
folder is no longer at its path, or its encryption was removed or is now another folder's: what was to be
encrypted there is never sent plain. Anything else is stored plain, so **what was made from a file with a
password of its own must not be stored in a plain folder**, but over a file with that password: a smaller
copy of an encrypted picture would be a plain one. Nor does encrypting a file touch the copies made of it
before; encrypting the folder does.

A plugin's module lives as long as the page does, which starts anew as the session ends: what it keeps
in memory is never there for the next person to sign in. What it keeps in the browser's storage is,
unless it removes it.

In a viewer, a folder type's too, or a dialog of the plugin, `usePluginDocuments(file)` reads what it keeps
about a live file: its document about the contents (`contents`, null for an encrypted file), and the one
about the file (`file`) with its version, whose changes it follows. `useSetPluginDocument(file)` writes the
latter as `{ document, expectedVersion }`: the whole of it, null to remove it, over the `fileVersion` the
plugin read, null when there was none; another version there by now is an `ApiError` of status 409, and the
documents are read again ([Metadata](../Metadata/Metadata.md)). Both know the plugin by what draws them, and
throw elsewhere. **What was read from a file's plaintext must not go into the document about the file**,
which is kept when the file is encrypted. For a file in the trash both fail.

Ids are apart within a plugin's viewers, and within its file actions, as suffixes are within its folder
types. Every type is in `byteshelf.client/src/plugin/index.ts`.

## Versions

`api` is one integer for all that plugins may use: the plugin folder, the assemblies they get from the
server (`ByteShelf.Plugins`, `ByteShelfCommon` and the framework), and in the web UI the shared modules
and `@byteshelf/plugin`, the types it exports, such as `ShelfFile`, included. It is raised on a breaking
change to a released contract, one that a published release of the server holds; an addition, or a
change to what no release holds, leaves it as it is. The server's HTTP API, which a plugin reaches with
`authorizedFetch`, is no part of it but for those types: it changes with the server's releases.

`ByteShelf.Plugins`' assembly version is the api and a minor, which an addition to a released contract
raises and a new api starts at 0; `ByteShelfCommon`'s is its package's without the patch, so a plugin
built against a later patch of it than the server's loads. A server without an addition to either refuses
a plugin built against it.

An addition to the web UI's part raises nothing, and `--check-plugin`, which runs no module, sees nothing
of it: on a server without it, a plugin that imports a new export fails as it loads, which the Settings
page says, and one that uses a new member of a type there (`ViewerProps`, `ContentReader`, `FolderContents`) loads
and fails in the viewer or action that does. A part of `definePlugin` that server does not know is not
missed by anything: folders of a type show as plain ones. Its server part runs either way.

How plugins are built, and their styles kept apart from the app's, is in
[plugins/README.md](../../plugins/README.md).
