using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;

namespace ScriptingClass
{
    public class Program : MyGridProgram
    {
        /// Copy code from here

        /// <summary>
        /// CargoDisplay — by Paolo
        ///
        /// Add cargo containers (and any block with inventory: assemblers, refineries,
        /// connectors, cockpits) AND the LCDs / cockpit screens you want to write to,
        /// into a single block group named <see cref="GroupName"/>. The script scans
        /// every inventory in the group, aggregates items by category and writes a
        /// monospace report to every text surface in the same group.
        ///
        /// Optional per-LCD filter — put one of these tags in the LCD's CustomName
        /// (square brackets included):
        ///   [Total]       only the summary header + fill bar
        ///   [Ores]        only ores
        ///   [Ingots]      only ingots
        ///   [Components]  only components
        ///   [Ammo]        only ammo magazines
        ///   [Tools]       only hand tools (welder, grinder, drill)
        ///   [Bottles]     only gas bottles
        ///   [Consumables] only consumables
        ///   [Other]       only items that did not match the categories above
        ///   no tag        full report (default)
        ///
        /// Designed to keep multiplayer server load low: work is split across ticks
        /// in a small state machine:
        ///   1) Refresh — re-fetch group members
        ///   2) Scan    — walk inventories <see cref="InventoriesPerTick"/> at a time
        ///   3) Render  — write all displays in one tick
        /// </summary>

        // ------- CONFIG -------
        const string GroupName = "CargoDisplay";
        const int InventoriesPerTick = 10;
        const int BarWidth = 16;
        const char BarFill = '█';   // █
        const char BarEmpty = '░';  // ░
        const int NameColWidth = 22;
        const int AmountColWidth = 13;
        const string LcdFont = "Monospace";
        const float LcdFontSize = 0.6f;
        // How many body lines fit on an LCD AFTER the fixed header (which is 6 lines:
        // title / Updated / Containers / Fill / Vol / Page X/Y). Tune to match how
        // many rows your LCD actually shows. Each LCD advances one page per render.
        const int BodyLinesPerPage = 16;
        // ----------------------

        enum Phase { Refresh, Scan, Render }
        enum Category { Ore, Ingot, Component, Ammo, Tool, Bottle, Consumable, Other }

        struct ItemAgg
        {
            public string SubtypeId;
            public Category Cat;
            public double Amount;
        }

        readonly List<IMyTerminalBlock> _inventoryBlocks = new List<IMyTerminalBlock>();
        readonly List<IMyTextSurface> _displays = new List<IMyTextSurface>();
        readonly List<string> _displayTags = new List<string>();
        readonly List<int> _displayPageIndex = new List<int>();
        readonly Dictionary<string, ItemAgg> _items = new Dictionary<string, ItemAgg>();
        readonly List<MyInventoryItem> _scratchItems = new List<MyInventoryItem>();
        readonly List<List<string>> _blocks = new List<List<string>>(8);
        readonly List<List<string>> _pages = new List<List<string>>(8);
        readonly StringBuilder _sb = new StringBuilder(2048);

        Phase _phase = Phase.Refresh;
        int _scanIndex;
        int _containerCount;
        double _totalCurrentVolume;
        double _totalMaxVolume;
        DateTime _lastRender = DateTime.MinValue;
        string _status = "starting...";

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Save() { }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                switch (_phase)
                {
                    case Phase.Refresh:
                        DoRefresh();
                        _phase = Phase.Scan;
                        _scanIndex = 0;
                        break;
                    case Phase.Scan:
                        DoScanChunk();
                        if (_scanIndex >= _inventoryBlocks.Count)
                        {
                            _scanIndex = 0;
                            _phase = Phase.Render;
                        }
                        break;
                    case Phase.Render:
                        DoRender();
                        _phase = Phase.Refresh;
                        break;
                }
            }
            catch (Exception ex)
            {
                _status = "ERR: " + ex.Message;
            }

