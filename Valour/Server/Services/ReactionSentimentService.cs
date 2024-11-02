namespace Valour.Server.Services;

public class ReactionSentimentService
{
    public readonly Dictionary<string, Sentiment> SentimentMap = new()
    { 
        { "😀", new(200, 30, 10, 5, 5, 5, 30, 20) },   // U+1F600 - Grinning Face
        { "😁", new(220, 40, 5, 5, 5, 10, 20, 10) },   // U+1F601 - Beaming Face with Smiling Eyes
        { "😂", new(150, 230, 5, 5, 5, 5, 40, 10) },   // U+1F602 - Face with Tears of Joy
        { "🤣", new(130, 255, 5, 5, 5, 10, 50, 5) },   // U+1F923 - Rolling on the Floor Laughing
        { "😃", new(200, 20, 10, 5, 5, 5, 30, 20) },   // U+1F603 - Grinning Face with Big Eyes
        { "😄", new(220, 30, 5, 5, 5, 10, 20, 10) },   // U+1F604 - Grinning Face with Smiling Eyes
        { "😅", new(180, 150, 5, 10, 20, 10, 150, 5) },// U+1F605 - Grinning Face with Sweat
        { "😆", new(200, 180, 5, 5, 5, 5, 150, 5) },   // U+1F606 - Grinning Squinting Face
        { "😉", new(170, 60, 5, 5, 5, 5, 80, 10) },    // U+1F609 - Winking Face
        { "😊", new(230, 10, 5, 5, 5, 5, 40, 10) },    // U+1F60A - Smiling Face with Smiling Eyes
        { "😋", new(180, 50, 5, 5, 5, 100, 60, 5) },   // U+1F60B - Face Savoring Food
        { "😎", new(190, 80, 5, 5, 5, 5, 50, 10) },    // U+1F60E - Smiling Face with Sunglasses
        { "😍", new(255, 20, 5, 5, 5, 5, 100, 5) },    // U+1F60D - Smiling Face with Heart-Eyes
        { "😘", new(220, 10, 5, 5, 5, 5, 60, 5) },     // U+1F618 - Face Blowing a Kiss
        { "😗", new(190, 10, 5, 5, 5, 5, 40, 10) },    // U+1F617 - Kissing Face
        { "😙", new(190, 10, 5, 5, 5, 5, 40, 10) },    // U+1F619 - Kissing Face with Smiling Eyes
        { "😚", new(190, 10, 5, 5, 5, 5, 30, 5) },     // U+1F61A - Kissing Face with Closed Eyes
        { "🙂", new(180, 5, 5, 5, 5, 5, 20, 10) },     // U+1F642 - Slightly Smiling Face
        { "🤗", new(200, 20, 5, 5, 5, 5, 20, 10) },    // U+1F917 - Hugging Face
        { "🤔", new(30, 10, 10, 5, 10, 5, 150, 20) },  // U+1F914 - Thinking Face
        { "😐", new(10, 5, 10, 5, 5, 5, 10, 200) },    // U+1F610 - Neutral Face
        { "😑", new(5, 5, 20, 5, 5, 5, 5, 210) },      // U+1F611 - Expressionless Face
        { "😶", new(5, 5, 5, 5, 5, 5, 5, 220) },       // U+1F636 - Face Without Mouth
        { "🙄", new(5, 5, 10, 20, 5, 5, 100, 10) },    // U+1F644 - Face with Rolling Eyes
        { "😏", new(30, 10, 5, 5, 5, 5, 80, 20) },     // U+1F60F - Smirking Face
        { "😣", new(5, 5, 5, 10, 20, 5, 10, 200) },    // U+1F623 - Persevering Face
        { "😥", new(5, 5, 5, 10, 5, 5, 10, 150) },     // U+1F625 - Disappointed but Relieved Face
        { "😮", new(5, 10, 5, 5, 10, 5, 200, 10) },    // U+1F62E - Face with Open Mouth
        { "🤐", new(5, 5, 5, 5, 5, 5, 5, 200) },       // U+1F910 - Zipper-Mouth Face
        { "😯", new(5, 5, 5, 5, 10, 5, 180, 10) },     // U+1F62F - Hushed Face
        { "😪", new(5, 5, 50, 5, 5, 5, 10, 100) },     // U+1F62A - Sleepy Face
        { "😫", new(5, 5, 10, 20, 5, 5, 5, 150) },     // U+1F62B - Tired Face
        { "😴", new(5, 5, 5, 5, 5, 5, 5, 180) },       // U+1F634 - Sleeping Face
        { "😌", new(160, 5, 5, 5, 5, 5, 5, 30) },      // U+1F60C - Relieved Face
        { "😛", new(140, 150, 5, 5, 5, 5, 10, 5) },    // U+1F61B - Face with Tongue
        { "😜", new(120, 200, 5, 5, 5, 5, 50, 5) },    // U+1F61C - Winking Face with Tongue
        { "😝", new(100, 220, 5, 5, 5, 5, 80, 5) },    // U+1F61D - Squinting Face with Tongue
        { "🤤", new(120, 5, 5, 5, 5, 100, 10, 5) },    // U+1F924 - Drooling Face
        { "😒", new(10, 5, 5, 80, 5, 5, 5, 200) },     // U+1F612 - Unamused Face
        { "😓", new(5, 5, 5, 5, 5, 5, 5, 190) },       // U+1F613 - Face with Cold Sweat
        { "😔", new(5, 5, 5, 5, 5, 5, 5, 170) },       // U+1F614 - Pensive Face
        { "😕", new(5, 5, 10, 5, 5, 5, 150, 20) },     // U+1F615 - Confused Face
        { "🙃", new(120, 140, 5, 5, 5, 5, 60, 5) },    // U+1F643 - Upside-Down Face
        { "😨", new(0, 0, 10, 50, 150, 0, 200, 5) },   // U+1F628 - Fearful Face
        { "😩", new(0, 0, 20, 40, 100, 0, 50, 5) },    // U+1F629 - Weary Face
        { "😬", new(0, 0, 0, 0, 10, 0, 30, 200) },     // U+1F62C - Grimacing Face
        { "😭", new(0, 0, 255, 0, 0, 0, 40, 0) },      // U+1F62D - Loudly Crying Face
        { "😰", new(0, 0, 10, 60, 100, 0, 50, 0) },    // U+1F630 - Face with Open Mouth & Cold Sweat
        { "😱", new(0, 0, 0, 200, 180, 0, 255, 0) },   // U+1F631 - Face Screaming in Fear
        { "😲", new(0, 0, 0, 0, 0, 0, 255, 5) },       // U+1F632 - Astonished Face
        { "😳", new(0, 0, 0, 10, 30, 0, 200, 5) },     // U+1F633 - Flushed Face
        { "😵", new(0, 0, 0, 20, 20, 0, 180, 5) },     // U+1F635 - Dizzy Face
        { "😷", new(0, 0, 0, 0, 0, 150, 0, 100) },     // U+1F637 - Face with Medical Mask
        { "😸", new(200, 50, 0, 0, 0, 0, 20, 0) },     // U+1F638 - Grinning Cat Face with Smiling Eyes
        { "😹", new(150, 200, 0, 0, 0, 0, 50, 0) },    // U+1F639 - Cat Face with Tears of Joy
        { "😺", new(220, 0, 0, 0, 0, 0, 10, 5) },      // U+1F63A - Smiling Cat Face with Open Mouth
        { "😻", new(255, 0, 0, 0, 0, 0, 80, 0) },      // U+1F63B - Smiling Cat Face with Heart-Eyes
        { "😼", new(100, 0, 0, 50, 0, 0, 60, 20) },    // U+1F63C - Cat Face with Wry Smile
        { "😽", new(200, 0, 0, 0, 0, 0, 10, 5) },      // U+1F63D - Kissing Cat Face with Closed Eyes
        { "😾", new(0, 0, 40, 150, 0, 0, 10, 5) },     // U+1F63E - Pouting Cat Face
        { "😿", new(0, 0, 200, 0, 0, 0, 10, 5) },      // U+1F63F - Crying Cat Face
        { "🙀", new(0, 0, 0, 0, 180, 0, 200, 5) },     // U+1F640 - Weary Cat Face
        { "🙁", new(0, 0, 50, 0, 0, 0, 10, 180) },     // U+1F641 - Slightly Frowning Face
        { "🙅", new(0, 0, 0, 150, 0, 0, 0, 50) },      // U+1F645 - Person Gesturing No
        { "🤫", new(0, 0, 0, 0, 0, 0, 20, 200) },      // U+1F92B - Shushing Face
        { "🤮", new(0, 0, 0, 0, 0, 255, 0, 10) },      // U+1F92E - Face Vomiting
        { "🤯", new(0, 0, 0, 0, 0, 0, 255, 5) },       // U+1F92F - Exploding Head
        { "🥰", new(255, 0, 0, 0, 0, 0, 150, 10) },    // U+1F970 - Smiling Face with Hearts
        { "🥵", new(0, 0, 0, 50, 0, 0, 200, 10) },     // U+1F975 - Hot Face
        { "🥶", new(0, 0, 0, 0, 150, 0, 200, 10) },    // U+1F976 - Cold Face
        { "🥳", new(220, 150, 0, 0, 0, 0, 200, 10) },  // U+1F973 - Partying Face
        { "🥺", new(0, 0, 200, 0, 0, 0, 80, 20) },     // U+1F97A - Pleading Face
        { "🧐", new(0, 0, 0, 0, 0, 0, 200, 150) }      // U+1F9D0 - Face with Monocle
    };
    
