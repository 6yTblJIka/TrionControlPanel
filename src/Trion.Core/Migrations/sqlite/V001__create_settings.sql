-- V001 — Settings key-value store
-- Holds all runtime configuration, setup flags, lockout state, and owner account data.

CREATE TABLE IF NOT EXISTS settings (
    key        TEXT NOT NULL PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
