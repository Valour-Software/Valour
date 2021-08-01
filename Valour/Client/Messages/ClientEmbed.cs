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
using Markdig;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Valour.Client.Messages
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

     public enum EmbedSize {
        Big,
        Normal,
        Small,
        VerySmall,
        Short,
        VeryShort
    }

    public class ClientEmbedItem
    {

        /// <summary>
        /// list of types
        /// Text
        /// Button
        /// InputBox
        /// </summary>
        public string Type {get; set;}

        public string Text {get; set;}
        // is not required
        public string Name {get; set;}

        /// <summary>
        /// Must be in hex format, example: "ffffff"
        /// </summary>
        public string Color {get; set;}

        public bool Inline {get; set;}
        public string Link {get; set;}
        public string TextColor {get; set;}
        public bool Center {get; set;}
        public EmbedSize Size {get; set;}
        // for button click events and for naming form elements
        public string Id {get; set;}
        // the value of a input
        public string Value {get; set;}

        // The placeholder for input boxes
        public string Placeholder {get; set;}

        public string GetInlineStyle {
            get {
              if (Inline) {
                  return "display:inline-grid;margin-right: 8px;";
              }
              else {
                  return "margin-right: 8px;";
              } 
            }
        }

        public string GetInputStyle {
            get {
                switch (Size)
                {
                    case EmbedSize.Short:
                        return "width:50%;";
                    case EmbedSize.VeryShort:
                        return "width:25%";
                    default:
                        return "";
                }
            }
        }

        // blazor can't cast types
        // so we have to do this dumb function

        public MarkupString DoMarkdown(string data) 
        {
            return (MarkupString)MarkdownManager.GetHtml(data);
        }

        


    }

    /// <summary>
    /// This class exists render embeds
    /// </summary>
    public class ClientEmbed
    {

        // if pages is null/empty, then render items
        // else render the pages

        public List<List<ClientEmbedItem>> Pages {get; set;}

        public List<ClientEmbedItem> Items {get; set;}

        public List<ClientEmbedItem> CurrentlyDisplayed;
        public int CurrentPage = 0;

        public void NextPage()
        {
            CurrentPage += 1;
            if (CurrentPage >= Pages.Count()) {
                CurrentPage = 0;
            }
            UpdateCurrentlyDisplayed();
        }
        public void PrevPage()
        {
            CurrentPage -= 1;
            if (CurrentPage < 0) {
                CurrentPage = Pages.Count()-1;
            }
            UpdateCurrentlyDisplayed();
        }

        public bool HasPages()
        {
            if (Pages == null) {
                return false;
            }
            if (Pages.Count() == 0) {
                return false;
            }
            return true;
        }

        public void UpdateCurrentlyDisplayed()
        {
            if (Pages == null) {
                CurrentlyDisplayed = Items;
                return;
            }
            if (Pages.Count() == 0) {
                CurrentlyDisplayed = Items;
                return;
            }
            CurrentlyDisplayed = Pages[CurrentPage];
        }

        public List<EmbedFormDataItem> GetFormData()
        {
            List<EmbedFormDataItem> data = new List<EmbedFormDataItem>();
            foreach(ClientEmbedItem item in CurrentlyDisplayed) {
                if (item.Type == "InputBox") {
                    EmbedFormDataItem DataItem = new EmbedFormDataItem()
                    {
                        ElementId = item.Id,
                        Value = item.Value,
                        Type = item.Type
                    };

                    // do only if input is not null
                    // limit the size of the input to 32 char
                    if (DataItem.Value != null) {
                        if (DataItem.Value.Length > 32) {
                            DataItem.Value = DataItem.Value.Substring(0, 31);
                        }
                    } 
                    data.Add(DataItem);
                }
            }
            return data;
        }
    }
}