            Echo($"CargoDisplay | group '{GroupName}'");
            Echo($"Phase: {_phase} (scan {_scanIndex}/{_inventoryBlocks.Count})");
            Echo($"Inv blocks: {_inventoryBlocks.Count} | Displays: {_displays.Count} | Items: {_items.Count}");
            Echo($"Containers: {_containerCount} | Last render: {_lastRender:HH:mm:ss}");
            Echo($"Last run: {Runtime.LastRunTimeMs:0.00} ms | Instr: {Runtime.CurrentInstructionCount}/{Runtime.MaxInstructionCount}");
            Echo(_status);
        }

        void DoRefresh()
        {
            _inventoryBlocks.Clear();
            // Preserve current page indices across refreshes by matching surfaces
            // identity-wise. We rebuild from a snapshot of the old lists.
            var oldDisplays = new List<IMyTextSurface>(_displays);
            var oldPages = new List<int>(_displayPageIndex);
            _displays.Clear();
            _displayTags.Clear();
            _displayPageIndex.Clear();

            var group = GridTerminalSystem.GetBlockGroupWithName(GroupName);
            if (group == null)
            {
                _status = $"Group '{GroupName}' not found.";
                return;
            }

            var blocks = new List<IMyTerminalBlock>();
            group.GetBlocks(blocks);

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];

                if (block.HasInventory)
                    _inventoryBlocks.Add(block);

                var provider = block as IMyTextSurfaceProvider;
                if (provider != null && provider.SurfaceCount > 0)
                {
                    var surface = provider.GetSurface(0);
                    if (surface != null)
                    {
                        _displays.Add(surface);
                        _displayTags.Add(ExtractTag(block.CustomName));

                        int prevIdx = oldDisplays.IndexOf(surface);
                        _displayPageIndex.Add(prevIdx >= 0 ? oldPages[prevIdx] : 0);
                    }
                }
            }

            _status = $"OK: {_inventoryBlocks.Count} inv blocks, {_displays.Count} displays";
        }

        void DoScanChunk()
        {
            if (_scanIndex == 0)
            {
                _items.Clear();
                _totalCurrentVolume = 0;
                _totalMaxVolume = 0;
                _containerCount = 0;
            }

            int end = Math.Min(_scanIndex + InventoriesPerTick, _inventoryBlocks.Count);
            for (int b = _scanIndex; b < end; b++)
            {
                var block = _inventoryBlocks[b];
                if (block == null) continue;

                if (block is IMyCargoContainer)
                    _containerCount++;

                for (int s = 0; s < block.InventoryCount; s++)
                {
                    var inv = block.GetInventory(s);
                    if (inv == null) continue;

                    _totalCurrentVolume += (double)inv.CurrentVolume;
                    _totalMaxVolume += (double)inv.MaxVolume;

                    _scratchItems.Clear();
                    inv.GetItems(_scratchItems);

                    for (int k = 0; k < _scratchItems.Count; k++)
                    {
                        var item = _scratchItems[k];
                        string typeId = item.Type.TypeId;
                        string subtypeId = item.Type.SubtypeId;
                        string key = typeId + "/" + subtypeId;

                        ItemAgg agg;
                        if (!_items.TryGetValue(key, out agg))
                        {
                            agg.SubtypeId = subtypeId;
                            agg.Cat = ClassifyType(typeId);
                            agg.Amount = 0;
                        }
                        agg.Amount += (double)item.Amount;
                        _items[key] = agg;
                    }
                }
            }
            _scanIndex = end;
        }

        void DoRender()
        {
            _lastRender = DateTime.UtcNow;

            for (int i = 0; i < _displays.Count; i++)
            {
                var surface = _displays[i];
                if (surface == null) continue;

                surface.ContentType = ContentType.TEXT_AND_IMAGE;
                surface.Font = LcdFont;
                surface.FontSize = LcdFontSize;

                int newPageIndex;
                _sb.Clear();
                RenderTo(_sb, _displayTags[i], _displayPageIndex[i], out newPageIndex);
                surface.WriteText(_sb);

                // Advance to next page for next render cycle (modulo handled inside).
                _displayPageIndex[i] = newPageIndex;
            }
        }

        // Renders header + one page of body lines into sb. Writes the next page index
        // (already wrapped to [0, pageCount-1]) into newPageIndex.
        void RenderTo(StringBuilder sb, string tag, int pageIndex, out int newPageIndex)
        {
            bool showAll = string.IsNullOrEmpty(tag);
            bool onlyTotal = tag == "total";

            // ---- Build per-category blocks then pack into pages so a section
            //      header is never separated from its items.
            _blocks.Clear();
            if (!onlyTotal)
            {
                if (showAll || tag == "ores")        AppendCategoryBlock(Category.Ore,         "ORES",        " kg", true);
                if (showAll || tag == "ingots")      AppendCategoryBlock(Category.Ingot,       "INGOTS",      " kg", true);
                if (showAll || tag == "components")  AppendCategoryBlock(Category.Component,   "COMPONENTS",  "",    false);
                if (showAll || tag == "ammo")        AppendCategoryBlock(Category.Ammo,        "AMMO",        "",    false);
                if (showAll || tag == "tools")       AppendCategoryBlock(Category.Tool,        "TOOLS",       "",    false);
                if (showAll || tag == "bottles")     AppendCategoryBlock(Category.Bottle,      "BOTTLES",     "",    false);
                if (showAll || tag == "consumables") AppendCategoryBlock(Category.Consumable,  "CONSUMABLES", "",    false);
                if (showAll || tag == "other")       AppendCategoryBlock(Category.Other,       "OTHER",       "",    false);
            }

            PaginateBlocks();

            int pageCount = _pages.Count;
            int page = pageIndex % pageCount;
            if (page < 0) page += pageCount;

            // ---- Header (always shown — same 6 lines on every page) ----
            sb.Append("=== CARGO INVENTORY ===\n");
            sb.Append("Updated: ").Append(_lastRender.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            sb.Append("Containers: ").Append(_containerCount).Append('\n');

            double frac = _totalMaxVolume > 0 ? _totalCurrentVolume / _totalMaxVolume : 0;
            sb.Append("Fill:  ");
            AppendBar(sb, frac, BarWidth);
            sb.Append(' ').Append((frac * 100).ToString("0.0")).Append("%\n");
            sb.Append("Vol:   ").Append(FormatVolume(_totalCurrentVolume))
                .Append(" / ").Append(FormatVolume(_totalMaxVolume)).Append('\n');
            sb.Append("Page:  ").Append(page + 1).Append('/').Append(pageCount).Append('\n');

            var pageLines = _pages[page];
            for (int i = 0; i < pageLines.Count; i++)
            {
                sb.Append(pageLines[i]).Append('\n');
            }

            newPageIndex = (page + 1) % pageCount;
        }

        void AppendCategoryBlock(Category cat, string title, string unit, bool decimals)
        {
            var rows = new List<ItemAgg>();
            foreach (var kvp in _items)
            {
                if (kvp.Value.Cat == cat) rows.Add(kvp.Value);
            }
            if (rows.Count == 0) return;

            rows.Sort((a, b) => string.Compare(a.SubtypeId, b.SubtypeId, StringComparison.Ordinal));

            var block = new List<string>(rows.Count + 1);
            block.Add("--- " + title + " ---");
            for (int i = 0; i < rows.Count; i++)
            {
                string name = PrettifyName(rows[i].SubtypeId);
                if (name.Length > NameColWidth) name = name.Substring(0, NameColWidth);
                string amount = rows[i].Amount.ToString(decimals ? "N1" : "N0") + unit;

                block.Add(name.PadRight(NameColWidth) + amount.PadLeft(AmountColWidth));
            }
            _blocks.Add(block);
        }

        // Packs category blocks into pages of <= BodyLinesPerPage lines each.
        // Blocks are kept atomic when possible (so a section title is never left at
        // the bottom of a page without its items). A block larger than a full page
        // is split, with the title repeated as "<TITLE> (cont.)" on each chunk.
        void PaginateBlocks()
        {
            _pages.Clear();
            var current = new List<string>();

            for (int b = 0; b < _blocks.Count; b++)
            {
                var block = _blocks[b];
                int sep = current.Count > 0 ? 1 : 0;

                if (current.Count + sep + block.Count <= BodyLinesPerPage)
                {
                    if (sep > 0) current.Add("");
                    current.AddRange(block);
                    continue;
                }

                // Doesn't fit on current page — flush, then place block on fresh page(s).
                if (current.Count > 0)
                {
                    _pages.Add(current);
                    current = new List<string>();
                }

                if (block.Count <= BodyLinesPerPage)
                {
                    current.AddRange(block);
                }
                else
                {
                    string contTitle = block[0] + " (cont.)";
                    int idx = 0;
                    while (idx < block.Count)
                    {
                        int available = idx == 0 ? BodyLinesPerPage : BodyLinesPerPage - 1;
                        int take = Math.Min(available, block.Count - idx);

                        if (idx > 0) current.Add(contTitle);
                        current.AddRange(block.GetRange(idx, take));
                        idx += take;

                        if (idx < block.Count)
                        {
                            _pages.Add(current);
                            current = new List<string>();
                        }
                    }
                }
            }

            if (current.Count > 0) _pages.Add(current);
            if (_pages.Count == 0) _pages.Add(new List<string>());
        }

        static void AppendBar(StringBuilder sb, double frac, int width)
        {
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            int filled = (int)Math.Round(frac * width);
            for (int i = 0; i < filled; i++) sb.Append(BarFill);
            for (int i = filled; i < width; i++) sb.Append(BarEmpty);
        }

        static string FormatVolume(double liters)
        {
            // CurrentVolume / MaxVolume are in liters (L).
            if (liters >= 1000000) return (liters / 1000000).ToString("N1") + " ML";
            if (liters >= 1000) return (liters / 1000).ToString("N1") + " kL";
            return liters.ToString("N0") + " L";
        }

        static Category ClassifyType(string typeId)
        {
            // TypeId looks like "MyObjectBuilder_Ore" / "MyObjectBuilder_Ingot" / ...
            if (typeId.EndsWith("_Ore")) return Category.Ore;
            if (typeId.EndsWith("_Ingot")) return Category.Ingot;
            if (typeId.EndsWith("_Component")) return Category.Component;
            if (typeId.EndsWith("_AmmoMagazine")) return Category.Ammo;
            if (typeId.EndsWith("_PhysicalGunObject")) return Category.Tool;
            if (typeId.EndsWith("_GasContainerObject")) return Category.Bottle;
            if (typeId.EndsWith("_OxygenContainerObject")) return Category.Bottle;
            if (typeId.EndsWith("_ConsumableItem")) return Category.Consumable;
            return Category.Other;
        }

        static string PrettifyName(string subtypeId)
        {
            if (string.IsNullOrEmpty(subtypeId)) return "";

            // Strip the conventional trailing "Item" suffix on tools.
            if (subtypeId.EndsWith("Item") && subtypeId.Length > 4)
                subtypeId = subtypeId.Substring(0, subtypeId.Length - 4);

            var sb = new StringBuilder(subtypeId.Length + 4);
            for (int i = 0; i < subtypeId.Length; i++)
            {
                char c = subtypeId[i];
                if (c == '_')
                {
                    sb.Append(' ');
                    continue;
                }
                if (i > 0 && char.IsUpper(c)
                    && !char.IsUpper(subtypeId[i - 1])
                    && subtypeId[i - 1] != '_')
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // Returns the first lowercase "[tag]" found in name, or null.
        static string ExtractTag(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int open = name.IndexOf('[');
            if (open < 0) return null;
            int close = name.IndexOf(']', open + 1);
            if (close < 0) return null;
            return name.Substring(open + 1, close - open - 1).Trim().ToLowerInvariant();
        }

        //Copy to here
    }
}
