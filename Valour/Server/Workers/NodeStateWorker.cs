using StackExchange.Redis;
using Valour.Database;
using Valour.Server.Database;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Workers;

/// <summary>
/// Updates the node state in redis every 30 seconds
/// </summary>
public class NodeStateWorker : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NodeStateWorker> _logger;
    private NodeService _longNodeService; // This can ONLY be used for redis operations. Database-dependent
                                                   // operations will cause scoping issues
    
    
    // Timer for executing timed tasks
    private Timer _timer;
    
    public NodeStateWorker(ILogger<NodeStateWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        _longNodeService = scope.ServiceProvider.GetRequiredService<NodeService>();

        
        var db = scope.ServiceProvider.GetRequiredService<ValourDB>();

        /*
        var npMessages = db.PlanetMessages.Select(x => new Message()
        {
            Id = x.Id,
            PlanetId = x.PlanetId,
            ReplyToId = x.ReplyToId,
            AuthorUserId = x.AuthorUserId,
            AuthorMemberId = x.AuthorMemberId,
            Content = x.Content,
            TimeSent = x.TimeSent.ToUniversalTime(),
            ChannelId = x.ChannelId,
            EmbedData = x.EmbedData,
            MentionsData = x.MentionsData,
            AttachmentsData = x.AttachmentsData,
            EditedTime = x.EditedTime == null ? null : x.EditedTime.Value.ToUniversalTime(),
        });
        
        db.Messages.AddRange(npMessages);
        
        var dMessages = db.DirectMessages.Select(x => new Message()
        {
            Id = x.Id,
            PlanetId = null,
            ReplyToId = x.ReplyToId,
            AuthorUserId = x.AuthorUserId,
            AuthorMemberId = null,
            Content = x.Content,
            TimeSent = x.TimeSent.ToUniversalTime(),
            ChannelId = x.ChannelId,
            EmbedData = x.EmbedData,
            MentionsData = x.MentionsData,
            AttachmentsData = x.AttachmentsData,
            EditedTime = x.EditedTime == null ? null : x.EditedTime.Value.ToUniversalTime(),
        });
        
        db.Messages.AddRange(dMessages);
        
        var pCategoryChannels = db.PlanetCategories.IgnoreQueryFilters().Select(x => new NewChannel()
           {
           Id = x.Id,
           Name = x.Name,
           Description = x.Description,
           ChannelType = ChannelTypeEnum.PlanetCategory,
           LastUpdateTime = x.TimeLastActive.ToUniversalTime(),
           IsDeleted = x.IsDeleted,
           
           PlanetId = x.PlanetId,
           ParentId = x.ParentId,
           Position = x.Position,
           InheritsPerms = x.InheritsPerms,
           IsDefault = false
        });
           
        db.NewChannels.AddRange(pCategoryChannels);

        var pChatChannels = db.PlanetChatChannels.IgnoreQueryFilters().Select(x => new NewChannel()
        {
            Id = x.Id,
            Name = x.Name,
            Description = x.Description,
            ChannelType = ChannelTypeEnum.PlanetChat,
            LastUpdateTime = x.TimeLastActive.ToUniversalTime(),
            IsDeleted = x.IsDeleted,
            
            PlanetId = x.PlanetId,
            ParentId = x.ParentId,
            Position = x.Position,
            InheritsPerms = x.InheritsPerms,
            IsDefault = x.IsDefault
        });
        
        db.NewChannels.AddRange(pChatChannels);
        
        var pVoiceChannels = db.PlanetVoiceChannels.IgnoreQueryFilters().Select(x => new NewChannel()
           {
           Id = x.Id,
           Name = x.Name,
           Description = x.Description,
           ChannelType = ChannelTypeEnum.PlanetVoice,
           LastUpdateTime = x.TimeLastActive.ToUniversalTime(),
           IsDeleted = x.IsDeleted,
           
           PlanetId = x.PlanetId,
           ParentId = x.ParentId,
           Position = x.Position,
           InheritsPerms = x.InheritsPerms,
           IsDefault = false
        });
           
        db.NewChannels.AddRange(pVoiceChannels);

        var dChannels = db.DirectChatChannels.IgnoreQueryFilters().Select(x => new NewChannel()
        {
            Id = x.Id,
            Name = "Direct Chat",
            Description = "Talk with your friend",
            ChannelType = ChannelTypeEnum.DirectChat,
            LastUpdateTime = x.TimeLastActive.ToUniversalTime(),
            IsDeleted = x.IsDeleted,
           
            PlanetId = null,
            ParentId = null,
            Position = null,
            InheritsPerms = null,
            IsDefault = null
        });
           
        db.NewChannels.AddRange(dChannels);
        
        await db.SaveChangesAsync();
        
        foreach (var d in dChannels)
        {
           var m1 = new ChannelMember()
           {
           Id = IdManager.Generate(),
           ChannelId = d.Id,
           UserId = d.UserOneId,
           };
           
           var m2 = new ChannelMember()
           {
           Id = IdManager.Generate(),
           ChannelId = d.Id,
           UserId = d.UserTwoId,
           };
           
           db.ChannelMembers.AddRange(m1, m2);
        }
       
        await db.SaveChangesAsync();
        
        */
        
        /*
        foreach (var account in await db.EcoAccounts.IgnoreQueryFilters().Where(x => x.PlanetMemberId == null).ToListAsync())
        {
            if (account.PlanetMemberId is not null)
                continue;
            
            var member = await db.PlanetMembers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.PlanetId == account.PlanetId && x.UserId == account.UserId);
            if (member is null)
                continue;
            
            account.PlanetMemberId = member.Id;
            
            await db.SaveChangesAsync();
            
            Console.WriteLine($"Migrated account {account.Id}");
        }
        
        Console.WriteLine("Finished migrate");
        */
        
        var dChannels = await db.DirectChatChannels.IgnoreQueryFilters().ToListAsync();
        
        foreach (var d in dChannels)
        {
            if (!await db.ChannelMembers.AnyAsync(x => x.ChannelId == d.Id && x.UserId == d.UserOneId))
            {
                var m1 = new ChannelMember()
                {
                    Id = IdManager.Generate(),
                    ChannelId = d.Id,
                    UserId = d.UserOneId,
                };

                db.ChannelMembers.Add(m1);
            }

            if (!await db.ChannelMembers.AnyAsync(x => x.ChannelId == d.Id && x.UserId == d.UserTwoId))
            {
                var m2 = new ChannelMember()
                {
                    Id = IdManager.Generate(),
                    ChannelId = d.Id,
                    UserId = d.UserTwoId,
                };
                
                db.ChannelMembers.Add(m2);
            }
            
            Console.WriteLine("added");
        }
       
        await db.SaveChangesAsync();
        
        await _longNodeService.AnnounceNode();
        
        _timer = new Timer(UpdateNodeState, null, TimeSpan.Zero,
            TimeSpan.FromSeconds(30));
    }
    
    private async void UpdateNodeState(object? state)
    {
        _logger.LogInformation("Updating Node State.");
        await _longNodeService.UpdateNodeAliveAsync();
    }
    
    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Node State Worker is Stopping.");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }
        
    public void Dispose()
    {
        _timer?.Dispose();
    }
}