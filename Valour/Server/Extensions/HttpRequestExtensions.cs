using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Extensions
{
    public static class HttpRequestExtensions
    { 
        public static async Task<string> ReadBodyStringAsync(this HttpRequest request)
        {
            string val;

            using (var reader = new StreamReader(request.Body))
            {
                val = await reader.ReadToEndAsync();
            }

            return val;
        }
    }
}
