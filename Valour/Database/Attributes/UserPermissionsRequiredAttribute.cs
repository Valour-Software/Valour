using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Authorization;

namespace Valour.Database.Attributes
{
    public  class UserPermissionsRequiredAttribute : Attribute
    {
        public UserPermission[] permissions;

        public UserPermissionsRequiredAttribute(params UserPermission[] permissions)
        {
            this.permissions = permissions;
        }
    }
}
