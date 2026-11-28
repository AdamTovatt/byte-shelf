# Plugins made here

Each folder is one plugin, released with the server as `byteshelf-<id>.zip` and at its version. What a
plugin is, and how the server and the web UI load it, is in
[Plugins](../ByteShelf.Server/Plugins/Plugins.md). Nothing is published to build one elsewhere: any other
plugin is built from a checkout of this repository too, its web UI part as these are and its server
assembly as `images`' is, against the two projects that one references.

- `markdown`: shows `text/markdown`, and untyped files named `.md`, as they read or as their source. No
  HTML in one is shown and no image loaded (its alt text stands in); web links open in a new tab, and any
  other is plain text. A long one's first 128 KiB show as they read, and 1 MiB of its source.
- `images`: makes thumbnails, previews and what agents see of images, HEIC and cameras' raws included, and reads
  their size; its viewer shows an image at once and sharp once the whole of it is there ([README](images/README.md)).
- `video`: makes a poster, a storyboard and a copy that streams of each video, with an ffmpeg of its own, and reads
  how long it plays, its size and whether it streams well as it is; its player plays the stream when there is one,
  with the storyboard over its seek bar ([README](video/README.md)).

```
plugins/<id>/
  plugin.json         id (the folder's name), name, api and, if any, serverAssembly and fields; the build adds the version
  package.json        "type": "module", its own packages, and the scripts below, the second if it has web UI tests
  package-lock.json   npm install writes it; npm ci, which the release runs, needs it
  tsconfig.json       extends ../tsconfig.base.json, includes src
  src/plugin.tsx      its module: the default export is a definePlugin(...), of nothing when it has no web UI part
  server/             its server assembly's project, if it has one
  server.tests/       its tests, which `dotnet test ByteShelf.slnx` runs
```

```json
"scripts": {
  "build": "node ../../byteshelf.client/build/buildPlugin.ts",
  "test": "node ../../byteshelf.client/build/testPlugin.ts"
}
```

It takes Node 24, and the .NET SDK for a server assembly. `npm ci` in `byteshelf.client`, whose tools it
uses, and in the plugin, then `npm run build` in the plugin makes `dist/`, a plugin folder: copy or link it into the folder
`BYTESHELF_PLUGINS` names and restart the server. `npm test` runs its tests, with the app's own React
and UI library, and `@byteshelf/plugin/testing`, stand-ins for what the app gives a viewer (`derivedReaderOf`, and
`InPreview`, a preview to draw one in, whose are all keys pressed in the document), which only tests import; `npm run lint` in `byteshelf.client` covers the plugins too.

A new one also needs its line under `extensions.known` in `ByteShelf.Server/updaemon.json`, which a
server test asks for, so that updaemon 1.1 or later installs it by its id alone. Its id may not read as the end of a
server's zip, as `linux-x64` does; the build refuses it. The build publishes
`server/` into `dist` when `plugin.json` names a `serverAssembly`, for no one runtime, so one zip is for every
platform.

- **What it shares with the app** it imports and does not bring: `react`, `react/jsx-runtime`,
  `react-dom`, `@tanstack/react-query`, `react-router-dom` and `@byteshelf/plugin`
  (`byteshelf.client/src/plugin/index.ts`), which is the UI library and what of the app a plugin may
  use. Another module of those packages, or `react-router`, fails the build, since it would bring a
  second copy.
- **Styles** are Tailwind's under a class prefix, the plugin's id and a hyphen (`markdown-flex`,
  `hover:markdown-underline`, `dark:markdown-hidden`), with the app's colours and scales. A utility
  without it, in a class attribute, a call to `cn` or a stylesheet, fails the build: it would change the
  app's own elements, or depend on what the app's stylesheet happens to hold. One kept in a constant
  elsewhere is not seen. Preflight is off; the app's is there.
- **Heavy code** is lazy (`import()`) inside the plugin, since its module is loaded at sign-in: a viewer's
  component may be a `lazy` one, as `markdown`'s is, and so may a folder type's component and icon.
