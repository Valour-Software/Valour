using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Authorization;

namespace Valour.Database.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ChatChannelPermsRequiredAttribute : Attribute
    {
        public ChatChannelPermissionsEnum[] permissions;
        public string channelRouteName;

        public ChatChannelPermsRequiredAttribute(string channelRouteName, params ChatChannelPermissionsEnum[] permissions)
        {
            this.permissions = permissions;
            this.channelRouteName = channelRouteName;
        }
    }
}
