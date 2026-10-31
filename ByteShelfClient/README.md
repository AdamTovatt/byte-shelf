# ByteShelfClient

The client for ByteShelf, a self-hosted file store. One interface over a server's shelves, files and
users, an HTTP implementation, and an in-memory one so an app's tests need no running server.
Targets net8.0 and is AOT-compatible.

## Use

```csharp
HttpClient http = new HttpClient { BaseAddress = new Uri("https://files.example.com/") };

// A program uses one shelf with a key an admin created for it.
IByteShelfClient client = new ByteShelfHttpClient(http, new ShelfKeyCredentials(secret));
IShelfClient receipts = client.Shelf("receipts");

ShelfFile stored = await receipts.UploadAsync("/2026/march.pdf", fileStream, new UploadOptions { ContentType = "application/pdf" });
await using Stream content = await receipts.OpenReadAsync(stored.Id);
```

A person signs in instead, and the session renews itself:

```csharp
using UserSession session = await UserSession.SignInAsync(http, email, password);
session.Renewed += (_, _) => Save(session.RefreshToken);   // to resume later with UserSession.Resume(http, saved)
IByteShelfClient client = new ByteShelfHttpClient(http, session);
// ...
await session.SignOutAsync();   // ends it on the server; disposing alone leaves it resumable
```

`IByteShelfClient` lists and creates shelves and, with a `UserSession`, the signed-in user and
(for admins) the other users; a shelf key cannot reach users. `IShelfClient` works on one shelf:
list, upload, open, create directories, move, delete, the trash, and for admins the shelf's settings,
grants and keys. Files and directories are named by path or by id; both convert to an
`EntryReference`.

## Uploads

The server needs a file's size before it accepts any bytes. A stream that can seek is sent from its
position to its end; any other stream is first copied to a temporary file. Files up to
`ByteShelfHttpClientOptions.SingleRequestMaxBytes` (32 MiB) go in one request, larger ones as a
resumable upload in chunks of `ChunkBytes` (8 MiB). A failed or cancelled chunk aborts the upload,
so its reservation against the quota is released at once; the abort gets 10 seconds, and the
failure it follows is the one thrown.

## Errors

A path or content type the rules in ByteShelfCommon refuse throws `ArgumentException` before
anything is sent. Anything the server refuses throws `ByteShelfException`, whose `Code` is one of
`ErrorCodes`: `not_found`, `conflict`, `permission_denied`, `quota_exceeded` and so on. A request that
never reached the server throws what `HttpClient` throws.

## Sessions

A `UserSession` belongs to the server its `HttpClient` points at. It renews its access token a
minute before it runs out, and once more if the server challenges it. A refresh token works once,
so renewals run one at a time, a renewal already sent finishes even when the request that started
it is cancelled, and `Renewed` reports each new token. A renewal the server refuses throws its
error: `unauthenticated` when the session has ended (sign in again), anything else leaves the
session as it was. A streamed upload cannot be sent twice, so it relies on the renewal ahead of
time. Dispose a session when done with it; the `HttpClient` stays the caller's.

## In tests

`InMemoryByteShelfClient` keeps everything in memory and follows the server's rules for paths,
names, conflicts, quotas, the trash and users. It acts as a signed-in admin and does not model
access, so nothing is refused for lack of permission. The admin is `CurrentUser`; pass `email` and
`password` to choose who it is (a test of `ChangePasswordAsync` needs the password), a
`TimeProvider` to control timestamps and when the trash is purged, and `restoreWindow` to change
the 30 days.

```csharp
InMemoryByteShelfClient client = new InMemoryByteShelfClient(email: "me@example.com", password: "a-long-password");
await client.CreateShelfAsync("receipts");
await client.Shelf("receipts").SetGrantAsync(client.CurrentUser.Id, ShelfAccessPermission.Read);
```

The server's test suite runs the same contract tests against both clients.
