using Markdig;
using Markdig.Renderers;

namespace Valour.Client.Messages.Formatting.MemberMentions
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class is a markdown exception to handle custom member mentions
    /// </summary>
    public class MemberMentionExtension : IMarkdownExtension
    {
        void IMarkdownExtension.Setup(MarkdownPipelineBuilder pipeline)
        {
            if (!pipeline.InlineParsers.Contains<MemberMentionParser>())
            {
                pipeline.InlineParsers.Insert(0, new MemberMentionParser());
            }
        }

        void IMarkdownExtension.Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
        }
    }
}
