using System.Text.Json;
using Valour.Api.Items.Messages.Embeds;
using Valour.Shared.Items.Messages.Mentions;

namespace Valour.Api.Items.Messages;

public class Message : Valour.Shared.Items.Messages.Message {
        /// <summary>
        /// The mentions contained within this message
        /// </summary>
        private List<Mention> _mentions;

        /// <summary>
        /// True if the mentions data has been parsed
        /// </summary>
        private bool mentionsParsed = false;

        /// <summary>
        /// The inner embed data
        /// </summary>
        private Embed _embed;

        /// <summary>
        /// True if the embed data has been parsed
        /// </summary>
        private bool embedParsed = false;

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
        
        public Embed Embed
        {
            get
            {
                if (!embedParsed)
                {
                    if (!string.IsNullOrEmpty(Embed_Data))
                    {
                        _embed = JsonSerializer.Deserialize<Embed>(Embed_Data);
                    }

                    embedParsed = true;
                }

                return _embed;
            }
        }

        public void SetMentions(IEnumerable<Mention> mentions)
        {
            _mentions = mentions.ToList();
            Mentions_Data = JsonSerializer.Serialize(mentions);
        }

        public void ClearMentions()
        {
            if (_mentions == null)
            {
                _mentions = new List<Mention>();
            }
            else
            {
                _mentions.Clear();
            }
        }

}