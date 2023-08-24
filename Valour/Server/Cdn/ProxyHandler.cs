using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Markdig.Extensions.MediaLinks;
using Valour.Api.Models;
using Valour.Server.Cdn.Objects;
using Valour.Shared.Cdn;
using Valour.Shared.Models;
using SixLabors.ImageSharp;

namespace Valour.Server.Cdn;

public static class ProxyHandler
{
    private static readonly SHA256 Sha256 = SHA256.Create();
    private static readonly HttpClient Http = new HttpClient();

    static ProxyHandler()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("ValourCDN/1.0");
    }

    public static async Task<List<MessageAttachment>> GetUrlAttachmentsFromContent(string url, CdnDb db, HttpClient client)
    {
        var urls = CdnUtils.UrlRegex.Matches(url);

        List<MessageAttachment> attachments = null;
        
        foreach (Match match in urls)
        {
            var attachment = await GetAttachmentFromUrl(match.Value, db, client);
            if (attachment != null)
            {
                if (attachments is null)
                    attachments = new();
                
                attachments.Add(attachment);
            }
        }

        return attachments;
    }

    /// <summary>
    /// Given a url, returns the attachment object
    /// This can be an image, video, or even an embed website view
    /// Also handles converting to ValourCDN when necessary
    /// </summary>
    public static async Task<MessageAttachment> GetAttachmentFromUrl(string url, CdnDb db, HttpClient client)
    {
        var uri = new Uri(url.Replace("www.", ""));

        // Determine if this is a 'virtual' attachment. Like YouTube!
        // these attachments do not have an actual file - usually they use an iframe.
        var isVirtual = CdnUtils.VirtualAttachmentMap.TryGetValue(uri.Host, out var virtualType);
        if (isVirtual)
        {
            var attachment = new MessageAttachment(virtualType);
            switch (virtualType)
            {
                case MessageAttachmentType.Reddit:
                {
                    try
                    {
                        // Get oembed data from reddit
                        var route = "https://www.reddit.com/oembed?url=" + HttpUtility.UrlEncode(url);
                        var oembedData =
                            await Http.GetFromJsonAsync<OembedData>(route);

                        attachment.Html = oembedData.Html;

                        return attachment;
                    }
                    catch (Exception ex)
                    {
                        return null;
                    }
                }
                case MessageAttachmentType.Twitter:
                {
                    try
                    {
                        // Get oembed data from twitter
                        var oembedData =
                            await Http.GetFromJsonAsync<OembedData>("https://publish.twitter.com/oembed?url=" + url + 
                                                                    "&theme=dark&dnt=true&omit_script=true&maxwidth=400&maxheight=400&limit=1&hide_thread=true");
                        attachment.Location = url;
                        attachment.Html = oembedData.Html;
                    }
                    catch (System.Exception ex)
                    {
                        return null;
                    }

                    break;
                }
                case MessageAttachmentType.YouTube:
                {
                    // Youtube uses ?v= or /embed/
                    if (uri.Query.Contains("v="))
                    {
                        var query = HttpUtility.ParseQueryString(uri.Query);
                        attachment.Location = $"https://www.youtube.com/embed/{query["v"]}";
                    }
                    else
                    {
                        attachment.Location = $"https://www.youtube.com/embed/{uri.Segments.Last()}";
                    }

                    break;
                }
                case MessageAttachmentType.Vimeo:
                {
                    attachment.Location = $"https://player.vimeo.com/video/{uri.Segments.Last()}";
                    break;
                }
                case MessageAttachmentType.Twitch:
                {
                    // clip
                    if (uri.AbsolutePath.StartsWith("/clip/"))
                    {
                        attachment.Location = $"https://clips.twitch.tv/embed?clip=={uri.AbsolutePath.Replace("/clip/", "")}";
                    }
                    // video
                    else if (uri.AbsolutePath.StartsWith("/videos/"))
                    {
                        attachment.Location = $"https://player.twitch.tv/?video={uri.AbsolutePath.Replace("/videos/", "")}";
                    }
                    // collection
                    else if (uri.AbsolutePath.StartsWith("/collections/"))
                    {
                        attachment.Location = $"https://player.twitch.tv/?collections={uri.AbsolutePath.Replace("/collections/", "")}";
                    }
                    // channel
                    else
                    {
                        attachment.Location = $"https://player.twitch.tv/?channel={uri.AbsolutePath.Substring(1)}";
                    }

                    break;
                }
            }

            // Enough data to build iframes and such
            return attachment;
        }
        else
        {
            // We have to determine the type of the attachment
            var name = Path.GetFileName(uri.AbsoluteUri);
            var ext = Path.GetExtension(name).ToLower();
            
            // Try to get media type
            CdnUtils.ExtensionToMimeType.TryGetValue(ext, out var mime);

            // Default type is file
            var type = MessageAttachmentType.File;
            
            // It's not media, so we just treat it as a link
            if (mime is null || mime.Length < 4)
            {
                return null;
            }
            // Is media
            else
            {
                // Determine if audio or video or image
                
                // We only actually need to check the first letter,
                // since only 'image/' starts with i
                if (mime[0] == 'i')
                {
                    type = MessageAttachmentType.Image;
                }
                // Same thing here - only 'video/' starts with v
                else if (mime[0] == 'v')
                {
                    type = MessageAttachmentType.Video;
                }
                // Unfortunately 'audio/' and 'application/' both start with 'a'
                else if (mime[0] == 'a' && mime[1] == 'u')
                {
                    type = MessageAttachmentType.Audio;
                }
            }
            
            // Bypass our own CDN
            if (CdnUtils.MediaBypassList.Contains(uri.Host))
            {
                return new MessageAttachment(type)
                {
                    Location = url,
                    MimeType = mime,
                    FileName = name,
                };
            }
            // Use our own CDN
            else
            {
                // Get hash from uri
                var h = Sha256.ComputeHash(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
                var hash = BitConverter.ToString(h).Replace("-", "").ToLower();

                var attachment = new MessageAttachment(type)
                {
                    MimeType = mime,
                    FileName = name,
                };
                
                // Check if we have already proxied this item
                var item = await db.ProxyItems.FindAsync(hash);
                if (item is null)
                {
                    if (type == MessageAttachmentType.Image)
                    {
                        // If it's an image, we have to get its height and width
                        var response = await client.GetAsync(uri.AbsoluteUri);
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine("Content Proxy error: " + await response.Content.ReadAsStringAsync());
                            return null;
                        }
                        
                        try
                        {
                            var stream = await response.Content.ReadAsStreamAsync();
                            var imageInfo = await Image.IdentifyAsync(stream);
                            attachment.Width = imageInfo.Width;
                            attachment.Height = imageInfo.Height;
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    
                    item = new ProxyItem()
                    {
                        Id = hash + ext,
                        Origin = uri.AbsoluteUri,
                        MimeType = mime,
                        Width = attachment.Width,
                        Height = attachment.Height
                    };

                    await db.AddAsync(item);
                    await db.SaveChangesAsync();

                    attachment.Location = $"https://cdn.valour.gg/proxy/{hash}{ext}";
                }
                else
                {
                    attachment.Location = item.Url;
                    attachment.Width = item.Width ?? 0;
                    attachment.Height = item.Height ?? 0;
                }

                return attachment;
            }
        }
    }
}
