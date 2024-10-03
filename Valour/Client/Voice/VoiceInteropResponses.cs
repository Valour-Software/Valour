namespace Valour.Client.Voice;

public class BaseResponse
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}

public class JoinRoomResponse : BaseResponse
{
}

public class SendCameraStreamResponse : BaseResponse
{
}

public class StartScreenshareResponse : BaseResponse
{
    public bool SupportsAudio { get; set; }
}

public class StartCameraResponse : BaseResponse
{
}

public class CycleCameraResponse : BaseResponse
{
}

public class StopStreamsResponse : BaseResponse
{
}

public class LeaveRoomResponse : BaseResponse
{
}

public class SubscribeToTrackResponse : BaseResponse
{
    /// <summary>
    /// Type of consumer created. Can be audio or video.
    /// </summary>
    public string Kind { get; set; }
}

public class UnsubscribeFromTrackResponse : BaseResponse
{
}
