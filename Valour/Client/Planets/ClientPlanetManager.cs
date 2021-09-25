using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;
using System.Collections.Concurrent;
using AutoMapper;
using Valour.Api.Planets;
using Valour.Api.Authorization.Roles;

namespace Valour.Client.Planets
{
    public class PlanetManager
    {
        public static PlanetManager Current;

        public PlanetManager()
        {
            Current = this;
        }

        private List<ChatChannelWindow> OpenPlanetChatWindows = new();
        private ConcurrentDictionary<ulong, Role> RolesCache = new();

        public event Func<Planet, Task> OnPlanetChange;

        public event Func<Planet, Task> OnPlanetClose;

        public event Func<Planet, Task> OnPlanetOpen;

        public event Func<Task> OnChannelsUpdate;

        public event Func<Task> OnChannelWindowUpdate;

        public event Func<Task> OnCategoriesUpdate;

        public event Func<Role, Task> OnRoleUpdate;
        public event Func<Role, Task> OnRoleDeletion;

        public event Func<Member, Task> OnMemberUpdate;

        public event Func<Planet, Task> OnPlanetUpdate;

        public event Func<Channel, Task> OnChatChannelUpdate;
        public event Func<Channel, Task> OnChatChannelDeletion;
        public event Func<Category, Task> OnCategoryDeletion;

        public event Func<Category, Task> OnCategoryUpdate;

        private readonly SignalRManager signalRManager;

        public PlanetManager(SignalRManager signalrmanager)
        {
            signalRManager = signalrmanager;

            signalRManager.hubConnection.On<string>("Relay", OnMessageRecieve);
            signalRManager.hubConnection.On<string>("RoleUpdate", UpdateRole);
            signalRManager.hubConnection.On<string>("RoleDeletion", RoleDeletion);
            signalRManager.hubConnection.On<string>("MemberUpdate", UpdateMember);
            signalRManager.hubConnection.On<string>("ChatChannelUpdate", UpdateChatChannel);
            signalRManager.hubConnection.On<string>("CategoryUpdate", UpdateCategory);
            signalRManager.hubConnection.On<string>("ChatChannelDeletion", ChatChannelDeletion);
            signalRManager.hubConnection.On<string>("CategoryDeletion", CategoryDeletion);
            signalRManager.hubConnection.On<string>("PlanetUpdate", UpdatePlanet);

            ClientCache.Members.TryAdd(ulong.MaxValue, new Member()
            {
                Nickname = "Victor",
                Id = ulong.MaxValue,
                Member_Pfp = "/media/victor-cyan.png"
            });
        }

        public List<Planet> GetOpenPlanets()
        {
            return OpenPlanets.Values.ToList();
        }

        public async Task SetUpdatedMember(Member member)
        {
            string dual_key = $"{member.Planet_Id}-{member.User_Id}";

            PlanetMemberDualCache[dual_key] = member;
            ClientCache.Members[member.Id] = member;
        }

        public async Task SetUpdatedRole(Role role)
        {
            RolesCache[role.Id] = role;
        }


        public async Task ReplacePlanetChatChannel(ChatChannelWindow window, Channel oldChannel, Channel newChannel)
        {
            if (oldChannel.Id == newChannel.Id)
                return;

            Console.WriteLine("Swapping chat channel " + oldChannel.Name + " for " + newChannel.Name);

            bool close_planet = true;

            if (oldChannel.Planet_Id == newChannel.Planet_Id)
                close_planet = false;

            await ClosePlanetChatChannel(window, oldChannel, close_planet);
            await OpenPlanetChatChannel(window);
        }

        public async Task OpenPlanetChatChannel(ChatChannelWindow window)
        {
            Channel channel = window.Channel;

            if (!OpenPlanetChatChannels.ContainsKey(channel.Id))
            {
                Planet planet = await channel.GetPlanetAsync();

                // Ensure planet is opened
                await OpenPlanet(planet);

                // Joins channel via SignalR
                await signalRManager.hubConnection.SendAsync("JoinChannel", channel.Id, ClientUserManager.UserSecretToken);

                // Add to open channel list
                OpenPlanetChatChannels.Add(channel.Id, channel);

                Console.WriteLine($"Joined SignalR group for channel {channel.Id}");
            }

            if (!OpenPlanetChatWindows.Contains(window))
                OpenPlanetChatWindows.Add(window);
        }

