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
    tag VARCHAR(4) NOT NULL,
    time_joined TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    custom_avatar BOOLEAN NOT NULL DEFAULT false,
    animated_avatar BOOLEAN NOT NULL DEFAULT false,
    status TEXT,
    bot BOOLEAN NOT NULL DEFAULT false,
    disabled BOOLEAN NOT NULL DEFAULT false,
    valour_staff BOOLEAN NOT NULL DEFAULT false,
    user_state_code INT NOT NULL DEFAULT 0,
    time_last_active TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    is_mobile BOOLEAN NOT NULL DEFAULT false,
    subscription_type TEXT NOT NULL,

    CONSTRAINT user_tag_unique UNIQUE (name, tag)
);  

CREATE TABLE IF NOT EXISTS user_profiles (
    id BIGINT NOT NULL PRIMARY KEY,
    headline TEXT,
    bio TEXT,
    border_color VARCHAR(7),
    glow_color VARCHAR(7),
    primary_color VARCHAR(7),
    secondary_color VARCHAR(7),
    tertiary_color VARCHAR(7),
    anim_border BOOLEAN,
    bg_image TEXT,
    CONSTRAINT fk_user FOREIGN KEY(id) REFERENCES users(id)  
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
    birth_date TIMESTAMP NOT NULL,
    locality INTEGER NOT NULL,
    join_invite_code TEXT,
    join_source TEXT,

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
    created_at TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    expires_at TIMESTAMP NOT NULL DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '1 day'),

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
    created_at TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    expires_at TIMESTAMP NOT NULL DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '1 hour'),

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS planets (
    id BIGINT NOT NULL PRIMARY KEY,
    owner_id BIGINT NOT NULL,
    name VARCHAR(32) NOT NULL,
    custom_icon BOOLEAN NOT NULL DEFAULT false,
    animated_icon BOOLEAN NOT NULL DEFAULT false,
    description TEXT NOT NULL,
    public BOOLEAN NOT NULL DEFAULT true,
    discoverable BOOLEAN NOT NULL DEFAULT true,
    default_role_id BIGINT,
    primary_channel_id BIGINT,
    nsfw BOOLEAN NOT NULL DEFAULT false,

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
    name VARCHAR(32) NOT NULL,
    description TEXT NOT NULL,
    channel_type INT NOT NULL,
    last_update_time TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    is_deleted BOOLEAN NOT NULL DEFAULT false,

    /* Planet stuff */
    position INT,
    planet_id BIGINT,
    parent_id BIGINT,
    inherits_perms BOOLEAN,
    is_default BOOLEAN,

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_parent FOREIGN KEY(parent_id) REFERENCES channels(id)
);

CREATE TABLE IF NOT EXISTS channel_members (
    id BIGINT NOT NULL PRIMARY KEY,    
    user_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES channels(id)
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

CREATE TABLE IF NOT EXISTS messages (
    id BIGINT NOT NULL PRIMARY KEY,
    
    author_user_id BIGINT NOT NULL,
    content TEXT NOT NULL,
    time_sent TIMESTAMP NOT NULL,
    channel_id BIGINT NOT NULL,

    /* Nullables */
    planet_id BIGINT,
    author_member_id BIGINT,

    embed_data TEXT,
    mentions_data TEXT,
    attachments_data TEXT,

    reply_to_id BIGINT,
    edited_time TIMESTAMP,

    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
    CONSTRAINT fk_member FOREIGN KEY(author_member_id) REFERENCES planet_members(id),
    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES channels(id),
    CONSTRAINT fk_author FOREIGN KEY(author_user_id) REFERENCES users(id),
    CONSTRAINT fk_replyto FOREIGN KEY(reply_to_id) REFERENCES messages(id)
);

CREATE TABLE IF NOT EXISTS planet_roles (
    id BIGINT NOT NULL PRIMARY KEY,
    is_admin BOOLEAN NOT NULL DEFAULT false,
    name VARCHAR(32) NOT NULL,
    position INT NOT NULL,
    planet_id BIGINT NOT NULL,
    color VARCHAR(6) NOT NULL DEFAULT 'ffffff',
    bold BOOLEAN NOT NULL DEFAULT false,
    italics BOOLEAN NOT NULL DEFAULT false,
    permissions BIGINT NOT NULL DEFAULT 0,
    chat_perms BIGINT NOT NULL DEFAULT 7,
    cat_perms BIGINT NOT NULL DEFAULT 1,
    voice_perms BIGINT NOT NULL DEFAULT 7,
    is_default BOOLEAN NOT NULL DEFAULT false,

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
    created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    reward DECIMAL NOT NULL DEFAULT 0,

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

CREATE TABLE IF NOT EXISTS notifications (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    planet_id BIGINT,
    channel_id BIGINT,
    source_id BIGINT,
    time_sent TIMESTAMP NOT NULL,
    time_read TIMESTAMP,
    title TEXT NOT NULL,
    body TEXT NOT NULL,
    ImageUrl TEXT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES channels(id),
    CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id)
);

CREATE TABLE IF NOT EXISTS currencies (
  id BIGINT NOT NULL PRIMARY KEY,  
  planet_id BIGINT NOT NULL,
  name VARCHAR(32) NOT NULL,
  plural_name VARCHAR(32) NOT NULL,
  short_code VARCHAR(5) NOT NULL,
  symbol VARCHAR(5) NOT NULL,
  issued BIGINT NOT NULL DEFAULT 0,
  decimal_places INT NOT NULL DEFAULT 2,
    
  CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id)
);

