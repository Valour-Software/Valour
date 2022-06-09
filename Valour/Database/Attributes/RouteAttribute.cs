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
        public string prefix;
        public HttpVerbs method;

        public ValourRouteAttribute(HttpVerbs method, string route = null, string prefix = null)
        {
            this.route = route;
            this.prefix = prefix;
            this.method = method;
        }
    }
}
