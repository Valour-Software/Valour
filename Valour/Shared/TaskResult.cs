using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared
{
    public class TaskResult
    {
        public string Response { get; set; }
        public bool Success { get; set; }

        public TaskResult(bool success, string response)
        {
            Success = success;
            Response = response;
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"[SUCC] {Response}";
            }

            return $"[FAIL] {Response}";
        }
    }
}