    public Sentiment GetSentiment(string emoji)
    {
        if (SentimentMap.TryGetValue(emoji, out var sentiment))
            return sentiment;
       
        return new Sentiment(0, 0, 0, 0, 0, 0, 0, 0);
    }

    public float GetMultiplier(IEnumerable<ReactionTotal> reactions)
    {
        if (reactions is null)
            return 1f;

        var joy = 0f;
        var humor = 0f;
        var sadness = 0f;
        var anger = 0f;
        var fear = 0f;
        var disgust = 0f;
        var surprise = 0f;
        var neutral = 0f;

        var total = 0;
        foreach (var reaction in reactions)
        {
            var sentiment = GetSentiment(reaction.Emoji);
            joy += sentiment.Joy * reaction.Count;
            humor += sentiment.Humor * reaction.Count;
            sadness += sentiment.Sadness * reaction.Count;
            anger += sentiment.Anger * reaction.Count;
            fear += sentiment.Fear * reaction.Count;
            disgust += sentiment.Disgust * reaction.Count;
            surprise += sentiment.Surprise * reaction.Count;
            neutral += sentiment.Neutral * reaction.Count;
            total += reaction.Count;
        }

        // doing this to pull values into 0-1 range
        total *= 255;

        var avgJoy = joy / total;
        var avgHumor = humor / total;
        var avgSadness = sadness / total;
        var avgAnger = anger / total;
        var avgFear = fear / total;
        var avgDisgust = disgust / total;
        var avgSurprise = surprise / total;
        var avgNeutral = neutral / total;
        
        // range is -4 to 4
        var totalSentiment = ((avgJoy + avgSurprise + avgNeutral + avgHumor) - (avgSadness + avgAnger + avgFear + avgDisgust));
        // range is 0 to 8
        totalSentiment += 4;
        // range is 0 to 2
        totalSentiment /= 4f;
        // output range is 0.5 to 1.5
        return Math.Clamp(totalSentiment, 0.5f, 1.5f) + SigmoidApprox(total, 2f);
    }
    
    // x: input value, a: steepness, b: horizontal shift, c: vertical scale.
    private static float SigmoidApprox(float x, float a = 1f, float b = 0f, float c = 1f)
    {
        // Shift and scale x to match the typical sigmoid range
        x = a * (x - b);

        // Use a fast polynomial approximation for the sigmoid
        // We use x / (1 + |x|) to approximate the shape of a sigmoid
        var result = x / (1f + MathF.Abs(x));

        // Scale the result to the vertical range [0, c]
        return (result + 1f) * (c / 2f); // Map to [0, c] instead of [-c/2, c/2]
    }
}