CREATE TABLE IF NOT EXISTS eco_accounts (
  id BIGINT NOT NULL PRIMARY KEY,
  name TEXT NOT NULL,
  account_type INT NOT NULL,
  user_id BIGINT NOT NULL,
  planet_id BIGINT NOT NULL,
  planet_member_id BIGINT NOT NULL,
  currency_id BIGINT NOT NULL,
  balance_value DECIMAL NOT NULL DEFAULT 0,
  
  CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
  CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
  CONSTRAINT fk_currency FOREIGN KEY(currency_id) REFERENCES currencies(id),
  CONSTRAINT fk_member FOREIGN KEY(planet_member_id) REFERENCES planet_members(id)
);

CREATE TABLE IF NOT EXISTS transactions (
  id TEXT NOT NULL PRIMARY KEY,
  planet_id BIGINT NOT NULL,
  user_from_id BIGINT NOT NULL,
  account_from_id BIGINT NOT NULL,
  user_to_id BIGINT NOT NULL,
  account_to_id BIGINT NOT NULL,
  time_stamp TIMESTAMP NOT NULL,
  description TEXT,
  amount DECIMAL NOT NULL,
  data TEXT,
  fingerprint TEXT NOT NULL,
  forced_by BIGINT,
  
  CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id),
  CONSTRAINT fk_user_from FOREIGN KEY(user_from_id) REFERENCES users(id),
  CONSTRAINT fk_user_to FOREIGN KEY(user_to_id) REFERENCES users(id),
  CONSTRAINT fk_account_from FOREIGN KEY(account_from_id) REFERENCES eco_accounts(id),
  CONSTRAINT fk_account_to FOREIGN KEY(account_to_id) REFERENCES eco_accounts(id),
  CONSTRAINT fk_forced_by FOREIGN KEY(forced_by) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS reports (
  id TEXT NOT NULL PRIMARY KEY,
  time_created TIMESTAMP NOT NULL,
  reporting_user_id BIGINT NOT NULL,
  message_id BIGINT,
  channel_id BIGINT,
  planet_id BIGINT,
  reason_code BIGINT,
  long_reason TEXT,
  reviewed BOOLEAN NOT NULL DEFAULT false,
  
  CONSTRAINT fk_user FOREIGN KEY(reporting_user_id) REFERENCES users(id),
  CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES channels(id),
  CONSTRAINT fk_planet FOREIGN KEY(planet_id) REFERENCES planets(id)
);

CREATE TABLE IF NOT EXISTS user_subscriptions (
  id TEXT NOT NULL PRIMARY KEY,
  user_id BIGINT NOT NULL,
  type TEXT NOT NULL,
  created TIMESTAMP NOT NULL,
  last_charged TIMESTAMP NOT NULL,
  active BOOLEAN NOT NULL DEFAULT true,
  cancelled BOOLEAN NOT NULL DEFAULT false,
  renewals INT NOT NULL DEFAULT 0,
  
  CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS themes (
    id BIGINT NOT NULL PRIMARY KEY,
    author_id BIGINT NOT NULL,
    name VARCHAR(50) NOT NULL,
    description TEXT,
    custom_banner BOOLEAN NOT NULL DEFAULT false,
    animated_banner BOOLEAN NOT NULL DEFAULT false,
    published BOOLEAN NOT NULL DEFAULT false,
    
    font_color VARCHAR(7) NOT NULL,
    font_alt_color VARCHAR(7) NOT NULL,
    link_color VARCHAR(7) NOT NULL,
    
    main_color_1 VARCHAR(7) NOT NULL,
    main_color_2 VARCHAR(7) NOT NULL,
    main_color_3 VARCHAR(7) NOT NULL,
    main_color_4 VARCHAR(7) NOT NULL,
    main_color_5 VARCHAR(7) NOT NULL,
    
    tint_color VARCHAR(7) NOT NULL,
    
    vibrant_purple VARCHAR(7) NOT NULL,
    vibrant_blue VARCHAR(7) NOT NULL,
    vibrant_cyan VARCHAR(7) NOT NULL,
    
    pastel_cyan VARCHAR(7) NOT NULL,
    pastel_cyan_purple VARCHAR(7) NOT NULL,
    pastel_purple VARCHAR(7) NOT NULL,
    pastel_red VARCHAR(7) NOT NULL,
    custom_css TEXT,

    CONSTRAINT fk_author FOREIGN KEY(author_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS theme_votes (
    id BIGINT NOT NULL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    theme_id BIGINT NOT NULL,
    
    CONSTRAINT fk_user FOREIGN KEY(user_id) REFERENCES users(id),
    CONSTRAINT fk_theme FOREIGN KEY(theme_id) REFERENCES themes(id)
);

CREATE TABLE IF NOT EXISTS member_channel_access (
    channel_id BIGINT NOT NULL,
    member_id BIGINT NOT NULL,
    
    CONSTRAINT fk_channel FOREIGN KEY(channel_id) REFERENCES channels(id),
    CONSTRAINT fk_member FOREIGN KEY(member_id) REFERENCES planet_members(id),
);

COMMIT;

