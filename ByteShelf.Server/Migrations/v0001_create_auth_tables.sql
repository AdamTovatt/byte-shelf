-- Identity is two-table: user_account holds who the user is (identity + roles), user_login holds
-- how they prove it (provider + credential). Another login provider later is an additive row.
-- Ids are lowercase GUID text and timestamps StoredInstant text, both written by the server.

CREATE TABLE user_account (
    id TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    display_name TEXT NOT NULL,
    roles TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

-- Email is the login identifier and password-reset channel; uniqueness is case-insensitive.
CREATE UNIQUE INDEX idx_user_account_email_lower ON user_account (LOWER(email));

CREATE TABLE user_login (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES user_account(id) ON DELETE CASCADE,
    provider TEXT NOT NULL,
    provider_user_id TEXT NOT NULL,
    credential TEXT,
    created_at TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_user_login_provider_user ON user_login (provider, provider_user_id);
-- One login row per provider per account: credential lookups and updates address the row by
-- (user_id, provider), so a duplicate pair would make those queries ambiguous.
CREATE UNIQUE INDEX idx_user_login_user_provider ON user_login (user_id, provider);

-- Refresh tokens with token-family rotation (EasyReasy.Auth): reuse of a consumed token
-- invalidates the whole family. Multi-session: one family per device.
CREATE TABLE refresh_token (
    token_hash TEXT PRIMARY KEY,
    subject TEXT NOT NULL,
    auth_type TEXT NOT NULL,
    family_id TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    consumed_at TEXT,
    is_invalidated INTEGER NOT NULL DEFAULT 0,
    serialized_claims TEXT,
    serialized_roles TEXT
);

CREATE INDEX idx_refresh_token_family_id ON refresh_token (family_id);
CREATE INDEX idx_refresh_token_subject ON refresh_token (subject);
CREATE INDEX idx_refresh_token_expires_at ON refresh_token (expires_at);

-- Single-use, time-bounded password-reset tokens, stored hashed.
CREATE TABLE password_reset_token (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES user_account(id) ON DELETE CASCADE,
    token_hash TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    used_at TEXT
);

CREATE UNIQUE INDEX idx_password_reset_token_token_hash ON password_reset_token (token_hash);
CREATE INDEX idx_password_reset_token_user_id ON password_reset_token (user_id);
CREATE INDEX idx_password_reset_token_expires_at ON password_reset_token (expires_at);
