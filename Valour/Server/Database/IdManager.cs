using IdGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Valour.Server.Database
{
    public class IdManager
    {
        public static IdGenerator Generator { get; set; }

        public IdManager()
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
}
