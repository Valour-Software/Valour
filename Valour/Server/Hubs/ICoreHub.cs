using Valour.Api.Models.Messages.Embeds;
using Valour.Server.Database;
using Valour.Shared;

namespace Valour.Server.Hubs;

public interface ICoreHub
{
    public CoreHub GetCoreHub();
    
    public Task<TaskResult> JoinUser();
    Task LeaveUser();

    Task<TaskResult> JoinPlanet(long planetId);
    Task LeavePlanet(long planetId);

    Task<TaskResult> JoinChannel(long channelId);
    Task LeaveChannel(long channelId);

    Task JoinInteractionGroup(long planetId);
    Task LeaveInteractionGroup(long planetId);

    void RelayMessage(Message message);
    void NotifyMessageDeletion(Message message);
    
    void RelayDirectMessage(DirectMessage message, long targetUserId);
    void NotifyDirectMessageDeletion(DirectMessage message, long targetUserId);

    void NotifyUserChange(User user, int flags = 0);
    void NotifyUserDelete(User user);
    void NotifyUserChannelStateUpdate(long userId, UserChannelState state);

    void NotifyPlanetItemChange(Valour.Api.Models.IPlanetModel model, int flags = 0);
    void NotifyPlanetItemDelete(Valour.Api.Models.IPlanetModel model);

    void NotifyPlanetChange(Planet item, int flags = 0);
    void NotifyPlanetDelete(Planet item);

    void NotifyInteractionEvent(EmbedInteractionEvent interaction);

    void NotifyPersonalEmbedUpdateEvent(PersonalEmbedUpdate update);
    void NotifyChannelEmbedUpdateEvent(ChannelEmbedUpdate update);

    void UpdateChannelsWatching();
    void UpdateCurrentlyTypingChannels();
}