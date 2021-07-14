using AutoMapper;
using Markdig.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Valour.Client.Planets;
using Valour.Client.Users;
using Valour.Shared.Messages;
using Valour.Shared.Users;

namespace Valour.Client.Messages
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add client funtionality to the PlanetMessage
    /// class.
    /// </summary>
    public class ClientPlanetMessage : PlanetMessage
    {
        /// <summary>
        /// True if this message's content has been parsed for rich content
        /// </summary>
        private bool richParsed = false;

        /// <summary>
        /// True if the markdown has already been generated
        /// </summary>
        private bool markdownParsed = false;

        /// <summary>
        /// The markdown-parsed version of the Content
        /// </summary>
        private string _markdownContent;

        /// <summary>
        /// The mentions contained within this message
        /// </summary>
        private List<MemberMention> _memberMentions;

        public string MarkdownContent
        {
            get
            {
                if (!markdownParsed)
                {
                    ParseMarkdown();
                }

                return _markdownContent;
            }
            private set
            {
                Content = value;
            }
        }

        /// <summary>
        /// The mentions for members within this message
        /// </summary>
        public List<MemberMention> MemberMentions
        {
            get
            {
                if (!richParsed)
                {
                    ParseRichContent();
                }

                return _memberMentions;
            }
        }

        /// <summary>
        /// Returns the generic planet object
        /// </summary>
        public PlanetMessage PlanetMessage
        {
            get
            {
                return (PlanetMessage)this;
            }
        }

        public ClientPlanetMessage()
        {

        }

        /// <summary>
        /// Returns client version using shared implementation
        /// </summary>
        public static ClientPlanetMessage FromBase(PlanetMessage message, IMapper mapper)
        {
            return mapper.Map<ClientPlanetMessage>(message);
        }


        /// <summary>
        /// Returns the author of the message
        /// </summary>
        public async Task<ClientPlanetMember> GetAuthorAsync()
        {
            ClientPlanetMember planetMember = await ClientPlanetManager.Current.GetPlanetMemberAsync(Author_Id, Planet_Id);

            return planetMember;
        }

        private void ParseMarkdown()
        {
            _markdownContent = MarkdownManager.GetHtml(Content);
            markdownParsed = true;
        }

        /// <summary>
        /// Parses (should parse AFTER markdown is generated!)
        /// </summary>
        private void ParseRichContent()
        {
            // Set up containers
            if (_memberMentions == null)
            {
                _memberMentions = new List<MemberMention>(0);
            }
            else
            {
                _memberMentions.Clear();
            }


            int pos = 0;

            string text = MarkdownContent;

            // Scan over full text
            while (pos < text.Length)
            {
                // Start tag
                if (text[pos] != '<')
                {
                    pos++;
                    continue;
                }

                int s_len = text.Length - pos;

                // Must be at least this long ( <@x-x> )
                if (s_len < 6)
                {
                    pos++;
                    continue;
                }

                // Mentions (<@x-)
                if (text[pos + 1] == '@' &&
                    text[pos + 3] == '-')
                {
                    // Member mention (<@m-)
                    if (text[pos + 2] == 'm')
                    {
                        // Extract id
                        char c = ' ';
                        int offset = 4;
                        string id_chars = "";

                        while (offset < s_len && 
                               (c = text[pos + offset]).IsDigit())
                        {
                            id_chars += c;
                            offset++;
                        }

                        // Make sure ending tag is '>'
                        if (c != '>')
                        {
                            pos++;
                            continue;
                        }

                        ulong id = ulong.Parse(id_chars);

                        // Create object
                        MemberMention memberMention = new MemberMention()
                        {
                            Member_Id = id,
                            Position = (ushort)pos
                        };

                        _memberMentions.Add(memberMention);
                    }
                    // Other mentions go here
                    else
                    {
                        pos++;
                        continue;
                    }
                }
                // Put future things here
                else
                {
                    pos++;
                    continue;
                }

                pos++;
            }
        }
    }
}
