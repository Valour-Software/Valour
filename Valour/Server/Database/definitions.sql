BEGIN;

CREATE TABLE IF NOT EXISTS users (
    id BIGINT NOT NULL PRIMARY KEY,
    name VARCHAR(32) NOT NULL,
    time_joined TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    pfp_url TEXT,
    status TEXT,
    bot BOOLEAN NOT NULL DEFAULT false,
    disabled BOOLEAN NOT NULL DEFAULT false,
    valour_staff BOOLEAN NOT NULL DEFAULT false,
    user_state_code INT NOT NULL DEFAULT 0,
    time_last_active TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
);

CREATE TABLE IF NOT EXISTS user_emails (
    email TEXT NOT NULL PRIMARY KEY,
    verified BOOLEAN NOT NULL DEFAULT false,
    user_id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS auth_tokens (
    id VARCHAR(40) NOT NULL PRIMARY KEY,
    app_id VARCHAR(36) NOT NULL,
    user_id BIGINT NOT NULL,
    scope BIGINT NOT NULL,
    time_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    time_expires TIMESTAMP NOT NULL DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '7 days'),

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES Users(id)
);

CREATE TABLE IF NOT EXISTS credentials (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    credential_type VARCHAR(16) NOT NULL,
    identifier VARCHAR(64) NOT NULL,
    secret BYTEA NOT NULL,
    salt BYTEA NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS email_confirm_codes (
    code VARCHAR(36) NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS notification_subscriptions (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    endpoint TEXT NOT NULL,
    not_key  VARCHAR(128) NOT NULL,
    auth VARCHAR(32) NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS oauth_apps (
    id BIGINT NOT NULL PRIMARY KEY,
    secret VARCHAR(44) NOT NULL,
    owner_id BIGINT NOT NULL,
    uses INTEGER NOT NULL DEFAULT 0,
    name VARCHAR(32) NOT NULL,
    image_url TEXT,

    CONSTRAINT fk_owner FOREIGN KEY(owner_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS password_recoveries (
    code VARCHAR(36) NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS planets (
    id BIGINT NOT NULL PRIMARY KEY,
    owner_id BIGINT NOT NULL,
    name VARCHAR(32) NOT NULL,
    icon_url TEXT NOT NULL,
    description TEXT NOT NULL,
    public BOOLEAN NOT NULL DEFAULT true,
    default_role_id BIGINT,
    primary_channel_id BIGINT
);

CREATE TABLE IF NOT EXISTS planet_bans (
    id BIGINT NOT NULL PRIMARY KEY,
    target_id BIGINT NOT NULL,
    planet_id BIGINT NOT NULL,
    issuer_id BIGINT NOT NULL,
    reason TEXT NOT NULL,
    time_created TIMESTAMP NOT NULL,
    time_expires TIMESTAMP,

    CONSTRAINT fk_target FOREIGN KEY(target_id) REFERENCES users(id),
    CONSTRAINT fk_banner FOREIGN KEY(issuer_id) REFERENCES users(id),
    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id)
);

CREATE TABLE IF NOT EXISTS planet_channels (
    id BIGINT NOT NULL PRIMARY KEY,
    name VARCHAR(32) NOT NULL,
    position INT NOT NULL,
    description TEXT NOT NULL,
    planet_id BIGINT NOT NULL,
    parent_id BIGINT,
    inherits_perms BOOLEAN NOT NULL DEFAULT true,

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_parent FOREIGN KEY(parent_id) REFERENCES planet_channels(id)
);

CREATE TABLE IF NOT EXISTS planet_category_channels (
    id BIGINT NOT NULL PRIMARY KEY,

    CONSTRAINT fk_inherit FOREIGN KEY(id) REFERENCES planet_channels(id)
);

CREATE TABLE IF NOT EXISTS planet_chat_channels (
    id BIGINT NOT NULL PRIMARY KEY,
    message_count BIGINT NOT NULL DEFAULT 0,

    CONSTRAINT fk_inherit FOREIGN KEY(id) REFERENCES planet_channels(id)
);

CREATE TABLE IF NOT EXISTS planet_invites (
    id BIGINT NOT NULL PRIMARY KEY,
    code VARCHAR(8) NOT NULL,
    planet_id BIGINT NOT NULL,
    issuer_id BIGINT NOT NULL,
    time_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    time_expires TIMESTAMP,

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_issuer FOREIGN KEY(issuer_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS planet_members (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    planet_id BIGINT NOT NULL,
    nickname VARCHAR(32),
    member_pfp TEXT,

    UNIQUE (user_id, planet_id),

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES Users(id)
);

CREATE TABLE IF NOT EXISTS planet_messages (
    id BIGINT NOT NULL PRIMARY KEY,
    reply_to_id BIGINT,
    author_user_id BIGINT NOT NULL,
    content TEXT NOT NULL,
    time_sent TIMESTAMP NOT NULL,
    message_index BIGINT NOT NULL,
    embed_data TEXT,
    mentions_data TEXT,
    author_member_id BIGINT NOT NULL,
    planet_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_member FOREIGN KEY(author_member_id) REFERENCES planet_members(id),
    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES planet_chat_channels(id),
    CONSTRAINT fk_author FOREIGN KEY(author_user_id) REFERENCES users(id),
    CONSTRAINT fk_replyto FOREIGN KEY(reply_to_id) REFERENCES planet_messages(id)
);

CREATE TABLE IF NOT EXISTS planet_roles (
    id BIGINT NOT NULL PRIMARY KEY,
    name VARCHAR(32) NOT NULL,
    position INT NOT NULL,
    planet_id BIGINT NOT NULL,
    red SMALLINT NOT NULL DEFAULT 0,
    green SMALLINT NOT NULL DEFAULT 0,
    blue SMALLINT NOT NULL DEFAULT 0,
    bold BOOLEAN NOT NULL DEFAULT false,
    italics BOOLEAN NOT NULL DEFAULT false,
    permissions BIGINT NOT NULL DEFAULT 0,

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id)
);

CREATE TABLE IF NOT EXISTS planet_role_members (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    role_id BIGINT NOT NULL,
    planet_id BIGINT NOT NULL,
    member_id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
    CONSTRAINT fk_role FOREIGN KEY(role_id) REFERENCES planet_roles(id),
    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_member FOREIGN KEY(member_id) REFERENCES planet_members(id)
);

CREATE TABLE IF NOT EXISTS permissions_nodes (
    id BIGINT NOT NULL PRIMARY KEY,
    code BIGINT NOT NULL,
    mask BIGINT NOT NULL,
    role_id BIGINT NOT NULL,
    target_id BIGINT NOT NULL,
    planet_id BIGINT NOT NULL,
    target_type INT NOT NULL,

    CONSTRAINT fk_role FOREIGN KEY(role_id) REFERENCES planet_roles(id),
    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id)
);

CREATE TABLE IF NOT EXISTS referrals (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    referrer_id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
    CONSTRAINT fk_referrer FOREIGN KEY(referrer_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS message_hashes (
    hash BYTEA NOT NULL PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS stats (
    id BIGSERIAL NOT NULL PRIMARY KEY,
    time_created TIMESTAMP NOT NULL,
    messages_sent BIGINT NOT NULL,
    user_count BIGINT NOT NULL,
    planet_count BIGINT NOT NULL,
    planet_member_count BIGINT NOT NULL,
    channel_count BIGINT NOT NULL,
    category_count BIGINT NOT NULL,
    message_day_count BIGINT NOT NULL
);

ALTER TABLE planets
    ADD CONSTRAINT fk_default_role
        FOREIGN KEY(default_role_id)
            REFERENCES planet_roles(id);

ALTER TABLE planets
    ADD CONSTRAINT fk_primary_channel
        FOREIGN KEY(primary_channel_id)
            REFERENCES planet_chat_channels(id);

COMMIT;

