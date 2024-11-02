namespace Valour.Server.Services;

public class EmojiSentimentService
{
    public readonly Dictionary<string, Sentiment> SentimentMap = new()
    { 
        { "😀", new(200, 30, 0, 0, 0, 0, 20, 5) },   // U+1F600 - Grinning Face
        { "😁", new(220, 40, 0, 0, 0, 0, 10, 5) },   // U+1F601 - Beaming Face with Smiling Eyes
        { "😂", new(150, 230, 0, 0, 0, 0, 50, 0) },  // U+1F602 - Face with Tears of Joy
        { "🤣", new(130, 255, 0, 0, 0, 0, 80, 0) },  // U+1F923 - Rolling on the Floor Laughing
        { "😃", new(200, 20, 0, 0, 0, 0, 20, 5) },   // U+1F603 - Grinning Face with Big Eyes
        { "😄", new(220, 30, 0, 0, 0, 0, 10, 0) },   // U+1F604 - Grinning Face with Smiling Eyes
        { "😅", new(180, 100, 0, 10, 0, 0, 150, 5) }, // U+1F605 - Grinning Face with Sweat
        { "😆", new(200, 180, 0, 0, 0, 0, 100, 5) }, // U+1F606 - Grinning Squinting Face
        { "😉", new(170, 80, 0, 0, 0, 0, 30, 10) },  // U+1F609 - Winking Face
        { "😊", new(230, 0, 0, 0, 0, 0, 10, 5) },    // U+1F60A - Smiling Face with Smiling Eyes
        { "😋", new(180, 30, 0, 0, 0, 100, 40, 5) }, // U+1F60B - Face Savoring Food
        { "😎", new(190, 60, 0, 0, 0, 0, 30, 10) },  // U+1F60E - Smiling Face with Sunglasses
        { "😍", new(255, 0, 0, 0, 0, 0, 80, 0) },    // U+1F60D - Smiling Face with Heart-Eyes
        { "😘", new(220, 0, 0, 0, 0, 0, 40, 5) },    // U+1F618 - Face Blowing a Kiss
        { "😗", new(190, 0, 0, 0, 0, 0, 20, 5) },    // U+1F617 - Kissing Face
        { "😙", new(190, 0, 0, 0, 0, 0, 20, 5) },    // U+1F619 - Kissing Face with Smiling Eyes
        { "😚", new(190, 0, 0, 0, 0, 0, 10, 5) },    // U+1F61A - Kissing Face with Closed Eyes
        { "🙂", new(180, 0, 0, 0, 0, 0, 10, 5) },    // U+1F642 - Slightly Smiling Face
        { "🤗", new(200, 0, 0, 0, 0, 0, 20, 5) },    // U+1F917 - Hugging Face
        { "🤔", new(30, 0, 0, 0, 0, 0, 150, 10) },   // U+1F914 - Thinking Face
        { "😐", new(0, 0, 0, 0, 0, 0, 10, 200) },    // U+1F610 - Neutral Face
        { "😑", new(0, 0, 0, 0, 0, 0, 5, 210) },     // U+1F611 - Expressionless Face
        { "😶", new(0, 0, 0, 0, 0, 0, 0, 220) },     // U+1F636 - Face Without Mouth
        { "🙄", new(0, 0, 0, 80, 0, 0, 120, 10) },   // U+1F644 - Face with Rolling Eyes
        { "😏", new(30, 10, 0, 0, 0, 0, 70, 20) },   // U+1F60F - Smirking Face
        { "😣", new(0, 0, 0, 10, 20, 0, 10, 200) },  // U+1F623 - Persevering Face
        { "😥", new(0, 0, 10, 0, 0, 0, 10, 150) },   // U+1F625 - Disappointed but Relieved Face
        { "😮", new(0, 0, 0, 0, 0, 0, 200, 10) },    // U+1F62E - Face with Open Mouth
        { "🤐", new(0, 0, 0, 0, 0, 0, 0, 200) },     // U+1F910 - Zipper-Mouth Face
        { "😯", new(0, 0, 0, 0, 0, 0, 180, 10) },    // U+1F62F - Hushed Face
        { "😪", new(0, 0, 80, 0, 0, 0, 0, 100) },    // U+1F62A - Sleepy Face
        { "😫", new(0, 0, 0, 30, 0, 0, 0, 150) },    // U+1F62B - Tired Face
        { "😴", new(0, 0, 0, 0, 0, 0, 0, 180) },     // U+1F634 - Sleeping Face
        { "😌", new(160, 0, 0, 0, 0, 0, 0, 30) },    // U+1F60C - Relieved Face
        { "😛", new(140, 150, 0, 0, 0, 0, 20, 5) },  // U+1F61B - Face with Tongue
        { "😜", new(120, 200, 0, 0, 0, 0, 100, 5) }, // U+1F61C - Winking Face with Tongue
        { "😝", new(100, 220, 0, 0, 0, 0, 150, 5) }, // U+1F61D - Squinting Face with Tongue
        { "🤤", new(120, 0, 0, 0, 0, 100, 20, 0) },  // U+1F924 - Drooling Face
        { "😒", new(0, 0, 0, 80, 0, 0, 0, 200) },    // U+1F612 - Unamused Face
        { "😓", new(0, 0, 0, 0, 0, 0, 0, 190) },     // U+1F613 - Face with Cold Sweat
        { "😔", new(0, 0, 10, 0, 0, 0, 0, 170) },    // U+1F614 - Pensive Face
        { "😕", new(0, 0, 0, 0, 0, 0, 150, 20) },    // U+1F615 - Confused Face
        { "🙃", new(120, 140, 0, 0, 0, 0, 80, 5) },  // U+1F643 - Upside-Down Face
        { "🤑", new(10, 60, 0, 0, 0, 0, 40, 0) },    // U+1F911 - Money-Mouth Face
        { "😲", new(0, 0, 0, 0, 0, 0, 255, 5) },     // U+1F632 - Astonished Face
        { "☹️", new(0, 0, 10, 0, 0, 0, 0, 180) },    // U+2639 - Frowning Face
        { "🙁", new(0, 0, 20, 0, 0, 0, 0, 160) },    // U+1F641 - Slightly Frowning Face
        { "😖", new(0, 0, 40, 0, 0, 0, 0, 150) },    // U+1F616 - Confounded Face
        { "😞", new(0, 0, 30, 0, 0, 0, 0, 170) },    // U+1F61E - Disappointed Face
        { "😟", new(0, 0, 10, 0, 0, 0, 0, 160) },    // U+1F61F - Worried Face
        { "😤", new(0, 0, 0, 200, 0, 0, 50, 0) },    // U+1F624 - Face with Steam from Nose
        { "😢", new(0, 0, 255, 0, 0, 0, 20, 0) },    // U+1F622 - Crying Face
        { "😭", new(0, 0, 255, 0, 0, 0, 40, 0) },    // U+1F62D - Loudly Crying Face
        { "😦", new(0, 0, 10, 0, 20, 0, 180, 10) },  // U+1F626 - Frowning Face with Open Mouth
        { "😧", new(0, 0, 10, 0, 30, 0, 180, 10) },  // U+1F627 - Anguished Face
        { "😨", new(0, 0, 10, 0, 150, 0, 200, 5) },   // U+1F628 - Fearful Face
        { "😩", new(0, 0, 20, 40, 100, 0, 50, 5) },    // U+1F629 - Weary Face
        { "😪", new(0, 0, 60, 0, 0, 0, 10, 100) },     // U+1F62A - Sleepy Face
        { "😫", new(0, 0, 10, 50, 0, 0, 10, 150) },    // U+1F62B - Tired Face
        { "😬", new(0, 0, 0, 0, 10, 0, 30, 200) },     // U+1F62C - Grimacing Face
        { "😭", new(0, 0, 255, 0, 0, 0, 20, 5) },      // U+1F62D - Loudly Crying Face
        { "😮", new(0, 0, 0, 0, 0, 0, 250, 5) },       // U+1F62E - Face with Open Mouth
        { "😯", new(0, 0, 0, 0, 0, 0, 200, 5) },       // U+1F62F - Hushed Face
        { "😰", new(0, 0, 10, 60, 100, 0, 50, 0) },    // U+1F630 - Face with Open Mouth & Cold Sweat
        { "😱", new(0, 0, 0, 200, 180, 0, 250, 0) },   // U+1F631 - Face Screaming in Fear
        { "😲", new(0, 0, 0, 0, 0, 0, 255, 5) },       // U+1F632 - Astonished Face
        { "😳", new(0, 0, 0, 10, 30, 0, 200, 5) },     // U+1F633 - Flushed Face
        { "😴", new(0, 0, 0, 0, 0, 0, 0, 180) },       // U+1F634 - Sleeping Face
        { "😵", new(0, 0, 0, 20, 20, 0, 180, 5) },     // U+1F635 - Dizzy Face
        { "😶", new(0, 0, 0, 0, 0, 0, 0, 220) },       // U+1F636 - Face Without Mouth
        { "😷", new(0, 0, 0, 0, 0, 150, 0, 100) },     // U+1F637 - Face with Medical Mask
        { "🤒", new(0, 0, 0, 0, 0, 150, 0, 100) },     // U+1F912 - Face with Thermometer
        { "🤕", new(0, 0, 0, 30, 0, 0, 50, 100) },     // U+1F915 - Face with Head-Bandage
        { "🤢", new(0, 0, 0, 0, 0, 200, 0, 10) },      // U+1F922 - Nauseated Face
        { "🤧", new(0, 0, 0, 0, 0, 150, 0, 100) },     // U+1F927 - Sneezing Face
        { "🥵", new(0, 0, 0, 40, 0, 0, 200, 20) },     // U+1F975 - Hot Face
        { "🥶", new(0, 0, 0, 80, 150, 0, 200, 0) },    // U+1F976 - Cold Face
        { "🥴", new(0, 0, 0, 0, 0, 0, 180, 20) },      // U+1F974 - Woozy Face
        { "😵‍💫", new(0, 0, 0, 0, 0, 0, 180, 10) },   // U+1F635+200D+1F4AB - Face with Spiral Eyes
        { "🤯", new(0, 0, 0, 0, 0, 0, 255, 5) },       // U+1F92F - Exploding Head
        { "🤠", new(200, 30, 0, 0, 0, 0, 80, 5) },     // U+1F920 - Cowboy Hat Face
        { "🥳", new(220, 120, 0, 0, 0, 0, 180, 5) },   // U+1F973 - Partying Face
        { "😎", new(190, 60, 0, 0, 0, 0, 80, 5) },     // U+1F60E - Smiling Face with Sunglasses
        { "🤓", new(50, 20, 0, 0, 0, 0, 120, 100) },   // U+1F913 - Nerd Face
        { "🧐", new(10, 0, 0, 0, 0, 0, 180, 80) },     // U+1F9D0 - Face with Monocle
    };
    
    public Sentiment GetSentiment(string emoji)
    {
        if (SentimentMap.TryGetValue(emoji, out var sentiment))
            return sentiment;
       
        return new Sentiment(0, 0, 0, 0, 0, 0, 0, 0);
    }

    public float GetMultiplier(IEnumerable<string> emojis)
    {
        if (emojis is null)
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
        foreach (var emoji in emojis)
        {
            var sentiment = GetSentiment(emoji);
            joy += sentiment.Joy;
            humor += sentiment.Humor;
            sadness += sentiment.Sadness;
            anger += sentiment.Anger;
            fear += sentiment.Fear;
            disgust += sentiment.Disgust;
            surprise += sentiment.Surprise;
            neutral += sentiment.Neutral;
            total++;
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
        return Math.Clamp(totalSentiment, 0.5f, 1.5f);
    }
}