﻿using Valour.Api.Items.Messages.Embeds;
using System;

namespace Valour.Api.Items.Messages.Embeds.Items;

public enum EmbedItemType
{
    Text = 1,
    Button = 2,
    InputBox = 3,
    TextArea = 4,
    ProgressBar = 5,
    Form = 6
}

public abstract class EmbedItem
{
    public EmbedItemType ItemType { get; set; }

    /// <summary>
    /// Should be null if the embed is not FreelyBased
    /// </summary>
    public int? X { get; set; }

    /// <summary>
    /// Should be null if the embed is not FreelyBased
    /// </summary>
    public int? Y { get; set; }

    public string GetStyle()
    {
        string style = "";
        if (X is not null)
        {
            int x = Math.Min((int)X, 500);
            int y = Math.Min((int)Y, 800);
            style += $"position: relative;left: {X}px;top: {Y}px;";
        }
        else
        {
            style += "display: inline-block;";
        }

        return style;
    }
}
