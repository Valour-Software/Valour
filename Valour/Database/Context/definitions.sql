BEGIN;

CREATE TABLE IF NOT EXISTS node_stats (
    name text NOT NULL PRIMARY KEY,
    connection_count INT NOT NULL DEFAULT 0,
    connection_group_count INT NOT NULL DEFAULT 0,
    planet_count INT NOT NULL DEFAULT 0,
    active_member_count INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS primary_node_connections (
    connection_id VARCHAR(22) NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    open_time TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    node_id VARCHAR(10) NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

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
    time_last_active TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    is_mobile BOOLEAN NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS user_friends (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    friend_id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
    CONSTRAINT fk_friend FOREIGN KEY(friend_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS tenor_favorites (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    tenor_id TEXT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS user_emails (
    email TEXT NOT NULL PRIMARY KEY,
    verified BOOLEAN NOT NULL DEFAULT false,
    user_id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS blocked_user_emails (
    email TEXT NOT NULL PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS auth_tokens (
    id VARCHAR(40) NOT NULL PRIMARY KEY,
    app_id VARCHAR(36) NOT NULL,
    user_id BIGINT NOT NULL,
    scope BIGINT NOT NULL,
    time_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    time_expires TIMESTAMP NOT NULL DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '7 days'),
    issued_address TEXT not null,

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
    key  VARCHAR(128) NOT NULL,
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
    redirect_url TEXT NOT NULL,

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
    icon_url TEXT,
    description TEXT NOT NULL,
    public BOOLEAN NOT NULL DEFAULT true,
    discoverable BOOLEAN NOT NULL DEFAULT true,
    default_role_id BIGINT,
    primary_channel_id BIGINT,

    CONSTRAINT fk_default_role FOREIGN KEY(default_role_id) REFERENCES planet_roles(id),
    CONSTRAINT fk_primary_channel FOREIGN KEY(primary_channel_id) REFERENCES planet_chat_channels(id)
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

CREATE TABLE IF NOT EXISTS channels (
    id BIGINT NOT NULL PRIMARY KEY,
    time_last_active TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    is_deleted BOOLEAN NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS planet_channels (
    id BIGINT NOT NULL PRIMARY KEY,
    name VARCHAR(32) NOT NULL,
    description TEXT NOT NULL,
    position INT NOT NULL,
    planet_id BIGINT NOT NULL,
    parent_id BIGINT,
    inherits_perms BOOLEAN NOT NULL DEFAULT true,

    CONSTRAINT fk_inherit FOREIGN KEY(id) REFERENCES channels(id),
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

CREATE TABLE IF NOT EXISTS planet_voice_channels (
    id BIGINT NOT NULL PRIMARY KEY,

    CONSTRAINT fk_inherit FOREIGN KEY(id) REFERENCES planet_channels(id)
);

CREATE TABLE IF NOT EXISTS direct_chat_channels (
    id BIGINT NOT NULL PRIMARY KEY,
    user_one_id BIGINT NOT NULL,
    user_two_id BIGINT NOT NULL,
    message_count BIGINT NOT NULL DEFAULT 0,

    CONSTRAINT fk_user_one FOREIGN KEY(user_one_id) REFERENCES users(id),
    CONSTRAINT fk_user_two FOREIGN KEY(user_two_id) REFERENCES users(id),
    CONSTRAINT fk_inherit FOREIGN KEY(id) REFERENCES channels(id)
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
    is_deleted BOOLEAN DEFAULT false,

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
    attachments_data TEXT,
    author_member_id BIGINT NOT NULL,
    planet_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    edited BOOLEAN NOT NULL DEFAULT false,

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_member FOREIGN KEY(author_member_id) REFERENCES planet_members(id),
    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES planet_chat_channels(id),
    CONSTRAINT fk_author FOREIGN KEY(author_user_id) REFERENCES users(id),
    CONSTRAINT fk_replyto FOREIGN KEY(reply_to_id) REFERENCES planet_messages(id)
);

CREATE TABLE IF NOT EXISTS direct_messages (
    id BIGINT NOT NULL PRIMARY KEY,
    reply_to_id BIGINT,
    author_user_id BIGINT NOT NULL,
    content TEXT NOT NULL,
    time_sent TIMESTAMP NOT NULL,
    message_index BIGINT NOT NULL,
    embed_data TEXT,
    mentions_data TEXT,
    attachments_data TEXT,
    channel_id BIGINT NOT NULL,
    edited BOOLEAN NOT NULL DEFAULT false,

    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES direct_chat_channels(id),
    CONSTRAINT fk_author FOREIGN KEY(author_user_id) REFERENCES users(id),
    CONSTRAINT fk_replyto FOREIGN KEY(reply_to_id) REFERENCES direct_messages(id)
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
    chat_perms BIGINT NOT NULL DEFAULT 7,
    cat_perms BIGINT NOT NULL DEFAULT 1,
    voice_perms BIGINT NOT NULL DEFAULT 7,

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
    user_id BIGINT NOT NULL PRIMARY KEY,
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

CREATE TABLE IF NOT EXISTS channel_states (
    channel_id BIGINT NOT NULL PRIMARY KEY,
    planet_id BIGINT,
    last_update_time TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),

    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES channels(id),
        CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id)
);

CREATE TABLE IF NOT EXISTS user_channel_states (
    user_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    last_viewed_state TEXT,

    PRIMARY KEY(user_id, channel_id),

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES channels(id)
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

