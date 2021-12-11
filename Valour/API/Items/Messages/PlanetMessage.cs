using Valour.Api.Items.Planets.Members;

namespace Valour.Api.Items.Messages;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMessage : Shared.Items.Messages.PlanetMessage
{
    /// <summary>
    /// Returns the author of the message
    /// </summary>
    public async Task<PlanetMember> GetAuthorAsync() =>
        await PlanetMember.FindAsync(Member_Id);
}

