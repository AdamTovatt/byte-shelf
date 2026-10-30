# Email

Outbound email behind one seam, `IEmailSender`. Its only sender so far is `LoggingEmailSender`,
which writes each message, links included, to the log instead of sending it, and logs a warning at
startup saying so. Password-reset links therefore appear in the log until a real sender (SMTP,
Postmark) is registered in its place.

`SendAsync` answers an `EmailSendResult` (`IsSuccess`, `MessageId`, `ErrorMessage`,
`IsTransient`), so a sender can say whether a failure is worth retrying.
