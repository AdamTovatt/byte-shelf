# Email

Outbound email behind one seam, `IEmailSender`. `EmailOptions.FromEnvironment` reads the settings
once at startup into exactly one of three kinds, and `AddEmailServices` registers the sender for
that kind:

1. `PostmarkEmail`, when `POSTMARK_API_KEY` is set: `PostmarkEmailSender` sends through Postmark's
   REST API from `EMAIL_FROM_ADDRESS` and `EMAIL_FROM_NAME` (`ByteShelf` by default). A token
   without a sender address stops the server at startup, since Postmark sends only from a confirmed
   sender. Every message turns Postmark's open and link tracking off explicitly, so a reset link
   reaches the user as written, whatever the Postmark server's own settings.
2. `DevMailEmail`, else when `REMOTE_CLAUDE_DEV_MAIL_DIR` is set: `DevMailEmailSender` sends nothing
   and writes each message into that folder as one JSON file for remote-claude's dev server pane
   (`DevMailFolder`; the contract is remote-claude's: `version`, `sentAt`, `to`, `fromEmail`,
   `fromName`, `subject`, `html`, `text`, `tag`). The file names the sender from
   `EMAIL_FROM_ADDRESS` and `EMAIL_FROM_NAME` when they are set, else `byteshelf@localhost` and
   `ByteShelf`. Each file is written through a `.tmp` sibling and renamed into place, owner-only
   (0600), since it carries reset links; a folder the server creates is 0700, and one that exists
   already keeps its own permissions. A write that fails is a failed send.
3. `LogOnlyEmail`, when neither is set: `LoggingEmailSender` delivers nothing. In Development it
   writes each message to the log, links included, so a developer can follow a password-reset link.
   Anywhere else it logs only that an email was asked for and not sent, never its body: a reset link
   in a log would let whoever reads the log into the account. So outside Development nobody
   receives a reset link, and a forgotten password can be reset only with the server's
   `--reset-password` ([Users](../Users/Users.md#from-the-servers-command-line)).

Values are trimmed, and blank ones count as unset. While mail is not sent through Postmark,
`EmailSenderStartupWarning` logs at startup where it goes instead. Tests pass `EmailOptions.None`,
so a token in the environment of the machine running them is never used.

`SendAsync` answers an `EmailSendResult` (`IsSuccess`, `MessageId`, `ErrorMessage`). A failure,
Postmark's refusal or timeout and a dev mail write included, is answered as `IsSuccess` false and
logged; only the caller's own cancellation is thrown. Nothing retries a failed send.
