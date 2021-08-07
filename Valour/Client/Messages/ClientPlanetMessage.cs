using AutoMapper;
using Markdig.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
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
        /// True if this message's content has been fully built
        /// </summary>
        private bool generated = false;

        /// <summary>
        /// True if the markdown has already been generated
        /// </summary>
        private bool markdownParsed = false;

        /// <summary>
        /// True if the embed data has been parsed
        /// </summary>
        private bool embedParsed = false;

        /// <summary>
        /// True if the mentions data has been parsed
        /// </summary>
        private bool mentionsParsed = false;

        /// <summary>
        /// The mentions contained within this message
        /// </summary>
        private List<Mention> _mentions;

        /// <summary>
        /// The fragments used for building elements
        /// </summary>
        private List<ElementFragment> _elementFragments;

        /// <summary>
        /// The inner embed data
        /// </summary>
        private ClientEmbed _embed;

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
        }

        public ClientEmbed Embed
        {
            get
            {
                if (!embedParsed)
                {
                    if (!string.IsNullOrEmpty(Embed_Data))
                    {
                        _embed = JsonSerializer.Deserialize<ClientEmbed>(Embed_Data);
                    }

                    embedParsed = true;
                }

                return _embed;
            }
        }

        /// <summary>
        /// The mentions for members within this message
        /// </summary>
        public List<Mention> Mentions
        {
            get
            {
                if (!mentionsParsed)
                {
                    if (!string.IsNullOrEmpty(Mentions_Data))
                    {
                        _mentions = JsonSerializer.Deserialize<List<Mention>>(Mentions_Data);
                    }
                }

                return _mentions;
            }
        }

        public ClientPlanetMessage()
        {

        }

        public void SetMentions(IEnumerable<Mention> mentions)
        {
            this._mentions = mentions.ToList();
            this.Mentions_Data = JsonSerializer.Serialize(mentions);
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
            return await ClientPlanetManager.Current.GetPlanetMemberAsync(Member_Id);
        }

        private void ParseMarkdown()
        {
            _markdownContent = MarkdownManager.GetHtml(Content);
            markdownParsed = true;
        }

        private static HashSet<string> InlineTags = new HashSet<string>()
        {
            "b", "/b", "em", "/em", "strong", "/strong",
            "blockquote", "/blockquote", "p", "/p",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "/h1", "/h2", "/h3", "/h4", "/h5", "/h6",
            "code", "/code", "br"
        };

        private static HashSet<string> SelfClosingTags = new HashSet<string>()
        {
            "br"
        };

        public void GenerateForPost()
        {
            if (_mentions == null)
            {
                _mentions = new List<Mention>();
            }
            else
            {
                _mentions.Clear();
            }

            int pos = 0;

            string text = MarkdownContent;

            while (pos < text.Length)
            {
                if (text[pos] == '«')
                {
                    int s_len = text.Length - pos;
                    // Must be at least this long ( «@x-x» )
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
                            Mention memberMention = new Mention()
                            {
                                Target_Id = id,
                                Position = (ushort)pos,
                                Length = (ushort)(6 + id_chars.Length),
                                Type = MentionType.Member
                            };

                            Mentions.Add(memberMention);
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

                pos++;
            }

            Mentions_Data = JsonSerializer.Serialize(Mentions);
        }

        /// <summary>
        /// Parses (should parse AFTER markdown is generated!)
        /// </summary>
        public void GenerateMessage()
        {
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

                        if (SelfClosingTags.Contains(tag))
                        {
                            start.Self_Closing = true;
                        }

                        _elementFragments.Add(start);
                    }
                }

                pos++;

            }

            generated = true;
        }

        /// <summary>
        /// Returns the message fragments used to render this message
        /// </summary>
        public List<MessageFragment> GetMessageFragments()
        {
            Stopwatch sw = new Stopwatch();

            if (!generated)
            {
                GenerateMessage();
            }

            sw.Start();

            List<MessageFragment> fragments = new List<MessageFragment>();

            // Empty message catch
            if (string.IsNullOrWhiteSpace(Content))
            {
                return fragments;
            }

            // First insert rich components into list and sort by position
            if (Mentions != null && Mentions.Count > 0)
            {
                foreach (var mention in Mentions)
                {
                    MessageFragment fragment = null;

                    if (mention.Type == MentionType.Member)
                    {
                        fragment = new MemberMentionFragment()
                        {
                            Mention = mention,
                            Position = mention.Position,
                            Length = mention.Length
                        };
                    }

                    if (fragment != null)
                    {
                        fragments.Add(fragment);
                    }
                }
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
