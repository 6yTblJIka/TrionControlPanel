-- V002 — JWT refresh token store
-- Holds issued refresh tokens for the Trion web panel.
-- Tokens are identified by their SHA-256 hash; the raw token is never stored.

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id         TEXT    NOT NULL PRIMARY KEY,   -- UUID
    username   TEXT    NOT NULL,
    user_hash  TEXT    NOT NULL,               -- hash of username for quick lookup
    token_hash TEXT    NOT NULL,               -- SHA-256 of the raw refresh token
    issued_at  TEXT    NOT NULL,               -- ISO-8601 UTC
    expires_at TEXT    NOT NULL,               -- ISO-8601 UTC
    revoked    INTEGER NOT NULL DEFAULT 0,     -- 0 = active, 1 = revoked
    revoked_at TEXT,                           -- ISO-8601 UTC, NULL if not revoked
    issuing_ip TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_token_hash
    ON refresh_tokens (token_hash);
