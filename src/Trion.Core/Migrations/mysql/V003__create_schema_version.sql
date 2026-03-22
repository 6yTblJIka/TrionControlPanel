-- V003 — Schema version tracker (MySQL / MariaDB)
-- Inserted automatically by the migration runner before any migration runs.
-- Kept here for reference; do not run manually.

CREATE TABLE IF NOT EXISTS schema_version (
    version INT NOT NULL
);

INSERT INTO schema_version (version)
SELECT 0 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM schema_version);
