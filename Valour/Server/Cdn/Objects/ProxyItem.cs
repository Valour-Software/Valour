using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Server.Cdn.Objects;

[Table("proxies")]
public class ProxyItem
{
    /// <summary>
    /// The id of proxied items are sha256 hashes of the original url
    /// </summary>
    [Key]
    [Column("id")]
    public string Id { get; set; }

    /// <summary>
    /// The original url fed to the proxy server
    /// </summary>
    [Column("origin")]
    public string Origin { get; set; }

    /// <summary>
    /// The type of content at the origin
    /// </summary>
    [Column("mime_type")]
    public string MimeType { get; set; }

    /// <summary>
    /// The url for the proxied item
    /// </summary>
    [JsonIgnore]
    public string Url => $"https://cdn.valour.gg/proxy/{Id}";
}

