namespace Valour.Server.Models;

public struct Sentiment
{
    public byte Joy;
    public byte Humor;
    public byte Sadness;
    public byte Anger;
    public byte Fear;
    public byte Disgust;
    public byte Surprise;
    public byte Neutral;
    
    public Sentiment(byte joy, byte humor, byte sadness, byte anger, byte fear, byte disgust, byte surprise, byte neutral)
    {
        Joy = joy;
        Humor = humor;
        Sadness = sadness;
        Anger = anger;
        Fear = fear;
        Disgust = disgust;
        Surprise = surprise;
        Neutral = neutral;
    }

    /*
    public float GetMultiplier()
    {
        var value = ((Joy + Surprise + Neutral) - (Sadness + Anger + Fear + Disgust));
        var clamped = Math.Clamp(value, -650, 500);
        var s = clamped / 650f; // gives a range of -1 to 0.77
        return s + 1;
    }
    */
}