# Email

Outbound email behind one seam, `IEmailSender`. No real sender (SMTP, Postmark) exists yet: the one
registered is `LoggingEmailSender`, which delivers nothing and logs a warning at startup saying so.
In Development it writes each message to the log, links included, so a developer can follow a
password-reset link. Anywhere else it logs only that an email was asked for and not sent, never its
body: a reset link in a log would let whoever reads the log into the account. So outside Development
nobody receives a reset link, and a forgotten password cannot be reset, until a real sender is
registered in its place.

`SendAsync` answers an `EmailSendResult` (`IsSuccess`, `MessageId`, `ErrorMessage`). Nothing retries
a failed send.
