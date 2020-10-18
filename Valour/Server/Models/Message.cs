using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Models
{
    public class Message
    {
        [Key]
        public string Hash { get; set; }
        public string Userid { get; set; }
        public int Post_time { get; set; }
    }
}
