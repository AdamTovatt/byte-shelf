# Users

People who sign in: email and password, refresh tokens, roles, the first admin, password change,
and password reset. Every change to a user and to their logins, password hash included, is in the
change log (see [ChangeLog.md](../ChangeLog/ChangeLog.md)).

## Identity (two tables)

- `user_account` — who the user is: a unique **email** (case-insensitive; the sign-in name and
  reset channel), a **display name**, and `roles`, a JSON array of strings in a `TEXT` column.
- `user_login` — how they prove it: `provider` + `provider_user_id` + `credential`. The password
  hash lives on the `provider = 'local'` row, so another sign-in provider later is another row.

## Roles

`admin` is the only role. An admin creates shelves and users and reaches every shelf; anyone else
uses the shelves they have been given access to (see [Access.md](../Access/Access.md)). The last
admin cannot lose the role or be deleted.
A role change reaches a signed-in user at their next token refresh (`CurrentRolesRefreshResolver`
puts the current roles on every refreshed token), so within one access-token lifetime.

An access token is not checked against the database, so it keeps its roles until it expires, up to
an hour, even after the user is demoted or deleted. A deleted user's shelf access ends at once,
since grants are looked up on every request, but a deleted or demoted admin keeps full admin power
until then, including creating keys that outlive them. Deleting an admin ends their sessions, so
no refresh extends that hour.

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
| `PUT /api/users/me/password` | signed in | Changes the password. A wrong current one is 401 `invalid_credentials` without a `WWW-Authenticate` challenge, which tells a client its token is fine and must not be renewed and resent. |
| `GET /api/users` | admin | Every user. |
| `POST /api/users` | admin | Creates a user. |
| `PUT /api/users/{id}/roles` | admin | Replaces a user's roles; 409 for the last admin. |
| `DELETE /api/users/{id}` | admin | Deletes a user with their logins, reset tokens, shelf access and sessions; 409 for the last admin. See below for the access token they still hold. |
| `POST /api/password-reset/request` | anyone | Queues a reset link; 204 whether or not the email has an account. |
| `POST /api/password-reset/complete` | anyone | Sets a new password with a link's token and ends every session; 400 for a bad token. |

## Sign-in

EasyReasy.Auth handles the endpoints; `AuthRequestValidationService` checks the credentials and
issues the tokens, with expiry from the injected `TimeProvider`. A sign-in with an unknown email
still runs the password hasher, so its timing does not tell which emails have accounts. Refresh
tokens rotate within a family, one family per device, and reuse of a consumed token invalidates the
family. A token stored into a family that is already invalidated is stored invalidated, so a
refresh racing a password reset cannot keep a session alive.

## Password reset

A request stores a single-use, 1-hour, SHA-256-hashed token and hands the link to
`ResetLinkOutbox`; `ResetLinkSender` sends it in the background through `IEmailSender`, so the
response takes as long whether or not the email has an account. Asking again leaves earlier links
working until one of them is used, which invalidates the rest and ends every session.
`APP_BASE_URL` is where links point; it is required outside Development.

Changing your own password does not end your other sessions.

## The first admin

`InitialAccountSeeder` creates an admin from `INITIAL_ADMIN_EMAIL` and `INITIAL_ADMIN_PASSWORD`
(display name from `INITIAL_ADMIN_DISPLAY_NAME`, else the email's local part) while there are no
users, and does nothing once there are, so both can be removed after the first start.

## Cleanup

`ExpiredTokenCleanupWorker` deletes expired refresh and reset tokens at startup and then daily,
recorded as `users.expired_token_cleanup`.
