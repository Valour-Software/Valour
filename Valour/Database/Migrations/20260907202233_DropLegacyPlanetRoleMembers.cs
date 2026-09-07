using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyPlanetRoleMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert only planets that have not completed the membership-flag migration.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT r.planet_id FROM planet_roles r
                        JOIN planets p ON p.id = r.planet_id
                        WHERE p.version < 1 AND EXISTS (
                            SELECT 1 FROM planet_role_members legacy
                            JOIN planet_members member ON member.id = legacy.member_id
                            WHERE member.planet_id = p.id
                        )
                        GROUP BY r.planet_id HAVING count(*) > 256
                    ) THEN
                        RAISE EXCEPTION 'Cannot migrate legacy memberships: a planet has more than 256 roles';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM planet_role_members rm
                        JOIN planet_members m ON m.id = rm.member_id
                        JOIN planets p ON p.id = m.planet_id
                        LEFT JOIN planet_roles r ON r.id = rm.role_id
                        WHERE p.version < 1 AND (r.id IS NULL OR r.planet_id <> m.planet_id)
                    ) THEN
                        RAISE EXCEPTION 'Cannot migrate legacy memberships: a role belongs to a different planet or is missing';
                    END IF;
                END $$;

                WITH indices AS (
                    SELECT r.id, (row_number() OVER (
                        PARTITION BY r.planet_id ORDER BY r.is_default DESC, r.position DESC, r.id
                    ) - 1)::integer AS bit_index
                    FROM planet_roles r JOIN planets p ON p.id = r.planet_id
                    WHERE p.version < 1 AND EXISTS (
                        SELECT 1 FROM planet_role_members legacy
                        JOIN planet_members member ON member.id = legacy.member_id
                        WHERE member.planet_id = p.id
                    )
                )
                UPDATE planet_roles r SET local_index = i.bit_index
                FROM indices i WHERE r.id = i.id;

                WITH flags AS (
                    SELECT m.id,
                        COALESCE(bit_or(1::bigint << (r.local_index % 64)) FILTER (WHERE r.local_index / 64 = 0), 0) AS rf0,
                        COALESCE(bit_or(1::bigint << (r.local_index % 64)) FILTER (WHERE r.local_index / 64 = 1), 0) AS rf1,
                        COALESCE(bit_or(1::bigint << (r.local_index % 64)) FILTER (WHERE r.local_index / 64 = 2), 0) AS rf2,
                        COALESCE(bit_or(1::bigint << (r.local_index % 64)) FILTER (WHERE r.local_index / 64 = 3), 0) AS rf3
                    FROM planet_members m JOIN planets p ON p.id = m.planet_id
                    LEFT JOIN planet_role_members rm ON rm.member_id = m.id
                    LEFT JOIN planet_roles r ON r.id = rm.role_id
                    WHERE p.version < 1 AND EXISTS (
                        SELECT 1 FROM planet_role_members legacy
                        JOIN planet_members member ON member.id = legacy.member_id
                        WHERE member.planet_id = p.id
                    )
                    GROUP BY m.id
                )
                UPDATE planet_members m SET rf0 = f.rf0, rf1 = f.rf1, rf2 = f.rf2, rf3 = f.rf3
                FROM flags f WHERE m.id = f.id;

                UPDATE planets SET version = 1 WHERE version < 1;
                """);

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS apply_member_access_for_all_in_role(bigint);
                DROP FUNCTION IF EXISTS apply_member_access_channel_all(bigint);
                DROP FUNCTION IF EXISTS apply_member_access_planet(bigint);
                DROP FUNCTION IF EXISTS apply_member_access(bigint, bigint);
                DROP FUNCTION IF EXISTS check_member_access(bigint, bigint);
                DROP FUNCTION IF EXISTS check_member_permission(bigint, bigint, bigint, integer);
                """);

            migrationBuilder.DropTable(
                name: "planet_role_members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Membership flags remain authoritative after downgrade; legacy rows are not recreated.
            migrationBuilder.CreateTable(
                name: "planet_role_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    member_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_member_id",
                table: "planet_role_members",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_role_id",
                table: "planet_role_members",
                column: "role_id");
        }
    }
}