        public async Task ClosePlanetChatChannel(ChatChannelWindow window, Channel channel, bool close_planet = true)
        {
            if (!OpenPlanetChatChannels.ContainsKey(channel.Id))
                return;

            Console.WriteLine("Closing chat channel " + channel.Name);

            // If there are no longer any windows open for the channel, leave
            if (!OpenPlanetChatWindows.Any(x => x.Channel.Id == channel.Id))
            {
                // Leaves channel via signalr
                await signalRManager.hubConnection.SendAsync("LeaveChannel", channel.Id);

                // Remove from list
                OpenPlanetChatChannels.Remove(channel.Id);

                Console.WriteLine($"Left SignalR group for channel {channel.Id}");
            } 

            // If there are no windows open for a planet, close the planet
            if (close_planet && !OpenPlanetChatWindows.Any(x => x.Channel.Planet_Id == channel.Planet_Id))
            {
                await ClosePlanet(await channel.GetPlanetAsync());
                await SetCurrentPlanet(null);
            }
        }

        public void SetChannelWindowClosed(ChatChannelWindow window)
        {
            OpenPlanetChatWindows.Remove(window);
        }

        public async Task RefreshOpenedChannels() {
            await OnChannelWindowUpdate.Invoke();
        }

        public async Task RefreshChannels() {
            await OnChannelsUpdate.Invoke();
        }

        public async Task RefreshCategories() {
            await OnCategoriesUpdate.Invoke();
        }

        public async Task SetCurrentPlanet(Planet planet)
        {
            if (planet == null)
            {
                if (CurrentPlanet == null)
                {
                    return;
                }
            }
            else
            {
                if (CurrentPlanet != null)
                {
                    if (planet.Id == CurrentPlanet.Id)
                    {
                        return;
                    }
                }
            }

            // Console.WriteLine("Egg");

            CurrentPlanet = planet;

            // Open planet if it's not opened
            await OpenPlanet(planet);

            
            if (planet == null)
                Console.WriteLine($"Set current planet to: null");
            else
                Console.WriteLine($"Set current planet to: " + planet.Name);

            if (OnPlanetChange != null)
            {
                Console.WriteLine($"Invoking planet change event");
                await OnPlanetChange?.Invoke(planet);
            }
        }

        public Planet GetCurrent()
        {
            return CurrentPlanet;
        }

        public override string ToString()
        {
            return $"Planet: {CurrentPlanet.Id}";
        }

        public async Task OnMessageRecieve(string json)
        {
            PlanetMessage message = JsonSerializer.Deserialize<PlanetMessage>(json);

            //Console.WriteLine("RECIEVE: ");
            //Console.WriteLine(json);

            //Console.WriteLine($"Recieved message {message.Message_Index} from channel {message.Channel_Id}.");

            if (!OpenPlanetChatChannels.ContainsKey(message.Channel_Id))
            {
                Console.WriteLine("Error: Recieved a message for a closed channel.");
            }

            foreach (ChatChannelWindow window in OpenPlanetChatWindows.Where(x => x.Channel.Id == message.Channel_Id))
            {
                await window.Component.OnRecieveMessage(message);
            }
        }

        public async Task RoleDeletion(string json)
        {
            Role role = JsonSerializer.Deserialize<Role>(json);

            if (role == null)
            {
                Console.WriteLine("Failed to deserialize role in role deletion.");
                return;
            }

            Console.WriteLine($"RECIEVE: Planet role deletion ping for role {role.Id}");
            Console.WriteLine(json);
            Role r;
            RolesCache.TryRemove(role.Id, out r);
            if (RolesListCache[role.Planet_Id].Contains(role.Id))
            {
                RolesListCache[role.Planet_Id].Remove(role.Id);
            }

            // check every member

            foreach (Member member in ClientCache.Members.Values)
            {
                member.RemoveRoleId(role.Id);
            }


            if (OnRoleDeletion != null)
            {
                await OnRoleDeletion.Invoke(role);
            }
        }

