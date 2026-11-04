-- When a password was last set without the current one (a reset link, or the server's
-- --reset-password): every access token issued before then is refused, as the refresh tokens of the
-- sessions it ended are. NULL until the first such reset.
ALTER TABLE user_account ADD COLUMN sessions_ended_at TEXT;
