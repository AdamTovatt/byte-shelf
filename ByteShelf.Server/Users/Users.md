# Users

People who sign in: email and password, refresh tokens, roles, the first admin, password change,
and password reset. Every change to a user and to their logins, password hash included, is in the
change log (see [ChangeLog.md](../ChangeLog/ChangeLog.md)).

## Identity (two tables)

- `user_account`, who the user is: a unique **email** (case-insensitive; the sign-in name and
  reset channel), a **display name**, and `roles`, a JSON array of strings in a `TEXT` column.
- `user_login`, how they prove it: `provider` + `provider_user_id` + `credential`. The password
  hash lives on the `provider = 'local'` row, so another sign-in provider later is another row.

## Roles

`admin` is the only role. An admin creates shelves and users and reaches every shelf; anyone else
uses the shelves they have been given access to (see [Access.md](../Access/Access.md)). The last
admin cannot lose the role or be deleted.
A role change reaches a signed-in user at their next request. Every request with an access token
reads the user from the database as the token is validated (`CurrentUserTokenValidation`): the
request gets the roles the user has now, whatever the token was minted with, and a token whose user
has been deleted no longer authenticates (401). So a demoted or deleted admin loses admin at once.
`CurrentRolesRefreshResolver` also puts the current roles on every refreshed token.

## Endpoints

Requests and answers are ByteShelfCommon types (`UserInfo`, `CreateUserRequest`, `SignInRequest`,
`SessionTokens` and the rest), and the rules for an email, display name, password and roles live
there too (`UserFields`, `PasswordPolicy`, `UserRoles`), so the in-memory client refuses what the
server refuses. The HTTP client sends them unchecked and reports the server's answer.

| Method and route | Who | What it does |
|---|---|---|
| `POST /api/auth/login` | anyone | Signs in with `username` (the email) and `password`; answers a 1-hour JWT and a refresh token. |
| `POST /api/auth/refresh`, `/api/auth/logout` | anyone | Rotates a refresh token; ends a session. |
| `GET /api/users/me` | signed in | The signed-in user. |
| `PUT /api/users/me/password` | signed in | Changes the password and ends the user's other sessions; the one making the change carries on. A wrong current one is 401 `invalid_credentials` without a `WWW-Authenticate` challenge, which tells a client its token is fine and must not be renewed and resent. |
| `GET /api/users` | admin | Every user. |
| `POST /api/users` | admin | Creates a user. |
| `PUT /api/users/{id}/roles` | admin | Replaces a user's roles; 409 for the last admin. |
| `DELETE /api/users/{id}` | admin | Deletes a user with their logins, reset tokens, shelf access and sessions; 409 for the last admin. The access token they still hold is refused from then on. |
| `POST /api/password-reset/request` | anyone | Queues a reset link; 204 whether or not the email has an account. |
| `POST /api/password-reset/complete` | anyone | Sets a new password with a link's token and ends every session; 400 for a bad token. |

## Sign-in

EasyReasy.Auth handles the endpoints; `AuthRequestValidationService` checks the credentials and
issues the tokens, with expiry from the injected `TimeProvider`. A sign-in with an unknown email
still runs the password hasher, so its timing does not tell which emails have accounts. Refresh
tokens rotate within a family, one family per device, and reuse of a consumed token invalidates the
family. A token stored into a family that is already invalidated is stored invalidated, so a
refresh racing a password reset cannot keep a session alive. Sign-in and renewal share a strict rate
limit per address ([rate limits](../../DEPLOYMENT.md#rate-limits)).

Every session is stamped with the password it was signed in with (`CredentialStamp`): its refresh
token stores the SHA-256 of the password hash as it was when the password was checked. A refresh is
refused once the user's password hash is another, so a password change or reset ends every session
that predates it, set to the same password again included. A sign-in that checked the password just
before a reset committed is refused rather than handed out. Sessions signed in before stamps existed
have none, and end at their next refresh.

## Password reset

A request stores a single-use, 1-hour, SHA-256-hashed token and hands the link to
`ResetLinkOutbox`; `ResetLinkSender` sends it in the background through `IEmailSender`, so the
response takes as long whether or not the email has an account. Asking again leaves earlier links
working until one of them is used, which invalidates the rest and ends every session.
`APP_BASE_URL` is where links point; it is required outside Development.

No mail provider exists yet, so reset emails are not delivered: outside Development the link is
neither sent nor logged, and a forgotten password cannot be reset ([Email](../Email/Email.md)). In
Development the link is written to the log. The outbox holds at most 100 links waiting to be sent:
when it is full a new link is dropped (with a warning in the log) and the user has to ask again.
It lives in memory, so links still waiting are lost on a restart, and a send that fails is logged
and not tried again.

Changing your own password ends your other sessions; the session you changed it from carries on.
The access token names its session, so the server moves that one session onto the new password.

## The first admin

`InitialAccountSeeder` creates an admin from `INITIAL_ADMIN_EMAIL` and `INITIAL_ADMIN_PASSWORD`
(display name from `INITIAL_ADMIN_DISPLAY_NAME`, else the email's local part) while there are no
users, and does nothing once there are, so both can be removed after the first start.

## Cleanup

`ExpiredTokenCleanupWorker` deletes expired refresh and reset tokens at startup and then daily,
recorded as `users.expired_token_cleanup`.

## On a replica

A replica's users are its primary's, taken whole when it pairs (the ones it had are removed, and their
sessions end), so people sign in there with their accounts on the primary. Signing in, renewing and
signing out work as anywhere; every change to accounts, passwords and roles is refused with 409
`read_only_replica` ([Replication](../Replication/Replication.md)). Sessions are the replica's own: a
session on the primary does not carry over. A password changed or reset on the primary reaches the
replica with the login row, and the replica's sessions signed in with the old password end at their
next refresh, as on the primary.
