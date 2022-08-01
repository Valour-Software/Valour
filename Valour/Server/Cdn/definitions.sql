BEGIN;

CREATE TABLE proxies (
    id TEXT NOT NULL PRIMARY KEY,
    origin TEXT NOT NULL,
    mime_type TEXT NOT NULL
);

CREATE TABLE bucket_items (
    id TEXT NOT NULL PRIMARY KEY,
    hash TEXT NOT NULL,
    mime_type TEXT NOT NULL,
    user_id BIGINT NOT NULL,
    category TEXT NOT NULL
);

COMMIT;