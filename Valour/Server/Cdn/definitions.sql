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

CREATE TABLE embeds (
    id TEXT NOT NULL PRIMARY KEY,
    type TEXT,
    version TEXT,
    width INTEGER,
    height INTEGER,
    title TEXT,
    url TEXT,
    author_name TEXT,
    author_url TEXT,
    provider_name TEXT,
    provider_url TEXT
);  


COMMIT;