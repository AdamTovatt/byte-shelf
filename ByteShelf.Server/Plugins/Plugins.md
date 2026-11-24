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
| `id` | Lowercase letters, digits and hyphens. What the plugin is called everywhere. The folder's name means nothing, and of two folders with one id neither is loaded. |
| `name`, `version` | For people to read. |
| `api` | `PluginApi.Version` of the servers it is for, one integer the server raises on any breaking change to what plugins may use. Another number is not loaded. |
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
`IByteShelfPlugin`, which registers services. It gets a copy of the server's service collection,
taken over only once the plugin can be loaded, so one that fails leaves nothing registered and no
controller behind. The exception is what a registration does to an object already registered:
`AddHttpClient<T>` keeps the names of typed clients in one, so a typed client needs a class name no
other plugin's has, and a failed plugin's stays taken until the server restarts. A copy rather than
an empty collection, because the framework's own registrations (`AddHttpClient`, `AddLogging`) add
to what is already there, and a second set beside the server's breaks or resets it. There is no
access to shelves or files in the contract.

Each plugin has an `AssemblyLoadContext` of its own (`PluginLoadContext`): its packages and native
libraries come from its folder, so it may use another version of a package than the server does.
`ByteShelf.Plugins`, `ByteShelfCommon` and the framework always come from the server
(`HostAssemblies`), whatever the plugin brings, since their types cross between the two. So a plugin
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
stylesheet fails, or whose default export is not made with `definePlugin`, adds nothing to the page
and leaves no styles in it; the others are not affected, nor is its own server part, and admins see
the reason on the Settings page. `index.html`'s import map
(`byteshelf.client/build/sharedModules.ts`) has the browser resolve the modules plugins share with
the app, so there is one React and one of each context. How plugins are built, and their styles kept
apart from the app's, is in [plugins/README.md](../../plugins/README.md).
