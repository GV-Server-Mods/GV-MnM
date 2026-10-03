using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.Definitions;
using VRageMath;
using Sandbox.Game.EntityComponents;
using VRage.Utils;
using SpaceEngineers.Game.ModAPI;


namespace AutoRepair
{

    public class InventoryHelper
    {
        List<IMyTerminalBlock> myInventoryBlocks = new List<IMyTerminalBlock>();
        List<VRage.Game.ModAPI.Ingame.MyInventoryItem> myTempItems = new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();

        private bool IsValidInventoryBlock(IMyTerminalBlock block)
        {
            if (!block.HasInventory)
                return false;

            if (block is IMyRefinery ||
                block is IMyGasGenerator ||
                block is IMyGasTank ||
                block is IMyPowerProducer ||
                block is IMyShipDrill)
                return false;

            return true;
        }

        private void CollectInventoryBlocks(IMyCubeGrid aSearchGrid)
        {
            myInventoryBlocks.Clear();

            if (!string.IsNullOrEmpty(Session.Instance.config.containerTag))
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(aSearchGrid).SearchBlocksOfName(Session.Instance.config.containerTag, myInventoryBlocks, IsValidInventoryBlock);
            else
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(aSearchGrid).GetBlocksOfType(myInventoryBlocks, IsValidInventoryBlock);
        }

        public void GetGridInventoryItems(IMyCubeGrid aSearchGrid, Dictionary<string, MyFixedPoint> someItems)
        {
            CollectInventoryBlocks(aSearchGrid);

            someItems.Clear();

            foreach (var block in myInventoryBlocks)
            {
                IMyInventory inventory = block.GetInventory(block.InventoryCount - 1);
                if (inventory == null)
                    continue;

                myTempItems.Clear();

                inventory.GetItems(myTempItems);
                foreach (var entry in myTempItems)
                {
                    foreach (var item in myTempItems)
                    {
                        MyDefinitionId def = item.Type;

                        MyFixedPoint amount;
                        if (!someItems.TryGetValue(def.SubtypeName, out amount))
                            someItems.Add(def.SubtypeName, item.Amount);
                        else
                            someItems[def.SubtypeName] += item.Amount;
                    }
                }
            }
        }

        public bool DoInventoriesContainMissingComponents(Dictionary<string, int> someMissingComponents, List<IMyInventory> someSourceInventories)
        {
            if (someMissingComponents.Count == 0) {
                return true;
            }
            foreach (var inventory in someSourceInventories)
            {
                inventory.GetItems(myTempItems);
                foreach (var item in myTempItems)
                {
                    MyDefinitionId def = item.Type;

                    int needed;
                    if (!someMissingComponents.TryGetValue(def.SubtypeName, out needed))
                        continue;

                    var amt = item.Amount.ToIntSafe();
                    if (amt == 0)
                        continue;
                    else if (amt >= needed)
                        someMissingComponents.Remove(def.SubtypeName);
                    else
                        someMissingComponents[def.SubtypeName] = needed - amt;

                    if (someMissingComponents.Count == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool GetInventoriesContainingMissingComponents(IMyCubeGrid aSearchGrid, Dictionary<string, int> someMissingComponents, List<IMyInventory> someSourceInventories)
        {
            if (someMissingComponents.Count == 0)
                return true;

            CollectInventoryBlocks(aSearchGrid);

            foreach (var block in myInventoryBlocks)
            {
                if (!block.HasInventory)
                    continue;

                IMyInventory inventory = block.GetInventory(block.InventoryCount - 1);

                if (inventory == null)
                    continue;

                bool inventoryAddedToList = false;
                myTempItems.Clear();
                inventory.GetItems(myTempItems);
                foreach (var item in myTempItems)
                {
                    MyDefinitionId def = item.Type;

                    int needed;
                    if (!someMissingComponents.TryGetValue(def.SubtypeName, out needed))
                        continue;

                    var amt = item.Amount.ToIntSafe();
                    if (amt == 0)
                        continue;
                    else if (amt >= needed)
                        someMissingComponents.Remove(def.SubtypeName);
                    else
                        someMissingComponents[def.SubtypeName] = needed - amt;

                    if (someSourceInventories != null && !inventoryAddedToList)
                    {
                        someSourceInventories.Add(inventory);
                        inventoryAddedToList = true;
                    }

                    if (someMissingComponents.Count == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
