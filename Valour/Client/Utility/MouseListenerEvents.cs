namespace Valour.Client.Utility;

public struct MousePosition
{
    public float ClientX { get; set; }
    public float ClientY { get; set; }
    
    public float PageX { get; set; }
    public float PageY { get; set; }
    
    public float ScreenX { get; set; }
    public float ScreenY { get; set; }
    
    // Delta is by ClientX and ClientY
    public float DeltaX { get; set; }
    public float DeltaY { get; set; }
}

public struct MouseUpEvent
{
    public float X;
    public float Y;
}