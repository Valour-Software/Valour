using Markdig.Helpers;
using Markdig.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Valour.Client.Messages.Formatting.MemberMentions
{
    public class MemberMentionParser : InlineParser
    {
        public static Regex Test = new Regex(@"<@m-\d+>");

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            // Test for first chars
            if (slice.CurrentChar != '<' ||
                slice.PeekCharExtra(1) != '@' ||
                slice.PeekCharExtra(2) != 'm' ||
                slice.PeekCharExtra(3) != '-')
            {
                return false;
            }

            // Build number from id
            string id_chars = "";
            int pos = 4;

            char c = ' ';

            while ((c = slice.PeekCharExtra(pos)).IsDigit())
            {
                id_chars += c;
                pos++;
            }

            // Make sure ending tag is '>'
            if (c != '>')
            {
                return false;
            }

            ulong id = ulong.Parse(id_chars);

            processor.Inline = new MemberMentionInline()
            {
                Span =
                {
                    Start = processor.GetSourcePosition(slice.Start, out int line, out int column)
                },
                Line = line,
                Column = column,
                Member_Id = id
            };

            slice.Start += (5 + id_chars.Length);

            return true;
        }
    }
}
