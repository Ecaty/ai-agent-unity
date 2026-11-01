using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Agents.UI
{
    internal sealed class AgentIcon : Label
    {
        internal enum Kind { Send, Microphone, Account, History }
        internal AgentIcon(Kind kind)
        {
            var font = Resources.Load<Font>("MaterialSymbolsRounded");
            if (!font) throw new InvalidOperationException("Material Symbols font is missing.");
            text = kind == Kind.Send ? "\ue163" : kind == Kind.Microphone ? "\ue029" : kind == Kind.Account ? "\ue7fd" : "\ue5cf";
            enableRichText = false; pickingMode = PickingMode.Ignore;
            AddToClassList("agent-chat__icon");
            style.unityFontDefinition = FontDefinition.FromFont(font);
            style.fontSize = 20; style.unityTextAlign = TextAnchor.MiddleCenter;
            style.width = 22; style.height = 22; style.flexShrink = 0;
            style.marginLeft = 0; style.marginRight = 0; style.paddingLeft = 0; style.paddingRight = 0;
        }
    }
}
