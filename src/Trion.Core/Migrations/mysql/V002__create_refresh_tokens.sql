-- V002 — JWT refresh token store (MySQL / MariaDB)
-- Holds issued refresh tokens for the Trion web panel.
-- Tokens are identified by their SHA-256 hash; the raw token is never stored.

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id         VARCHAR(64)  NOT NULL PRIMARY KEY,
    username   VARCHAR(255) NOT NULL,
    user_hash  VARCHAR(64)  NOT NULL,
    token_hash VARCHAR(64)  NOT NULL,
    issued_at  VARCHAR(64)  NOT NULL,
    expires_at VARCHAR(64)  NOT NULL,
    revoked    TINYINT(1)   NOT NULL DEFAULT 0,
    revoked_at VARCHAR(64)  NULL,
    issuing_ip VARCHAR(64)  NOT NULL,
    INDEX idx_token_hash (token_hash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
