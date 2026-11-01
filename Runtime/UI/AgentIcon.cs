using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Agents.UI
{
    internal sealed class AgentIcon : Label
    {
        internal enum Kind { Send, Microphone, Account }
        internal AgentIcon(Kind kind)
        {
            var font = Resources.Load<Font>("MaterialSymbolsRounded");
            if (!font) throw new InvalidOperationException("Material Symbols font is missing.");
            text = kind == Kind.Send ? "\ue163" : kind == Kind.Microphone ? "\ue029" : "\ue7fd";
            enableRichText = false; pickingMode = PickingMode.Ignore;
            style.unityFontDefinition = FontDefinition.FromFont(font);
            style.fontSize = 20; style.unityTextAlign = TextAnchor.MiddleCenter;
            style.width = 22; style.height = 22; style.flexShrink = 0;
            style.color = new Color(.88f, .89f, .92f);
            style.marginLeft = 0; style.marginRight = 0; style.paddingLeft = 0; style.paddingRight = 0;
        }
    }
}
