using IdGen;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database;

public static class IdManager
{
    public static IdGenerator Generator { get; set; }

    static IdManager()
    {
        // Fun fact: This is the exact moment that SpookVooper was terminated
        // which led to the development of Valour becoming more than just a side
        // project. Viva la Vooperia.
        var epoch = new DateTime(2021, 1, 11, 4, 37, 0);

        var structure = new IdStructure(45, 10, 8);

        var options = new IdGeneratorOptions(structure, new DefaultTimeSource(epoch));

        Generator = new IdGenerator(0, options);
    }

    public static ulong Generate()
    {
        return (ulong)Generator.CreateId();
    }
}

