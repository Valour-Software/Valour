using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blocked_user_emails",
                columns: table => new
                {
                    email = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocked_user_emails", x => x.email);
                });

            migrationBuilder.CreateTable(
                name: "cdn_bucket_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    mime_type = table.Column<string>(type: "text", nullable: true),
                    file_name = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cdn_bucket_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cdn_proxies",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: true),
                    mime_type = table.Column<string>(type: "text", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cdn_proxies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    plural_name = table.Column<string>(type: "text", nullable: true),
                    short_code = table.Column<string>(type: "text", nullable: true),
                    symbol = table.Column<string>(type: "text", nullable: true),
                    issued = table.Column<long>(type: "bigint", nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "node_stats",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    connection_count = table.Column<int>(type: "integer", nullable: false),
                    connection_group_count = table.Column<int>(type: "integer", nullable: false),
                    planet_count = table.Column<int>(type: "integer", nullable: false),
                    active_member_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_stats", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: true),
                    source_id = table.Column<long>(type: "bigint", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    time_sent = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_read = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    click_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    custom_icon = table.Column<bool>(type: "boolean", nullable: false),
                    animated_icon = table.Column<bool>(type: "boolean", nullable: false),
                    icon_url = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    @public = table.Column<bool>(name: "public", type: "boolean", nullable: false),
                    discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    nsfw = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reporting_user_id = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: true),
                    planet_id = table.Column<long>(type: "bigint", nullable: true),
                    reason_code = table.Column<long>(type: "bigint", nullable: false),
                    long_reason = table.Column<string>(type: "text", nullable: true),
                    reviewed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stat_objects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    messages_sent = table.Column<int>(type: "integer", nullable: false),
                    user_count = table.Column<int>(type: "integer", nullable: false),
                    planet_count = table.Column<int>(type: "integer", nullable: false),
                    planet_member_count = table.Column<int>(type: "integer", nullable: false),
                    channel_count = table.Column<int>(type: "integer", nullable: false),
                    category_count = table.Column<int>(type: "integer", nullable: false),
                    message_day_count = table.Column<int>(type: "integer", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stat_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenor_favorites",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    tenor_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenor_favorites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    headline = table.Column<string>(type: "text", nullable: true),
                    bio = table.Column<string>(type: "text", nullable: true),
                    border_color = table.Column<string>(type: "text", nullable: true),
                    glow_color = table.Column<string>(type: "text", nullable: true),
                    text_color = table.Column<string>(type: "text", nullable: true),
                    primary_color = table.Column<string>(type: "text", nullable: true),
                    secondary_color = table.Column<string>(type: "text", nullable: true),
                    tertiary_color = table.Column<string>(type: "text", nullable: true),
                    anim_border = table.Column<bool>(type: "boolean", nullable: false),
                    bg_image = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    custom_avatar = table.Column<bool>(type: "boolean", nullable: false),
                    animated_avatar = table.Column<bool>(type: "boolean", nullable: false),
                    pfp_url = table.Column<string>(type: "text", nullable: true),
                    time_joined = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    bot = table.Column<bool>(type: "boolean", nullable: false),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    valour_staff = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: true),
                    user_state_code = table.Column<int>(type: "integer", nullable: false),
                    time_last_active = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_mobile = table.Column<bool>(type: "boolean", nullable: false),
                    tag = table.Column<string>(type: "text", nullable: true),
                    compliance = table.Column<bool>(type: "boolean", nullable: false),
                    subscription_type = table.Column<string>(type: "text", nullable: true),
                    prior_name = table.Column<string>(type: "text", nullable: true),
                    name_change_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    channel_type = table.Column<int>(type: "integer", nullable: false),
                    last_update_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: true),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    inherits_perms = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channels", x => x.id);
                    table.ForeignKey(
                        name: "FK_channels_channels_parent_id",
                        column: x => x.parent_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_channels_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "planet_bans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    issuer_id = table.Column<long>(type: "bigint", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_expires = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_bans", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_bans_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planet_invites",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    issuer_id = table.Column<long>(type: "bigint", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_expires = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_invites", x => x.code);
                    table.ForeignKey(
                        name: "FK_planet_invites_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planet_roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    permissions = table.Column<long>(type: "bigint", nullable: false),
                    chat_perms = table.Column<long>(type: "bigint", nullable: false),
                    cat_perms = table.Column<long>(type: "bigint", nullable: false),
                    voice_perms = table.Column<long>(type: "bigint", nullable: false),
                    color = table.Column<string>(type: "text", nullable: true),
                    bold = table.Column<bool>(type: "boolean", nullable: false),
                    italics = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    anyone_can_mention = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_roles", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_roles_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_tokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    scope = table.Column<long>(type: "bigint", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_expires = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    issued_address = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_auth_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credentials",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    credential_type = table.Column<string>(type: "text", nullable: true),
                    identifier = table.Column<string>(type: "text", nullable: true),
                    secret = table.Column<byte[]>(type: "bytea", nullable: true),
                    salt = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credentials", x => x.id);
                    table.ForeignKey(
                        name: "FK_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_confirm_codes",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_confirm_codes", x => x.code);
                    table.ForeignKey(
                        name: "FK_email_confirm_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: true),
                    key = table.Column<string>(type: "text", nullable: true),
                    auth = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oauth_apps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    secret = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    redirect_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_apps", x => x.id);
                    table.ForeignKey(
                        name: "FK_oauth_apps_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_recoveries",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_recoveries", x => x.code);
                    table.ForeignKey(
                        name: "FK_password_recoveries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planet_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    nickname = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    member_pfp = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    role_hash_key = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_members_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_planet_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "referrals",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    referrer_id = table.Column<long>(type: "bigint", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reward = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referrals", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_referrals_users_referrer_id",
                        column: x => x.referrer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_referrals_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "themes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    custom_banner = table.Column<bool>(type: "boolean", nullable: false),
                    animated_banner = table.Column<bool>(type: "boolean", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false),
                    font_color = table.Column<string>(type: "text", nullable: true),
                    font_alt_color = table.Column<string>(type: "text", nullable: true),
                    link_color = table.Column<string>(type: "text", nullable: true),
                    main_color_1 = table.Column<string>(type: "text", nullable: true),
                    main_color_2 = table.Column<string>(type: "text", nullable: true),
                    main_color_3 = table.Column<string>(type: "text", nullable: true),
                    main_color_4 = table.Column<string>(type: "text", nullable: true),
                    main_color_5 = table.Column<string>(type: "text", nullable: true),
                    tint_color = table.Column<string>(type: "text", nullable: true),
                    vibrant_purple = table.Column<string>(type: "text", nullable: true),
                    vibrant_blue = table.Column<string>(type: "text", nullable: true),
                    vibrant_cyan = table.Column<string>(type: "text", nullable: true),
                    pastel_cyan = table.Column<string>(type: "text", nullable: true),
                    pastel_cyan_purple = table.Column<string>(type: "text", nullable: true),
                    pastel_purple = table.Column<string>(type: "text", nullable: true),
                    pastel_red = table.Column<string>(type: "text", nullable: true),
                    custom_css = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_themes", x => x.id);
                    table.ForeignKey(
                        name: "FK_themes_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_emails",
                columns: table => new
                {
                    email = table.Column<string>(type: "text", nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    birth_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locality = table.Column<int>(type: "integer", nullable: true),
                    join_invite_code = table.Column<string>(type: "text", nullable: true),
                    join_source = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_emails", x => x.email);
                    table.ForeignKey(
                        name: "FK_user_emails_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_friends",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    friend_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_friends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_friends_users_friend_id",
                        column: x => x.friend_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_friends_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_subscriptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_charged = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    renewals = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channel_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_channel_members_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_channel_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_channel_states",
                columns: table => new
                {
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: true),
                    last_viewed_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_channel_states", x => new { x.user_id, x.channel_id });
                    table.ForeignKey(
                        name: "FK_user_channel_states_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_channel_states_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_user_channel_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permissions_nodes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<long>(type: "bigint", nullable: false),
                    mask = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false),
                    target_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions_nodes", x => x.id);
                    table.ForeignKey(
                        name: "FK_permissions_nodes_channels_target_id",
                        column: x => x.target_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_permissions_nodes_planet_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "planet_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_permissions_nodes_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "eco_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_member_id = table.Column<long>(type: "bigint", nullable: true),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    balance_value = table.Column<decimal>(type: "numeric", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eco_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_eco_accounts_planet_members_planet_member_id",
                        column: x => x.planet_member_id,
                        principalTable: "planet_members",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_eco_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: true),
                    reply_to_id = table.Column<long>(type: "bigint", nullable: true),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    author_member_id = table.Column<long>(type: "bigint", nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    time_sent = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    embed_data = table.Column<string>(type: "text", nullable: true),
                    mentions_data = table.Column<string>(type: "text", nullable: true),
                    attachments_data = table.Column<string>(type: "text", nullable: true),
                    edit_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_messages_reply_to_id",
                        column: x => x.reply_to_id,
                        principalTable: "messages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_messages_planet_members_author_member_id",
                        column: x => x.author_member_id,
                        principalTable: "planet_members",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_messages_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_messages_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planet_role_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    member_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_role_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_role_members_planet_members_member_id",
                        column: x => x.member_id,
                        principalTable: "planet_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_planet_role_members_planet_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "planet_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_planet_role_members_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_planet_role_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "theme_votes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    theme_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    sentiment = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_theme_votes", x => x.id);
                    table.ForeignKey(
                        name: "FK_theme_votes_themes_theme_id",
                        column: x => x.theme_id,
                        principalTable: "themes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_theme_votes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    user_from_id = table.Column<long>(type: "bigint", nullable: false),
                    account_from_id = table.Column<long>(type: "bigint", nullable: false),
                    user_to_id = table.Column<long>(type: "bigint", nullable: false),
                    account_to_id = table.Column<long>(type: "bigint", nullable: false),
                    time_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    data = table.Column<string>(type: "text", nullable: true),
                    fingerprint = table.Column<string>(type: "text", nullable: true),
                    forced_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_transactions_eco_accounts_account_from_id",
                        column: x => x.account_from_id,
                        principalTable: "eco_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transactions_eco_accounts_account_to_id",
                        column: x => x.account_to_id,
                        principalTable: "eco_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transactions_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transactions_users_user_from_id",
                        column: x => x.user_from_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transactions_users_user_to_id",
                        column: x => x.user_to_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_user_id",
                table: "auth_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_channel_members_channel_id",
                table: "channel_members",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_channel_members_user_id",
                table: "channel_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_channels_parent_id",
                table: "channels",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_channels_planet_id",
                table: "channels",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_credentials_user_id",
                table: "credentials",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_eco_accounts_planet_member_id",
                table: "eco_accounts",
                column: "planet_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_eco_accounts_user_id",
                table: "eco_accounts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_email_confirm_codes_user_id",
                table: "email_confirm_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_author_member_id",
                table: "messages",
                column: "author_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_author_user_id",
                table: "messages",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_channel_id",
                table: "messages",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_planet_id",
                table: "messages",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_reply_to_id",
                table: "messages",
                column: "reply_to_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_user_id",
                table: "notification_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_oauth_apps_owner_id",
                table: "oauth_apps",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_password_recoveries_user_id",
                table: "password_recoveries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_nodes_planet_id",
                table: "permissions_nodes",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_nodes_role_id",
                table: "permissions_nodes",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_nodes_target_id",
                table: "permissions_nodes",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_bans_planet_id",
                table: "planet_bans",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_invites_planet_id",
                table: "planet_invites",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_planet_id",
                table: "planet_members",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_role_hash_key",
                table: "planet_members",
                column: "role_hash_key");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_user_id",
                table: "planet_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_user_id_planet_id",
                table: "planet_members",
                columns: new[] { "user_id", "planet_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_member_id",
                table: "planet_role_members",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_planet_id",
                table: "planet_role_members",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_role_id",
                table: "planet_role_members",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_user_id",
                table: "planet_role_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_roles_planet_id",
                table: "planet_roles",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_referrals_referrer_id",
                table: "referrals",
                column: "referrer_id");

            migrationBuilder.CreateIndex(
                name: "IX_theme_votes_theme_id",
                table: "theme_votes",
                column: "theme_id");

            migrationBuilder.CreateIndex(
                name: "IX_theme_votes_user_id",
                table: "theme_votes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_themes_author_id",
                table: "themes",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_from_id",
                table: "transactions",
                column: "account_from_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_to_id",
                table: "transactions",
                column: "account_to_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_planet_id",
                table: "transactions",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_user_from_id",
                table: "transactions",
                column: "user_from_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_user_to_id",
                table: "transactions",
                column: "user_to_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_channel_states_channel_id",
                table: "user_channel_states",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_channel_states_planet_id",
                table: "user_channel_states",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_channel_states_user_id",
                table: "user_channel_states",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_user_id",
                table: "user_emails",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_friends_friend_id",
                table: "user_friends",
                column: "friend_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_friends_user_id",
                table: "user_friends",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_subscriptions_user_id",
                table: "user_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_tag_name",
                table: "users",
                columns: new[] { "tag", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_time_last_active",
                table: "users",
                column: "time_last_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_tokens");

            migrationBuilder.DropTable(
                name: "blocked_user_emails");

            migrationBuilder.DropTable(
                name: "cdn_bucket_items");

            migrationBuilder.DropTable(
                name: "cdn_proxies");

            migrationBuilder.DropTable(
                name: "channel_members");

            migrationBuilder.DropTable(
                name: "credentials");

            migrationBuilder.DropTable(
                name: "currencies");

            migrationBuilder.DropTable(
                name: "email_confirm_codes");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "node_stats");

            migrationBuilder.DropTable(
                name: "notification_subscriptions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "oauth_apps");

            migrationBuilder.DropTable(
                name: "password_recoveries");

            migrationBuilder.DropTable(
                name: "permissions_nodes");

            migrationBuilder.DropTable(
                name: "planet_bans");

            migrationBuilder.DropTable(
                name: "planet_invites");

            migrationBuilder.DropTable(
                name: "planet_role_members");

            migrationBuilder.DropTable(
                name: "referrals");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "stat_objects");

            migrationBuilder.DropTable(
                name: "tenor_favorites");

            migrationBuilder.DropTable(
                name: "theme_votes");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "user_channel_states");

            migrationBuilder.DropTable(
                name: "user_emails");

            migrationBuilder.DropTable(
                name: "user_friends");

            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropTable(
                name: "user_subscriptions");

            migrationBuilder.DropTable(
                name: "planet_roles");

            migrationBuilder.DropTable(
                name: "themes");

            migrationBuilder.DropTable(
                name: "eco_accounts");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "planet_members");

            migrationBuilder.DropTable(
                name: "planets");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
