-- V001 — Settings key-value store (MySQL / MariaDB)
-- Holds all runtime configuration, setup flags, lockout state, and owner account data.

CREATE TABLE IF NOT EXISTS settings (
    `key`      VARCHAR(255) NOT NULL PRIMARY KEY,
    `value`    LONGTEXT     NOT NULL,
    updated_at VARCHAR(64)  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
