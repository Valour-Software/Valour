using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Creates the procedure that marks a channel as viewed for everyone watching
    /// it. It was installed by hand on the first databases, so a database created
    /// from migrations alone did not have it. Replacing it where it already
    /// exists leaves the same definition in place.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20261115000000_ChannelStateBatchProcedure")]
    public partial class ChannelStateBatchProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE PROCEDURE batch_user_channel_state_update(
                    user_ids_in bigint[], channel_id_in bigint, new_time timestamp with time zone)
                    LANGUAGE plpgsql
                AS
                $$
                BEGIN
                    UPDATE user_channel_states
                    SET last_viewed_time = new_time
                    WHERE channel_id = channel_id_in
                      AND user_id = ANY(user_ids_in);
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The procedure existed before this migration on the first databases,
            // so removing it here would break them.
        }
    }
}
