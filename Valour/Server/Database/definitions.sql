BEGIN;

CREATE TABLE IF NOT EXISTS Users (
    Id BIGINT NOT NULL PRIMARY KEY,
    Name VARCHAR(32) NOT NULL,
    Joined TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    PfpUrl TEXT,
    Bot BOOLEAN NOT NULL DEFAULT false,
    Disabled BOOLEAN NOT NULL DEFAULT false,
    ValourStaff BOOLEAN NOT NULL DEFAULT false,
    UserStateCode INT NOT NULL DEFAULT 0,
    LastActive TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
);

CREATE TABLE IF NOT EXISTS UserEmails (
    Email TEXT NOT NULL PRIMARY KEY,
    Verified BOOLEAN NOT NULL DEFAULT false,
    UserId BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS AuthTokens (
    Id VARCHAR(40) NOT NULL,
    App_Id VARCHAR(36) NOT NULL,
    UserId BIGINT NOT NULL,
    Scope BIGINT NOT NULL,
    Created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    Expires TIMESTAMP NOT NULL DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '7 days'),

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS Credentials (
    Id BIGINT NOT NULL PRIMARY KEY,
    UserId BIGINT NOT NULL,
    CredentialType VARCHAR(16) NOT NULL,
    Identifier VARCHAR(64) NOT NULL,
    Secret BYTEA NOT NULL,
    Salt BYTEA NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS EmailConfirmCodes (
    Code VARCHAR(36) NOT NULL PRIMARY KEY,
    UserId BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS NotificationSubscriptions (
    Id BIGINT NOT NULL PRIMARY KEY,
    UserId BIGINT NOT NULL,
    Endpoint TEXT NOT NULL,
    Not_Key  VARCHAR(128) NOT NULL,
    Auth VARCHAR(32) NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS OauthApps (
    Id BIGINT NOT NULL PRIMARY KEY,
    Secret VARCHAR(44) NOT NULL,
    OwnerId BIGINT NOT NULL,
    Uses INTEGER NOT NULL DEFAULT 0,
    Name VARCHAR(32) NOT NULL,
    ImageUrl TEXT,

    CONSTRAINT fk_owner FOREIGN KEY(OwnerId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS PasswordRecoveries (
    Code VARCHAR(36) NOT NULL PRIMARY KEY,
    UserId BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS Planets (
    Id BIGINT NOT NULL PRIMARY KEY,
    OwnerId BIGINT NOT NULL,
    Name VARCHAR(32) NOT NULL,
    IconUrl TEXT NOT NULL,
    Description TEXT NOT NULL,
    Public BOOLEAN NOT NULL DEFAULT true,
    DefaultRoleId BIGINT NOT NULL,
    PrimaryChannelId BIGINT NOT NULL,
);

CREATE TABLE IF NOT EXISTS PlanetBans (
    Id BIGINT NOT NULL PRIMARY KEY,
    TargetId BIGINT NOT NULL,
    PlanetId BIGINT NOT NULL,
    IssuerId BIGINT NOT NULL,
    Reason TEXT NOT NULL,
    Time TIMESTAMP NOT NULL,
    Expires TIMESTAMP,

    CONSTRAINT fk_target FOREIGN KEY(TargetId) REFERENCES Users(Id),
    CONSTRAINT fk_banner FOREIGN KEY(IssuerId) REFERENCES Users(Id),
    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id)
);

CREATE TABLE IF NOT EXISTS PlanetChannels (
    Id BIGINT NOT NULL PRIMARY KEY,
    Name VARCHAR(32) NOT NULL,
    Position INT NOT NULL,
    Description TEXT NOT NULL
    PlanetId BIGINT NOT NULL,
    ParentId BIGINT,
    InheritsPerms BOOLEAN NOT NULL DEFAULT true,
    CONSTRAINT fk_base_channel FOREIGN KEY(Id) REFERENCES Channels(Id),
    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id),
    CONSTRAINT fk_parent FOREIGN KEY(ParentId) REFERENCES PlanetChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetCategoryChannels (
    Id BIGINT NOT NULL PRIMARY KEY,

    CONSTRAINT fk_inherit FOREIGN KEY(Id) REFERENCES PlanetChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetChatChannels (
    Id BIGINT NOT NULL PRIMARY KEY,
    MessageCount BIGINT NOT NULL DEFAULT 0,

    CONSTRAINT fk_inherit FOREIGN KEY(Id) REFERENCES PlanetChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetInvites (
    Id BIGINT NOT NULL PRIMARY KEY,
    Code VARCHAR(8) NOT NULL,
    PlanetId BIGINT NOT NULL,
    IssuerId BIGINT NOT NULL,
    Created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    Expires TIMESTAMP,

    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id),
    CONSTRAINT fk_issuer FOREIGN KEY(IssuerId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS PlanetMembers (
    Id BIGINT NOT NULL PRIMARY KEY,
    UserId BIGINT NOT NULL,
    PlanetId BIGINT NOT NULL,
    Nickname VARCHAR(32),
    MemberPfp TEXT,

    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id),
    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS Messages (
    Id BIGINT NOT NULL PRIMARY KEY,
    AuthorId BIGINT NOT NULL,
    Content TEXT NOT NULL,
    TimeSent TIMESTAMP NOT NULL,
    MessageIndex BIGINT NOT NULL,
    EmbedData JSONB,
    MentionsData JSONB,

    CONSTRAINT fk_author FOREIGN KEY(AuthorId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS PlanetMessages (
    Id BIGINT NOT NULL PRIMARY KEY,
    MemberId BIGINT NOT NULL,
    PlanetId BIGINT NOT NULL,
    ChannelId BIGINT NOT NULL,

    CONSTRAINT fk_base_message FOREIGN KEY(Id) REFERENCES Messages(Id),
    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id),
    CONSTRAINT fk_member FOREIGN KEY(MemberId) REFERENCES PlanetMembers(Id),
    CONSTRAINT fk_channel FOREIGN KEY(ChannelId) REFERENCES PlanetChatChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetRoles (
    Id BIGINT NOT NULL PRIMARY KEY,
    Name VARCHAR(32) NOT NULL,
    Position INT NOT NULL,
    PlanetId BIGINT NOT NULL,
    Red SMALLINT NOT NULL DEFAULT 0,
    Green SMALLINT NOT NULL DEFAULT 0,
    Blue SMALLINT NOT NULL DEFAULT 0,
    Bold BOOLEAN NOT NULL DEFAULT false,
    Italics BOOLEAN NOT NULL DEFAULT false,
    Permissions BIGINT NOT NULL DEFAULT 0,

    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id)
);

CREATE TABLE IF NOT EXISTS PlanetRoleMembers (
    Id BIGINT NOT NULL PRIMARY KEY,
    UserId BIGINT NOT NULL,
    RoleId BIGINT NOT NULL,
    PlanetId BIGINT NOT NULL,
    MemberId BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id),
    CONSTRAINT fk_role FOREIGN KEY(RoleId) REFERENCES PlanetRoles(Id),
    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id),
    CONSTRAINT fk_member FOREIGN KEY(MemberId) REFERENCES PlanetMembers(Id)
);

CREATE TABLE IF NOT EXISTS PermissionsNodes (
    Id BIGINT NOT NULL PRIMARY KEY,
    Code BIGINT NOT NULL,
    Mask BIGINT NOT NULL,
    RoleId BIGINT NOT NULL,
    TargetId BIGINT NOT NULL,
    PlanetId BIGINT NOT NULL,
    TargetType INT NOT NULL,

    CONSTRAINT fk_role FOREIGN KEY(RoleId) REFERENCES PlanetRoles(Id),
    CONSTRAINT fk_planet FOREIGN KEY(PlanetId) REFERENCES Planets(Id)
);

CREATE TABLE IF NOT EXISTS Referrals (
    Id BIGINT NOT NULL PRIMARY KEY,
    UserId BIGINT NOT NULL,
    ReferrerId BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(UserId) REFERENCES Users(Id),
    CONSTRAINT fk_referrer FOREIGN KEY(ReferrerId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS MessageHashes (
    Hash BYTEA NOT NULL PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS Stats (
    Id BIGSERIAL NOT NULL PRIMARY KEY,
    Time TIMESTAMP NOT NULL,
    MessagesSent BIGINT NOT NULL,
    UserCount BIGINT NOT NULL,
    PlanetCount BIGINT NOT NULL,
    PlanetMemberCount BIGINT NOT NULL,
    ChannelCount BIGINT NOT NULL,
    CategoryCount BIGINT NOT NULL,
    Message24hCount BIGINT NOT NULL
);

ALTER TABLE Planets
    ADD CONSTRAINT fk_default_role 
        FOREIGN KEY(DefaultRoleId) 
            REFERENCES PlanetRoles(Id);

ALTER TABLE Planets
    ADD CONSTRAINT fk_primary_channel 
        FOREIGN KEY(PrimaryChannelId) 
            REFERENCES PlanetChatChannels(Id);

COMMIT;

