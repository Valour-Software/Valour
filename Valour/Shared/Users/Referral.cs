using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Users
{
    public class Referral
    {
        public ulong Id { get; set; }
        public ulong User_Id { get; set; }
        public ulong Referrer_Id { get; set; }
    }
}
