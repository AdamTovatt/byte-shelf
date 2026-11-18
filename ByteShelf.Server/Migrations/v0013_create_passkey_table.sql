-- Passkeys a user signs in with, many per user, beside their password. Every column up to created_at
-- is the passkey as the change log carries it to replicas. sign_count and last_used_at belong to the
-- server that saw the sign-in, as sessions do: they are not logged, and a replica keeps its own. The
-- user handle a passkey is made with, and must present, is the bytes of user_id: it has no column.
CREATE TABLE passkey (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES user_account(id) ON DELETE CASCADE,
    -- base64url, as the browser reports it; a sign-in looks the passkey up by it across all users.
    credential_id TEXT NOT NULL,
    -- The COSE public key, base64url.
    public_key TEXT NOT NULL,
    -- The transports registration reported, as a JSON array of strings; null when none were.
    transports TEXT,
    name TEXT NOT NULL,
    created_at TEXT NOT NULL,
    sign_count INTEGER NOT NULL DEFAULT 0,
    last_used_at TEXT
);

CREATE UNIQUE INDEX idx_passkey_credential_id ON passkey (credential_id);
CREATE INDEX idx_passkey_user_id ON passkey (user_id);
