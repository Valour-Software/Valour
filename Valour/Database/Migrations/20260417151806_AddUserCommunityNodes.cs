using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCommunityNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_community_nodes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    node_id = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    canonical_origin = table.Column<string>(type: "text", nullable: true),
                    authority_origin = table.Column<string>(type: "text", nullable: true),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    time_added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_community_nodes", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_community_nodes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_community_nodes_user_id",
                table: "user_community_nodes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_community_nodes_user_id_canonical_origin",
                table: "user_community_nodes",
                columns: new[] { "user_id", "canonical_origin" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_community_nodes");
        }
    }
}
