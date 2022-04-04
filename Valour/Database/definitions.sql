BEGIN;

CREATE TABLE IF NOT EXISTS Users (
    Id BIGINT NOT NULL PRIMARY KEY,
    Name VARCHAR(32) NOT NULL,
    Joined TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    PfpUrl TEXT,
    Bot BOOLEAN NOT NULL DEFAULT false,
    Disabled BOOLEAN NOT NULL DEFAULT false,
    ValourStaff BOOLEAN NOT NULL DEFAULT false,
    UserState_Value INT NOT NULL DEFAULT 0,
    LastActive TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
);

CREATE TABLE IF NOT EXISTS UserEmails (
    Email TEXT NOT NULL PRIMARY KEY,
    Verified BOOLEAN NOT NULL DEFAULT false,
    User_Id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS AuthTokens (
    Id VARCHAR(40) NOT NULL,
    App_Id VARCHAR(36) NOT NULL,
    User_Id BIGINT NOT NULL,
    Scope BIGINT NOT NULL,
    Created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    Expires TIMESTAMP NOT NULL DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '7 days'),

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS Credentials (
    Id BIGINT NOT NULL PRIMARY KEY,
    User_Id BIGINT NOT NULL,
    CredentialType VARCHAR(16) NOT NULL,
    Identifier VARCHAR(64) NOT NULL,
    Secret BYTEA NOT NULL,
    Salt BYTEA NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS EmailConfirmCodes (
    Code VARCHAR(36) NOT NULL PRIMARY KEY,
    User_Id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS NotificationSubscriptions (
    Id BIGINT NOT NULL PRIMARY KEY,
    User_Id BIGINT NOT NULL,
    Endpoint TEXT NOT NULL,
    Not_Key  VARCHAR(128) NOT NULL,
    Auth VARCHAR(32) NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS OauthApps (
    Id BIGINT NOT NULL PRIMARY KEY,
    Secret VARCHAR(44) NOT NULL,
    Owner_Id BIGINT NOT NULL,
    Uses INTEGER NOT NULL DEFAULT 0,
    Name VARCHAR(32) NOT NULL,
    ImageUrl TEXT,

    CONSTRAINT fk_owner FOREIGN KEY(Owner_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS PasswordRecoveries (
    Code VARCHAR(36) NOT NULL PRIMARY KEY,
    User_Id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS Planets (
    Id BIGINT NOT NULL PRIMARY KEY,
    Owner_Id BIGINT NOT NULL,
    Name VARCHAR(32) NOT NULL,
    Image_Url TEXT NOT NULL,
    Description TEXT NOT NULL,
    Public BOOLEAN NOT NULL DEFAULT true,
    Default_Role_Id BIGINT NOT NULL,
    Main_Channel_Id BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS PlanetBans (
    Id BIGINT NOT NULL PRIMARY KEY,
    Target_Id BIGINT NOT NULL,
    Planet_Id BIGINT NOT NULL,
    Banner_Id BIGINT NOT NULL,
    Reason TEXT NOT NULL,
    Time TIMESTAMP NOT NULL,
    Expires TIMESTAMP,

    CONSTRAINT fk_target FOREIGN KEY(Target_Id) REFERENCES Users(Id),
    CONSTRAINT fk_banner FOREIGN KEY(Banner_Id) REFERENCES Users(Id),
    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id)
);

CREATE TABLE IF NOT EXISTS Channels (
    Id BIGINT NOT NULL PRIMARY KEY,
    Name VARCHAR(32) NOT NULL,
    Position INT NOT NULL,
    Description TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS PlanetChannels (
    Id BIGINT NOT NULL PRIMARY KEY,
    Planet_Id BIGINT NOT NULL,
    Parent_Id BIGINT,
    CONSTRAINT fk_base_channel FOREIGN KEY(Id) REFERENCES Channels(Id),
    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id),
    CONSTRAINT fk_parent FOREIGN KEY(Parent_Id) REFERENCES PlanetChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetCategoryChannels (
    Id BIGINT NOT NULL PRIMARY KEY,

    CONSTRAINT fk_inherit FOREIGN KEY(Id) REFERENCES PlanetChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetChatChannels (
    Id BIGINT NOT NULL PRIMARY KEY,
    MessageCount BIGINT NOT NULL DEFAULT 0,
    InheritsPerms BOOLEAN NOT NULL DEFAULT true,

    CONSTRAINT fk_inherit FOREIGN KEY(Id) REFERENCES PlanetChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetInvites (
    Id BIGINT NOT NULL PRIMARY KEY,
    Code VARCHAR(8) NOT NULL,
    Planet_Id BIGINT NOT NULL,
    Issuer_Id BIGINT NOT NULL,
    Time TIMESTAMP NOT NULL,
    Expires TIMESTAMP,

    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id),
    CONSTRAINT fk_issuer FOREIGN KEY(Issuer_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS PlanetMembers (
    Id BIGINT NOT NULL PRIMARY KEY,
    User_Id BIGINT NOT NULL,
    Planet_Id BIGINT NOT NULL,
    Nickname VARCHAR(32),
    MemberPfp TEXT,

    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id),
    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS Messages (
    Id BIGINT NOT NULL PRIMARY KEY,
    Author_Id BIGINT NOT NULL,
    Content TEXT NOT NULL,
    TimeSent TIMESTAMP NOT NULL,
    MessageIndex BIGINT NOT NULL,
    EmbedData JSONB,
    MentionsData JSONB,

    CONSTRAINT fk_author FOREIGN KEY(Author_Id) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS PlanetMessages (
    Id BIGINT NOT NULL PRIMARY KEY,
    Member_Id BIGINT NOT NULL,
    Planet_Id BIGINT NOT NULL,
    Channel_Id BIGINT NOT NULL,

    CONSTRAINT fk_base_message FOREIGN KEY(Id) REFERENCES Messages(Id),
    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id),
    CONSTRAINT fk_member FOREIGN KEY(Member_Id) REFERENCES PlanetMembers(Id),
    CONSTRAINT fk_channel FOREIGN KEY(Channel_Id) REFERENCES PlanetChatChannels(Id)
);

CREATE TABLE IF NOT EXISTS PlanetRoles (
    Id BIGINT NOT NULL PRIMARY KEY,
    Name VARCHAR(32) NOT NULL,
    Position INT NOT NULL,
    Planet_id BIGINT NOT NULL,
    Color_Red SMALLINT NOT NULL DEFAULT 0,
    Color_Green SMALLINT NOT NULL DEFAULT 0,
    Color_Blue SMALLINT NOT NULL DEFAULT 0,
    Bold BOOLEAN NOT NULL DEFAULT false,
    Italics BOOLEAN NOT NULL DEFAULT false,
    Permissions BIGINT NOT NULL DEFAULT 0,

    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id)
);

CREATE TABLE IF NOT EXISTS PlanetRoleMembers (
    Id BIGINT NOT NULL PRIMARY KEY,
    User_Id BIGINT NOT NULL,
    Role_Id BIGINT NOT NULL,
    Planet_Id BIGINT NOT NULL,
    Member_Id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id),
    CONSTRAINT fk_role FOREIGN KEY(Role_Id) REFERENCES PlanetRoles(Id),
    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id),
    CONSTRAINT fk_member FOREIGN KEY(Member_Id) REFERENCES PlanetMembers(Id)
);

CREATE TABLE IF NOT EXISTS PermissionsNodes (
    Id BIGINT NOT NULL PRIMARY KEY,
    Code BIGINT NOT NULL,
    Mask BIGINT NOT NULL,
    Role_Id BIGINT NOT NULL,
    Target_Id BIGINT NOT NULL,
    Planet_Id BIGINT NOT NULL,
    Target_Type INT NOT NULL,

    CONSTRAINT fk_role FOREIGN KEY(Role_Id) REFERENCES PlanetRoles(Id),
    CONSTRAINT fk_planet FOREIGN KEY(Planet_Id) REFERENCES Planets(Id)
);

CREATE TABLE IF NOT EXISTS Referrals (
    Id BIGINT NOT NULL PRIMARY KEY,
    User_Id BIGINT NOT NULL,
    Referrer_Id BIGINT NOT NULL,

    CONSTRAINT fk_user FOREIGN KEY(User_Id) REFERENCES Users(Id),
    CONSTRAINT fk_referrer FOREIGN KEY(Referrer_Id) REFERENCES Users(Id)
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
        FOREIGN KEY(Default_Role_Id) 
            REFERENCES PlanetRoles(Id);

ALTER TABLE Planets
    ADD CONSTRAINT fk_main_channel 
        FOREIGN KEY(Main_Channel_Id) 
            REFERENCES PlanetChatChannels(Id);

COMMIT;

