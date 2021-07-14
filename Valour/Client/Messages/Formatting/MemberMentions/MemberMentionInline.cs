using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client.Messages.Formatting.MemberMentions
{
    public class MemberMentionInline : ContainerInline
    {
        public ulong Member_Id { get; set; }
    }
}
