using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Authorization;

namespace Valour.Database.Attributes
{
    public class CategoryChannelPermsRequiredAttribute : Attribute
    {
        public CategoryPermissionsEnum[] permissions;
        public string categoryRouteName;

        public CategoryChannelPermsRequiredAttribute(string categoryRouteName, params CategoryPermissionsEnum[] permissions)
        {
            this.permissions = permissions;
            this.categoryRouteName = categoryRouteName;
        }
    }
}
