using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace Valour.Database.Attributes
{
    public class ValourRouteAttribute : System.Attribute
    {
        public string route;
        public HttpVerbs method;

        public ValourRouteAttribute(HttpVerbs method, string route = null)
        {
            this.route = route;
            this.method = method;
        }
    }
}
