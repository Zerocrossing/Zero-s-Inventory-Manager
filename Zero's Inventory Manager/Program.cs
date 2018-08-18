using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        //User Flags
        bool trackAll = false; //Do we cound all items on the grid or just items in tagged containers?
        bool usePanels = true; //Disable if you don't plan to use any LCD panels
        bool useAssemblers = true; //Disable if you don't want the system to place orders

        //Tags and naming
        string itemTrackTag = "[STO]"; //if not using trackAll, items with this tag will have their inventories tracked
        string panelTrackAllKeyword = "TrackAll"; //if this string is the only string in customdata for an LCD it will display the entire enventory
        string panelTrackTag = "[PNL]"; //panels with this tag will be used 
        string assemblerMasterTag = "[ASM]"; //if using cooperative mode, just tag the master assembler

        //Block References
        List<IMyTerminalBlock> tempBlockList; //temp list for searching
        List<IMyTextPanel> trackingPanels;
        List<IMyInventory> trackedInventories;
        List<IMyAssembler> assemblers;
        IMyAssembler assemblerMaster;

        //Dicts
        Dictionary<string, float> itemCounts;
        Dictionary<string, float> itemQuotas;

        public Program()
        {
            Echo("Initializing Dictionaries...");
            InitDicts();
            Echo("Getting Inventories...");
            GetTrackedInventories();
            Echo(String.Format("Found {0} inventories", trackedInventories.Count));
            Echo("Getting Panels...");
            GetTrackingPanels();
            Echo(String.Format("Found {0} panels", trackingPanels.Count));
            Echo("Getting Assemblers...");
            GetAssemblers();
            Echo("Getting Quotas");
            InitQuotaList();
            Echo(String.Format("Found {0} quotas", itemQuotas.Count));
        }

        #region Inits and getters

        private void InitDicts()
        {
            itemCounts = new Dictionary<string, float>();
        }

        /// <summary>
        /// Populates the trackedInventories list
        /// </summary>
        private void GetTrackedInventories()
        {
            trackedInventories = new List<IMyInventory>();
            tempBlockList = new List<IMyTerminalBlock>();
            if (!trackAll) GridTerminalSystem.SearchBlocksOfName(itemTrackTag, tempBlockList);
            else GridTerminalSystem.GetBlocks(tempBlockList);
            for (int blockIndex = 0; blockIndex < tempBlockList.Count; blockIndex++)
            {
                var block = tempBlockList[blockIndex];
                if (!block.HasInventory) continue;
                for (int invIndex = 0; invIndex < block.InventoryCount; invIndex++)
                {
                    var inventory = block.GetInventory(invIndex);
                    trackedInventories.Add(inventory);
                }
            }
        }

        /// <summary>
        /// Populates the trackingPanels list and sets panel text visibility
        /// </summary>
        private void GetTrackingPanels()
        {
            if (!usePanels) return;
            trackingPanels = new List<IMyTextPanel>();
            PopulateTaggedList<IMyTextPanel>(trackingPanels, panelTrackTag);
            for (int i = 0; i < trackingPanels.Count; i++)
            {
                var panel = trackingPanels[i];
                panel.ShowPublicTextOnScreen();
            }
        }

        /// <summary>
        /// Assigns the assemblers. If multiple assemblers are tagged with assemblerMasterTag only one will be used
        /// </summary>
        private void GetAssemblers()
        {
            if (!useAssemblers) return;
            //Master
            tempBlockList = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName(assemblerMasterTag, tempBlockList);
            if (tempBlockList.Count == 0)
            {
                Echo("No Tagged Assembler Found!");
                return;
            }
            assemblerMaster = tempBlockList[0] as IMyAssembler;
            //All Assemblers
            tempBlockList.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyAssembler>(tempBlockList);
        }

        /// <summary>
        /// Populates a list of blocks of type T from any blocks that have a specific tag
        /// </summary>
        private void PopulateTaggedList<T>(List<T> outList, string blockTag)
        {
            tempBlockList = new List<IMyTerminalBlock>();
            outList.Clear();
            GridTerminalSystem.SearchBlocksOfName(blockTag, tempBlockList);
            for (int i = 0; i < tempBlockList.Count; i++)
            {
                T block = (T)tempBlockList[i];
                outList.Add(block);
            }
        }

        /// <summary>
        /// Initializes the itemQuotas dictionary from this PBs custom data
        /// </summary>
        private void InitQuotaList()
        {
            itemQuotas = new Dictionary<string, float>();
            var lines = Me.CustomData.Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                var line = lines[l].Split();
                if (line.Length != 2)
                {
                    Echo(lines[l] + " is not a valid quota declaration");
                    continue;
                }
                float value = 0f;
                if (!float.TryParse(line[1], out value))
                {
                    Echo(lines[l] + " is not a valid quota declaration");
                    continue;
                }
                if (itemQuotas.ContainsKey(line[0]))
                {
                    Echo(lines[l] + "is a duplicate entry and will be ignored");
                    continue;
                }
                itemQuotas[line[0]] = value;
            }
        }

        #endregion

        public void Main(string argument, UpdateType updateSource)
        {
            Echo("Getting Item Count");
            GetItemCounts();
            Echo(String.Format("Found {0} items", itemCounts.Count));
            Echo("Writing Panels");
            WritePanels();
        }

        /// <summary>
        /// Populates the itemCounts dict with the count of items in trackedInventories
        /// </summary>
        private void GetItemCounts()
        {
            itemCounts.Clear();
            for (int invIndex = 0; invIndex < trackedInventories.Count; invIndex++)
            {
                var items = trackedInventories[invIndex].GetItems();
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    var item = items[itemIndex];
                    var itemName = item.Content.SubtypeId.ToString();
                    if (item.Content.TypeId.ToString().ToLower().Contains("ore")) itemName += " Ore";
                    if (itemCounts.ContainsKey(itemName)) itemCounts[itemName] += (float)item.Amount;
                    else itemCounts[itemName] = (float)item.Amount;
                }
            }
        }

        /// <summary>
        /// Populates the text on all panels in trackingPanels
        /// </summary>
        private void WritePanels()
        {
            if (!usePanels) return;
            for (int p = 0; p < trackingPanels.Count; p++)
            {
                var panel = trackingPanels[p];
                if (panel.CustomData != panelTrackAllKeyword) WritePanelFromCustomData(panel);
                else WritePanelAll(panel);
            }
        }

        /// <summary>
        /// Sets the text on an inventory panel using it's custom data
        /// Custom data is assumed to be single lines of item names
        /// </summary>
        private void WritePanelFromCustomData(IMyTextPanel panel)
        {
            string panelText = "";
            panelText += panel.GetPublicTitle() + '\n';
            var lines = panel.CustomData.Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l];
                string quota = "";
                if (itemQuotas.ContainsKey(line)) quota += "/" + itemQuotas[line];
                if (itemCounts.ContainsKey(line)) panelText += itemCounts[line] + '\n';
            }
            panel.WritePublicText(panelText);
        }

        private void WritePanelAll(IMyTextPanel panel)
        {
            string panelText = "";
            panelText += panel.GetPublicTitle() + '\n';
            for (int i = 0; i < itemCounts.Count; i++)
            {
                var itemValue = itemCounts.ElementAt(i);
                panelText += String.Format("{0} : {1}\n", itemValue.Key, itemValue.Value);
            }
            panel.WritePublicText(panelText);
        }

        private void PlaceAssemblerOrders()
        {
            Echo("Place Assembler Order Not Implemented");
        }

    }
}