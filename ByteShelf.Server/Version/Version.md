# Version

Deploy detection: `GET /api/version/current` returns an id for the running deploy, a hash of the
server assembly's `ModuleVersionId` and the web UI's `index.html`. The build is deterministic, so
the id changes when the server's code or the web UI changes and stays put otherwise. The web UI
reads it at start, polls the endpoint, and offers a reload when the value changes;
`StaticFileCachePolicy` serves `index.html` uncached so the reload gets the new bundle.

The endpoint needs no sign-in (the sign-in page polls it too, and the value is not sensitive) and
has no repository.