        public async Task UpdateRole(string json)
        {
            Role role = JsonSerializer.Deserialize<Role>(json);

            if (role == null)
            {
                Console.WriteLine("Failed to deserialize role in role update.");
                return;
            }

            Console.WriteLine($"RECIEVE: Planet role update ping for role {role.Id}");
            Console.WriteLine(json);
            await SetUpdatedRole(role);
            if (!RolesListCache[role.Planet_Id].Contains(role.Id)) {
                var rolesList = RolesListCache[role.Planet_Id];
                rolesList.Insert(rolesList.Count()-1, role.Id);
            }
            

            if (OnRoleUpdate != null)
            {
                await OnRoleUpdate.Invoke(role);
            }
        }

        public async Task UpdateMember(string json)
        {
            Member member = JsonSerializer.Deserialize<Member>(json);

            if (member == null)
            {
                Console.WriteLine("Failed to deserialize member in member update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet member update ping");
            Console.WriteLine(json);

            // get new roles for this member

            await member.LoadUserAsync();

            await member.GetRoleIdsAsync();

            await SetUpdatedMember(member);

            Member m = PlanetMembersListCache[member.Planet_Id].Find(x => x.Id == member.Id);
            PlanetMembersListCache[member.Planet_Id].Remove(m);

            PlanetMembersListCache[member.Planet_Id].Add(member);

            if (OnMemberUpdate != null)
            {
                await OnMemberUpdate.Invoke(member);
            }
        }

        public async Task UpdateChatChannel(string json)
        {
            Channel channel = JsonSerializer.Deserialize<Channel>(json);

            if (channel == null)
            {
                Console.WriteLine("Failed to deserialize channel in chat channel update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet chat channel update ping");
            Console.WriteLine(json);

            // update planet cache

            await ClientCache.Planets[channel.Planet_Id].NotifyUpdateChannel(channel);

            if (OnChatChannelUpdate != null)
            {
                await OnChatChannelUpdate.Invoke(channel);
            }
        }
        public async Task CategoryDeletion(string json)
        {
            Category category = JsonSerializer.Deserialize<Category>(json);

            if (category == null)
            {
                Console.WriteLine("Failed to deserialize category in chat category update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet Category Deletion ping");
            Console.WriteLine(json);

            // Update planet cache

            ClientCache.Planets[category.Planet_Id].NotifyDeleteCategory(category);

            if (OnCategoryDeletion != null)
            {
                await OnCategoryDeletion.Invoke(category);
            }
        }
        public async Task ChatChannelDeletion(string json)
        {
            Channel channel = JsonSerializer.Deserialize<Channel>(json);

            if (channel == null)
            {
                Console.WriteLine("Failed to deserialize channel in chat channel update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet chat channel Deletion ping");
            Console.WriteLine(json);

            // Update planet cache

            ClientCache.Planets[channel.Planet_Id].NotifyDeleteChannel(channel);

            if (OnChatChannelDeletion != null)
            {
                await OnChatChannelDeletion.Invoke(channel);
            }
        }

        public async Task UpdateCategory(string json)
        {
            Category category = JsonSerializer.Deserialize<Category>(json);

            if (category == null)
            {
                Console.WriteLine("Failed to deserialize category in chat category update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet chat category update ping");
            Console.WriteLine(json);

            // update planet cache

            await ClientCache.Planets[category.Planet_Id].NotifyUpdateCategory(category);

            if (OnCategoryUpdate != null)
            {
                await OnCategoryUpdate.Invoke(category);
            }
        }

        public async Task UpdatePlanet(string json)
        {
            Planet planet = JsonSerializer.Deserialize<Planet>(json);

            if (planet == null)
            {
                Console.WriteLine("Failed to deserialize planet in chat category update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet update ping");
            Console.WriteLine(json);

            // update planet cache

            ClientCache.Planets[planet.Id] = planet;

            if (OnPlanetUpdate != null)
            {
                await OnPlanetUpdate.Invoke(planet);
            }
        }
    }
}
