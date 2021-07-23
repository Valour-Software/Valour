using AutoMapper;
using Markdig.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Valour.Client.Messages.Rendering;
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
        /// The mentions contained within this message
        /// </summary>
        private List<MemberMention> _memberMentions;

        /// <summary>
        /// The fragments used for building elements
        /// </summary>
        private List<ElementFragment> _elementFragments;

        /// <summary>
        /// The markdown-parsed version of the Content
        /// </summary>
        private string _markdownContent;

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

        public ClientEmbed GetEmbed()
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<ClientEmbed>(Embed_Data);
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

        private static HashSet<string> InlineTags = new HashSet<string>()
        {
            "b", "/b", "em", "/em", "strong", "/strong",
            "blockquote", "/blockquote"
        };

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

            if (_elementFragments == null)
            {
                _elementFragments = new List<ElementFragment>(2);
            }
            else
            {
                _elementFragments.Clear();
            }


            int pos = 0;

            string text = MarkdownContent;

            // Scan over full text
            while (pos < text.Length)
            {
                // Start tag
                if (text[pos] == '«')
                {

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
                            if (c != '»')
                            {
                                pos++;
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(id_chars))
                            {
                                pos++;
                                continue;
                            }

                            ulong id = 0;

                            bool parsed = ulong.TryParse(id_chars, out id);

                            if (!parsed)
                            {
                                pos++;
                                continue;
                            }

                            // Create object
                            MemberMention memberMention = new MemberMention()
                            {
                                Member_Id = id,
                                Position = (ushort)pos,
                                Length = (ushort)(6 + id_chars.Length)

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
                }
                else
                {
                    // Custom support for markdown things that are broken horribly.
                    // Detect html tags and build fragments
                    if (text[pos] == '<')
                    {
                        // A pure '<' can only be generated from markup, meaning we should
                        // always be able to find an end tag. I think.

                        int offset = pos + 1;
                        string tag = "";

                        while (text[offset] != '>')
                        {
                            tag += text[offset];
                            offset++;
                        }

                        if (InlineTags.Contains(tag))
                        {
                            // Allow these tags but be careful and block
                            // any we don't intend to be handled this way
                        }
                        else
                        {
                            pos++;
                            continue;
                        }

                        // We should now have the full tag

                        // Check if this is a closing tag

                        // Closing
                        if (tag[0] == '/')
                        {
                            ElementFragment end = new ElementFragment()
                            {
                                Closing = true,
                                Attributes = null,
                                Length = (ushort)(2 + (offset - pos)),
                                Position = (ushort)pos,
                                Tag = tag
                            };

                            _elementFragments.Add(end);
                        }
                        // Opening
                        else
                        {
                            ElementFragment start = new ElementFragment()
                            {
                                Closing = false,
                                Attributes = null,
                                Length = (ushort)(2 + (offset - pos)),
                                Position = (ushort)pos,
                                Tag = tag
                            };

                            _elementFragments.Add(start);
                        }
                    }
                }

                pos++;
            }
        }

        /// <summary>
        /// Returns the message fragments used to render this message
        /// </summary>
        public List<MessageFragment> GetMessageFragments()
        {
            Stopwatch sw = new Stopwatch();

            sw.Start();

            List<MessageFragment> fragments = new List<MessageFragment>();

            // Empty message catch
            if (string.IsNullOrWhiteSpace(Content)) 
            { 
                return fragments;
            }

            // First insert rich components into list and sort by position
            foreach (var memberMention in MemberMentions)
            {
                MemberMentionFragment fragment = new MemberMentionFragment()
                {
                    Mention = memberMention,
                    Member_Id = memberMention.Member_Id,
                    Position = memberMention.Position,
                    Length = memberMention.Length
                };

                fragments.Add(fragment);
            }

            // Shortcut if there is no rich content
            if (fragments.Count == 0)
            {
                MarkdownFragment fragment = new MarkdownFragment()
                {
                    Content = MarkdownContent,
                    Position = 0,
                    Length = (ushort)MarkdownContent.Length
                };

                fragments.Add(fragment);

                return fragments;
            }

            // Add fancy inline-supported fragments
            fragments.AddRange(_elementFragments);

            // Sort rich components
            var orderedRich = fragments.OrderBy(x => x.Position);
            var finFragments = orderedRich.ToList();

            ushort start = 0;
            // Now split the content at each rich starting position
            // and insert the first half into the fragments
            foreach (var rich in orderedRich)
            {
                if (rich.Position != 0)
                {
                    string markContent = MarkdownContent.Substring(start, rich.Position - start);

                    MarkdownFragment fragment = new MarkdownFragment()
                    {
                        Content = markContent,
                        Position = start,
                        Length = (ushort)markContent.Length
                    };

                    int index = finFragments.IndexOf(rich);

                    // Insert right before rich element
                    finFragments.Insert(index, fragment);
                }

                start = (ushort)(rich.Position + rich.Length - 1);
            }

            string endContent = MarkdownContent.Substring(start, MarkdownContent.Length - start);

            // There will be one remaining fragment for whatever is left
            MarkdownFragment end = new MarkdownFragment()
            {
                Content = endContent,
                Position = start,
                Length = (ushort)endContent.Length
            };

            finFragments.Add(end);

            sw.Stop();

            Console.WriteLine("Elapsed: " + sw.Elapsed.Milliseconds);

            return finFragments;
        }
    }
}
