﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AdvancedTooltip.Settings;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;

namespace AdvancedTooltip
{
    //it shows the suffix/refix tier directly near mod on hover item
    public class FastModsModule
    {
        private readonly Graphics _graphics;
        private readonly ItemModsSettings _modsSettings;
        private long _lastItemAddress;
        private Element _regularModsElement;
        private List<ModTierInfo> _mods = new List<ModTierInfo>();
        private Element _tooltip;

        public FastModsModule(Graphics graphics, ItemModsSettings modsSettings)
        {
            _graphics = graphics;
            _modsSettings = modsSettings;
        }

        public void DrawUiHoverFastMods(Element tooltip)
        {
            try
            {
                InitializeElements(tooltip);

                if (_regularModsElement == null || !_regularModsElement.IsVisibleLocal)
                    return;

                var rect = _regularModsElement.GetClientRectCache;
                var drawPos = new Vector2(tooltip.GetClientRectCache.X - 3, rect.Top);
                var height = rect.Height / _mods.Count;

                foreach (var modTierInfo in _mods)
                {
                    var textSize = _graphics.DrawText(modTierInfo.DisplayName,
                        drawPos.Translate(0, height / 2), modTierInfo.Color,
                        FontAlign.Right | FontAlign.VerticalCenter);

                    _graphics.DrawBox(
                        new RectangleF(drawPos.X - textSize.X - 3, drawPos.Y, textSize.X + 6, height),
                        Color.Black);
                    drawPos.Y += height;
                }
            }
            catch
            {
                //ignored   
            }
        }

        private void InitializeElements(Element tooltip)
        {
            _tooltip = tooltip;
            _regularModsElement = null;

            var modsRoot = tooltip.GetChildAtIndex(1);
            Element extendedModsElement = null;
            for (var i = modsRoot.Children.Count - 1; i >= 0; i--)
            {
                var element = modsRoot.Children[i];
                if (!string.IsNullOrEmpty(element.Text) && element.Text.StartsWith("<smaller"))
                {
                    extendedModsElement = element;
                    _regularModsElement = modsRoot.Children[i - 1];
                    break;
                }
            }

            if (_regularModsElement == null)
                return;
            if (_lastItemAddress != tooltip.Address)
            {
                _lastItemAddress = tooltip.Address;
                ParseItemHover(tooltip, extendedModsElement);
            }
        }

        private void ParseItemHover(Element tooltip, Element extendedModsElement)
        {
            _mods.Clear();
            var extendedModsStr =
                NativeStringReader.ReadString(extendedModsElement.Address + 0x2E8, tooltip.M, 5000);
            var extendedModsLines = extendedModsStr.Replace("\r\n", "\n").Split('\n');

            var regularModsStr =
                NativeStringReader.ReadString(_regularModsElement.Address + 0x2E8, tooltip.M, 5000);
            var regularModsLines = regularModsStr.Replace("\r\n", "\n").Split('\n');


            ModTierInfo currentModTierInfo = null;

            var modsDict = new Dictionary<string, ModTierInfo>();

            foreach (var extendedModsLine in extendedModsLines)
            {
                if (extendedModsLine.StartsWith("<smaller>") || extendedModsLine.StartsWith("<crafted>"))
                {
                    var isPrefix = extendedModsLine.Contains("Prefix");
                    var isSuffix = extendedModsLine.Contains("Suffix");
                    var affix = isPrefix ? "P" : "S";
                    var color = isPrefix ? _modsSettings.PrefixColor : _modsSettings.SuffixColor;
              

                    if (!extendedModsLine.Contains("Essences"))
                    {
                        var isRank = false;
                        const string TIER = "(Tier: ";
                        var tierPos = extendedModsLine.IndexOf(TIER);
                        if (tierPos != -1)
                        {
                            tierPos += TIER.Length;
                        }
                        else
                        {
                            const string RANK = "(Rank: ";
                            tierPos = extendedModsLine.IndexOf(RANK);

                            if (tierPos != -1)
                            {
                                tierPos += RANK.Length;
                                isRank = true;
                            }
                        }

                        if (tierPos == -1)
                        {
                            DebugWindow.LogMsg($"Cannot extract tier from mod text: {extendedModsLine}", 4);
                            return;
                        }

                        var tierStr = extendedModsLine.Substring(tierPos, 1);

                        if (!int.TryParse(tierStr, out var tier))
                        {
                            DebugWindow.LogMsg($"Cannot parse mod tier from mod text: {extendedModsLine}", 4);
                            return;
                        }

                        if(isRank)
                            affix += $" Rank{tier}";
                        else
                            affix += tier;

                        if (tier == 1)
                            color = _modsSettings.T1Color.Value;
                        else if (tier == 2)
                            color = _modsSettings.T2Color.Value;
                        else if (tier == 3)
                            color = _modsSettings.T3Color.Value;
                    }
                    else
                    {
                        affix += "E";
                    }

                    if (!isPrefix && !isSuffix)
                    {
                        DebugWindow.LogMsg($"Cannot extract Affix type from mod text: {extendedModsLine}", 4);
                        return;
                    }

                    currentModTierInfo = new ModTierInfo(affix, color);
                    continue;
                }


                if (extendedModsLine.StartsWith("<") && !char.IsLetterOrDigit(extendedModsLine[0]))
                {
                    currentModTierInfo = null;
                    continue;
                }

                if (currentModTierInfo != null)
                {
                    var modLine = Regex.Replace(extendedModsLine, @"\([\d-]+\)", string.Empty);
                    if (!modsDict.ContainsKey(modLine))
                    {
                        modsDict[modLine] = currentModTierInfo;
                    }
                }
            }

            var modTierInfos = new List<ModTierInfo>();
            foreach (var regularModsLine in regularModsLines)
            {
                var modFixed = regularModsLine;
                if (modFixed.StartsWith("+"))
                    modFixed = modFixed.Substring(1);

                var found = false;
                foreach (var keyValuePair in modsDict)
                {
                    if (regularModsLine.Contains(keyValuePair.Key))
                    {
                        found = true;
                        modTierInfos.Add(keyValuePair.Value);
                        break;
                    }
                }

                if (!found)
                {
                    DebugWindow.LogMsg($"Cannot extract mod from parsed mods: {modFixed}", 4);
                    modTierInfos.Add(new ModTierInfo("?", Color.Gray));
                    //return;
                }
            }

            _mods = modTierInfos;
        }


        private class ModTierInfo
        {
            public ModTierInfo(string displayName, Color color)
            {
                DisplayName = displayName;
                Color = color;
            }

            public string DisplayName { get; }
            public Color Color { get; }
        }
    }
}