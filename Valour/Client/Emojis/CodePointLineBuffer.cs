namespace Valour.Client.Emojis;

public class CodePointLineBuffer
{
    public int[] CodePoints;
    public int[] CodePointToCharIndex;
    public int CodePointCount;
    public string Text;

    public CodePointLineBuffer(string text)
    {
        Text = text;
        // Worst case: every char is a code point
        CodePoints = new int[text.Length];
        CodePointToCharIndex = new int[text.Length];
        int cp = 0;
        for (int i = 0; i < text.Length;)
        {
            CodePointToCharIndex[cp] = i;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                CodePoints[cp++] = char.ConvertToUtf32(text[i], text[i + 1]);
                i += 2;
            }
            else
            {
                CodePoints[cp++] = text[i];
                i += 1;
            }
        }
        CodePointCount = cp;
    }
}