using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Valour.Shared.Authorization;

namespace Valour.Database.Attributes
{
    public class TokenRequiredAttribute : Attribute
    {
    }
}
