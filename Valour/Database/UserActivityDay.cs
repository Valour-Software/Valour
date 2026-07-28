using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

/// <summary>
/// One row per user per UTC day with any recorded activity. This is the
/// backing table for daily/monthly active user analytics; it deliberately
/// stores nothing beyond the (day, user) pair.
/// </summary>
public class UserActivityDay
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The UTC day the activity occurred on
    /// </summary>
    public DateOnly Day { get; set; }

    /// <summary>
    /// The id of the user who was active
    /// </summary>
    public long UserId { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<UserActivityDay>(e =>
        {
            // Table
            e.ToTable("user_activity_days");

            // Keys
            e.HasKey(x => new { x.Day, x.UserId });

            // Properties
            e.Property(x => x.Day)
                .HasColumnName("day");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            // Indices
            e.HasIndex(x => x.Day);
        });
    }
}
