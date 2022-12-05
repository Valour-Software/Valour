namespace Valour.Client.Voice;

public class ActiveSpeaker
{
    public string PeerId { get; set; }
    public string ProducerId { get; set; }
    public int? Volume { get; set; }
}

public class MediaPeer
{
    public long JoinTs { get; set; }
    public long LastSeenTs { get; set; }

    // Track name, encoding
    public Dictionary<string, Media> Media {  get; set; }

    public Dictionary<string, List<Stat>> Stats { get; set; }
}

public class Stat
{
    public int Bitrate { get; set; }
    public int FractionLost { get; set; }
    public int Jitter { get; set; }
    public string Rid { get; set; }
    public int Score { get; set; }
}

public class Media
{
    public List<Encoding> Encodings { get; set; }
    bool Paused { get; set; }
}

public class Encoding
{
    // Video & Audio
    public bool Dtx { get; set; }

    // Video Encoding
    public bool Active { get; set; }
    public int ScaleResolutionDownBy { get; set; }
    public int MaxBitrate { get; set; }
    public string Rid { get; set; }
    public string ScalabilityMode { get; set; }


    // Audio Encoding
    public long Ssrc { get; set; }
}