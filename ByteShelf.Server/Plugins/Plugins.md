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

Other fields are ignored.

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
are a contract with updaemon, which runs it before switching versions, as the server's user, with
only `PATH` and `HOME`, from any folder. Only the server itself loads plugins; `--schema-only`,
`--migrate-only` and the root commands do not.

## The server assembly

It is written against `ByteShelf.Plugins` and holds one public class implementing
`IByteShelfPlugin`, which registers what it adds through an `IPluginBuilder`: its services, in a
copy of the server's collection, taken over only once the plugin can be loaded, so one that fails
leaves nothing registered and no controller behind. The exception is what a registration does to an
object already registered: `AddHttpClient<T>` keeps the names of typed clients in one, so a typed
client needs a class name no other plugin's has, and a failed plugin's stays taken until the server
restarts. A copy rather than an empty collection, because the framework's own registrations
(`AddHttpClient`, `AddLogging`) add to what is already there, and a second set beside the server's
breaks or resets it. There is no access to shelves or files in the contract.

What the server shares with a plugin must be no older than the plugin was built against, or it is
refused before any of it runs, as one for another api is. That goes for its server assembly and for
those in its folder that the assembly's references lead to, not for one it loads by name.

Each plugin has an `AssemblyLoadContext` of its own (`PluginLoadContext`): its packages and native
libraries come from its folder, so it may use another version of a package than the server does.
`ByteShelf.Plugins`, `ByteShelfCommon` and the framework always come from the server
(`HostAssemblies`), whatever the plugin brings, so the two agree on their types and rules. A plugin
references the first two without copying them (`Private="false" ExcludeAssets="runtime"`) and sets
`EnableDynamicLoading`, as `ByteShelf.Server.Tests.Plugin` does.

Its controllers answer under `/api/plugins/{id}/` whatever routes they declare
(`PluginRouteProvider`), so none can take an address of the server's, and are left out of the
OpenAPI document. Controllers are the only endpoints the contract has: one a plugin maps some other
way, such as in middleware of its own, is its full trust. They are guarded by `[Authorize]`, which
takes a signed-in user and no shelf key, are open to anyone without it, and refuse writes on a
replica.

## The web UI part

`GET /api/plugins` lists the loaded plugins with the addresses of their module and stylesheet, for
any signed-in user or shelf key, and for an admin those skipped. The `ui` folder, and nothing else
of the plugin, is served at `/plugins/{id}/{hash}/...` (`UsePluginAssets`), to anyone, as a browser
loads modules without the session's token. The hash is of every file in the folder, so each may be
cached for good, and is in the deploy id ([Version](../Version/Version.md)), so open tabs are told
of a new version. It is taken once, at startup, so a plugin's folder must not change under a running
server.

The app asks for the list once someone is signed in and imports each plugin's module
(`lib/plugins/pluginHost.ts`), showing it once its stylesheet is there too. One whose module or
stylesheet fails, whose default export is not made with `definePlugin`, or whose viewers or file
actions lack what each must have (`partsOf` in `lib/plugins/pluginDefinition.ts`), adds nothing to
the page and leaves no styles in it; the others are not affected, nor is its own server part, and
admins see the reason on the Settings page. `index.html`'s import map
(`byteshelf.client/build/sharedModules.ts`) has the browser resolve the modules plugins share with
the app, so there is one React and one of each context.

A module's default export is `definePlugin({ viewers, fileActions })`, from `@byteshelf/plugin`:

- **Viewers** show files in the preview, as the app's own do: `{ id, mediaTypes, extensions, component,
  unavailable? }`. `extensions` count only for files stored without a type
  (`application/octet-stream`). The component, which may be a `lazy` one, gets the file's record and a
  content reader, which answers plain contents wherever they come from, until the preview closes or
  moves on. A plugin's beats a built-in one for the same type; between two plugins the first id does.
  `unavailable` says why this browser cannot show the files, and the next viewer that takes them does.
- **File actions** are items in the menus of live files: `{ id, label, icon?, showsFor, run }`.
  `showsFor` is asked as a menu is drawn. `run` gets the shelf, the file, its content reader and
  `showDialog`, which shows a dialog of the plugin's until it calls the `close` it is given. The contents
  can be read until `run` is done and no dialog is open; an encrypted file's ask for the password as they
  are first read. What `run` throws is shown to the person, but for an `AbortError`, which says they gave
  up, as at that prompt. As the person leaves the shelf's files, its dialogs close and a read fails with
  an `AbortError`; `run` is not stopped, but a dialog it shows after that never appears.

A plugin's module lives as long as the page does, which starts anew as the session ends: what it keeps
in memory is never there for the next person to sign in. What it keeps in the browser's storage is,
unless it removes it.

Ids are apart within a plugin's viewers, and within its file actions. Every type is in
`byteshelf.client/src/plugin/index.ts`.

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
page says, and one that uses a new member of a type there (`ContentReader`, `FileActionContext`) loads
and fails in the viewer or action that does. Its server part runs either way.

How plugins are built, and their styles kept apart from the app's, is in
[plugins/README.md](../../plugins/README.md).
