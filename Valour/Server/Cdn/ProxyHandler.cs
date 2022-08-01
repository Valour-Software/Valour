using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.Security.Cryptography;
using System.Text;
using Valour.Server.Cdn.Objects;
using Valour.Shared;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn;

public static class ProxyHandler
{
    static SHA256 SHA256 = SHA256.Create();

    public static async Task<string> HandleUrls(string content, HttpClient client, CdnDb db)
    {
        //Console.WriteLine("content: " + content);

        MatchCollection matches = CdnUtils.Url_Regex.Matches(content);

        foreach (Match match in matches)
        {
            bool bypass = false;

            foreach (string s in CdnUtils.Media_Bypass)
            {
                var m = match.Value;

                //Console.WriteLine("pm: " + m);

                bool cm = m.StartsWith("![](") && m.EndsWith(')');

                //Console.WriteLine("cm: " + cm);

                if (cm)
                {
                    m = m.Substring(0, m.Length - 1);
                    m = m.Substring(4, m.Length - 4);
                }

                //Console.WriteLine("m: " + m);

                if (m.ToLower().Replace("www.", "").StartsWith(s))
                {
                    if (!cm)
                        content = content.Replace(m, $"![]({m})");

                    bypass = true;

                    break;
                }
            }
            if (!bypass)
            {
                byte[] h = SHA256.ComputeHash(Encoding.UTF8.GetBytes(match.Value));
                string hash = BitConverter.ToString(h).Replace("-", "").ToLower();

                ProxyItem item = await db.ProxyItems.FindAsync(hash);

                if (item is null)
                {
                    // Check if end resource is media
                    var response = await client.GetAsync(match.Value);

                    // If failure, return the reason and stop
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Proxy error: " + await response.Content.ReadAsStringAsync());
                        content = "[Proxy error]";
                        continue;
                    }

                    IEnumerable<string> contentTypes;

                    response.Content.Headers.TryGetValues("Content-Type", out contentTypes);

                    string content_type = contentTypes.FirstOrDefault().Split(';')[0];

                    item = new ProxyItem()
                    {
                        Id = hash,
                        Origin = match.Value,
                        MimeType = content_type
                    };

                    await db.AddAsync(item);
                    await db.SaveChangesAsync();
                }

                if (CdnUtils.Media_Types.Contains(item.MimeType))
                {
                    content = content.Replace(match.Value, $"![]({item.Url})");
                }
            }
        }

        return content;
    }
}
