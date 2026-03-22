-- V003 — Schema version tracker
-- Inserted automatically by the migration runner before any migration runs.
-- Kept here for reference; do not run manually.

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER NOT NULL
);

INSERT INTO schema_version (version)
SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM schema_version);
