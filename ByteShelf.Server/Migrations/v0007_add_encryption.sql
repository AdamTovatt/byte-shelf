-- Password encryption happens in the clients; the server only records what is encrypted.
--
-- A file whose contents are an age file encrypted with a password. The server never sees the
-- password or the plaintext; the flag tells the clients to ask for the password before opening it.
-- A resumable upload carries the flag until it finishes.
ALTER TABLE file ADD COLUMN encrypted INTEGER NOT NULL DEFAULT 0 CHECK (encrypted IN (0, 1));

ALTER TABLE upload ADD COLUMN encrypted INTEGER NOT NULL DEFAULT 0 CHECK (encrypted IN (0, 1));

-- A directory encrypted with a password: from then on every file stored under it, at any depth, is
-- encrypted with that password. The check is a small age file of a known text, encrypted with the
-- same password, so a client can tell a wrong password before it encrypts anything with it; it
-- reveals nothing the encrypted files themselves do not. NULL is a directory that is not encrypted.
-- 1024 bytes is FileEncryption.MaxCheckBytes.
ALTER TABLE directory ADD COLUMN encryption_check BLOB CHECK (encryption_check IS NULL OR length(encryption_check) <= 1024);
