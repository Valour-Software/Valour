using System.Text;

namespace Valour.Client.Emojis;

public class EmojiSourceProvider
{
    private static Dictionary<string, string> _srcUrlCache = new();
    private static Dictionary<string, string> _nativeUrlCache = new();

    public static string GetSrcUrlFromNative(string nativeEmoji)
    {
        if (_nativeUrlCache.TryGetValue(nativeEmoji, out string url))
            return url;
        
        var codePoints = new List<string>();
        for (int i = 0; i < nativeEmoji.Length; i++)
        {
            int codePoint = char.ConvertToUtf32(nativeEmoji, i);
            codePoints.Add(codePoint.ToString("x").ToLower());
            if (char.IsHighSurrogate(nativeEmoji[i]))
            {
                i++; // Skip the low surrogate
            }
        }

        var code = string.Join("-", codePoints);
        
        url = GetSrcUrlByCodePoints(code);
        
        _nativeUrlCache[nativeEmoji] = url;
        
        return url;
    }
    
    public static string GetSrcUrlByCodePoints(string emojiCodePoints)
    {
        if (_srcUrlCache.TryGetValue(emojiCodePoints, out string url))
            return url;
        
        url = $"https://cdn.jsdelivr.net/npm/emoji-datasource-twitter@14.0.0/img/twitter/64/{emojiCodePoints}.png";
        _srcUrlCache[emojiCodePoints] = url;
        
        return url;
    }
}