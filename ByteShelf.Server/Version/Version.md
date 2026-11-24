# Version

Deploy detection: `GET /api/version/current` returns an id for the running deploy, a hash of the
server assembly's `ModuleVersionId`, the web UI's `index.html` and the plugins' web UI files
([Plugins](../Plugins/Plugins.md)). The build is deterministic, so the id changes when the server's
code or the web UI, a plugin's part in it included, changes and stays put otherwise; a plugin's server assembly is not in it. The web UI
reads it at start, polls the endpoint, and offers a reload when the value changes;
`StaticFileCachePolicy` serves `index.html` uncached so the reload gets the new bundle.

That endpoint needs no sign-in (the sign-in page polls it too, and the id says only that a deploy
happened, not which release it was).

Release: `GET /api/version/release` returns the release the server runs, the csproj's `Version` that
a release's tag must name (`ReleaseVersion.Of`, the assembly's informational version without the
commit the build appends), with the same deploy id. It is open to any signed-in user or shelf key
(`ShelfCallerPolicy`) and answers 401 otherwise. The release is not public because an anonymous
caller who learned it could match the server against the known bugs of that release. `/mcp` names
it in its instructions, since every caller there holds a key.

Neither endpoint has a repository.
