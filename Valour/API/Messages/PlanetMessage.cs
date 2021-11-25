using Valour.Api.Client;
using Valour.Api.Planets;
using Valour.Shared;

namespace Valour.Api.Messages;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMessage : Shared.Messages.PlanetMessage
{
    /// <summary>
    /// Returns the author of the message
    /// </summary>
    public async Task<Member> GetAuthorAsync() =>
        await Member.FindAsync(Member_Id);
}

