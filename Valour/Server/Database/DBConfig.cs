/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database;

public class DBConfig
{
    public static DBConfig instance;

    public string Host { get; set; }

    public string Password { get; set; }

    public string Username { get; set; }

    public string Database { get; set; }

    public DBConfig()
    {
        // Set main instance to the most recently created config
        instance = this;
    }
}