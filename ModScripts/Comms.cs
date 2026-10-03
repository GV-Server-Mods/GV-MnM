using ProtoBuf;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace AutoRepair
{
    public enum DataType
    {
        Sync,
        RequestSettings,
        SendSettings,
        DetailInfo,
        RequestConfig,
        SendConfig,
        InventoryRemoval,
        UpdateControlPanel,
        Colorblocks,
        SendToAssembler
    }

    [ProtoContract]
    public class ObjectContainer
    {
        [ProtoMember(1)] public Settings settings;
        [ProtoMember(2)] public ulong steamId;
        [ProtoMember(3)] public long blockId;
        [ProtoMember(4)] public string text;
        [ProtoMember(5)] public Config config;
        [ProtoMember(6)] public MyFixedPoint amount;
        [ProtoMember(7)] public bool boolean;
        [ProtoMember(8)] public List<MissingComps> comps;
    }

    [ProtoContract]
    public struct MissingComps
    {
        [ProtoMember(1)] public string item;
        [ProtoMember(2)] public int amount;
    }

    [ProtoContract]
    public class CommsPackage
    {
        [ProtoMember(1)]
        public DataType Type;

        [ProtoMember(2)]
        public byte[] Data;

        public CommsPackage()
        {
            Type = DataType.Sync;
            Data = new byte[0];
        }

        public CommsPackage(DataType type, ObjectContainer oc)
        {
            Type = type;
            Data = MyAPIGateway.Utilities.SerializeToBinary(oc);
        }
    }


    public static class Comms
    {
        public static void SendCompsToAssembler(long assemblerId, Dictionary<string, int> comps)
        {
            ObjectContainer oc = new ObjectContainer()
            {
                blockId = assemblerId,
                comps = new List<MissingComps>()
            };

            foreach(var comp in comps.Keys)
            {
                MissingComps missingComps = new MissingComps();
                missingComps.item = comp;
                missingComps.amount = comps[comp];
                oc.comps.Add(missingComps);
            }

            CommsPackage package = new CommsPackage(DataType.SendToAssembler, oc);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToServer(6200, sendData);
        }

        public static void ColorBlocksToServer(long blockId)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                blockId = blockId
            };

            CommsPackage package = new CommsPackage(DataType.Colorblocks, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToServer(6200, sendData);
        }

        public static void SendControlPanelUpdate(bool status, long blockId)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                boolean = status,
                blockId = blockId
            };

            CommsPackage package = new CommsPackage(DataType.UpdateControlPanel, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToServer(6200, sendData);
        }

        public static void SyncSettings(Settings settings)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                settings = settings
            };

            CommsPackage package = new CommsPackage(DataType.Sync, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToOthers(6200, sendData);

            if (MyAPIGateway.Session.IsServer)
            {
                IMyEntity entity;
                if (!MyAPIGateway.Entities.TryGetEntityById(settings._blockId, out entity)) return;
                var logic = entity.GameLogic.GetAs<Logic>();
                if (logic == null) return;

                logic.SaveSettings(logic.settings);
            }
        }

        public static void RequestConfig(ulong steamId)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                steamId = steamId
            };

            CommsPackage package = new CommsPackage(DataType.RequestConfig, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToServer(6200, sendData);
        }

        public static void SendConfig(Config config, ulong steamId)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                config = config
            };

            CommsPackage package = new CommsPackage(DataType.SendConfig, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageTo(6200, sendData, steamId);
        }

        public static void RequestSettings(ulong steamId, long blockId)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                steamId = steamId,
                blockId = blockId
            };
            

            CommsPackage package = new CommsPackage(DataType.RequestSettings, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToServer(6200, sendData);
        }

        public static void SendSettings(Settings settings, long blockId, ulong steamId)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                settings = settings,
                blockId = blockId
            };

            CommsPackage package = new CommsPackage(DataType.SendSettings, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageTo(6200, sendData, steamId);
        }

        public static void SyncDetailInfo(string stringBuilder, long blockId)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                text = stringBuilder,
                blockId = blockId
            };

            CommsPackage package = new CommsPackage(DataType.DetailInfo, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToOthers(6200, sendData);
        }

        public static void RemoveCompFromInventory(long gridId, string item, MyFixedPoint amount)
        {
            ObjectContainer objectContainer = new ObjectContainer()
            {
                blockId = gridId,
                text = item,
                amount = amount
            };

            CommsPackage package = new CommsPackage(DataType.InventoryRemoval, objectContainer);
            var sendData = MyAPIGateway.Utilities.SerializeToBinary(package);
            MyAPIGateway.Multiplayer.SendMessageToServer(6200, sendData);
        }

        public static void MessageHandler(byte[] data)
        {
            try
            {
                var package = MyAPIGateway.Utilities.SerializeFromBinary<CommsPackage>(data);
                if (package == null) return;

                if (package.Type == DataType.Sync)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    Settings.SyncSettings(packet.settings);
                    return;
                }

                if (package.Type == DataType.RequestSettings)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    IMyEntity entity;
                    if (!MyAPIGateway.Entities.TryGetEntityById(packet.blockId, out entity)) return;

                    var logic = entity.GameLogic.GetAs<Logic>();
                    if (logic == null) return;

                    SendSettings(logic.settings, packet.blockId, packet.steamId);
                    return;
                }

                if (package.Type == DataType.SendSettings)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    IMyEntity entity;
                    if (!MyAPIGateway.Entities.TryGetEntityById(packet.blockId, out entity)) return;

                    var logic = entity.GameLogic.GetAs<Logic>();
                    if (logic == null) return;

                    logic.settings = packet.settings;
                    return;
                }

                if (package.Type == DataType.DetailInfo)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    IMyEntity entity;
                    if (!MyAPIGateway.Entities.TryGetEntityById(packet.blockId, out entity)) return;

                    var logic = entity.GameLogic.GetAs<Logic>();
                    if (logic == null) return;

                    logic.myDetailInfo = packet.text;
                    var terminal = entity as IMyTerminalBlock;
                    if (terminal == null) return;

                    terminal.RefreshCustomInfo();
                    Session.RefreshControls(terminal);
                    return;
                }

                if (package.Type == DataType.RequestConfig)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    Comms.SendConfig(Session.Instance.config, packet.steamId);
                    return;
                }

                if (package.Type == DataType.SendConfig)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    Session.Instance.config = packet.config;

                    MyDefinitionId id;
                    if (MyDefinitionId.TryParse(packet.config.boostItem, out id))
                        Session.Instance.token = id.SubtypeName;

                    return;
                }

                if (package.Type == DataType.InventoryRemoval)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    IMyEntity entity;
                    if (!MyAPIGateway.Entities.TryGetEntityById(packet.blockId, out entity)) return;

                    IMyCubeGrid grid = entity as IMyCubeGrid;
                    if (grid == null) return;

                    MyFixedPoint amountNeeded = packet.amount;

                    List<IMyTerminalBlock> fatBlocks = new List<IMyTerminalBlock>();
                    MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(fatBlocks, x => x.HasInventory);

                    foreach (var bk in fatBlocks)
                    {
                        MyInventory blockInv = (MyInventory)bk.GetInventory();
                        if (blockInv == null) return;

                        IMyInventory inv = blockInv as IMyInventory;
                        var invList = blockInv.GetItems();

                        foreach (var item in invList)
                        {
                            if (!item.Content.SubtypeName.Contains(packet.text)) continue;
                            if (amountNeeded == item.Amount)
                            {
                                inv.RemoveItemAmount(item, item.Amount);
                                return;
                            }

                            if (amountNeeded > item.Amount)
                            {
                                amountNeeded -= (int)item.Amount;
                                inv.RemoveItemAmount(item, item.Amount);
                                continue;
                            }
                            else
                            {
                                inv.RemoveItemAmount(item, amountNeeded);
                                return;
                            }
                        }

                        if (amountNeeded <= 0) break;
                    }

                    return;
                }

                if (package.Type == DataType.UpdateControlPanel)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    IMyEntity entity;
                    if (!MyAPIGateway.Entities.TryGetEntityById(packet.blockId, out entity))
                    {
                        return;
                    }
                    var logic = entity.GameLogic.GetAs<Logic>();
                    if (logic == null)
                    {
                        return;
                    }

                    logic.myControlPanelOnOpenUpdate = packet.boolean;
                    return;
                }

                if (package.Type == DataType.Colorblocks)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    IMyEntity entity;
                    if (!MyAPIGateway.Entities.TryGetEntityById(packet.blockId, out entity)) return;

                    IMyProjector projector = entity as IMyProjector;
                    if (projector == null) return;

                    IMyCubeGrid projectedGrid = projector.ProjectedGrid;
                    if (projectedGrid == null) return;

                    List<IMySlimBlock> projectedBlocks = new List<IMySlimBlock>();
                    projectedGrid.GetBlocks(projectedBlocks);
                    IMyCubeGrid grid = projector.CubeGrid;
                    MyCubeGrid realGrid = grid as MyCubeGrid;

                    MyAPIGateway.Parallel.StartBackground(() =>
                    {
                        foreach (var fakeBlock in projectedBlocks)
                        {
                            Vector3D projectedBlockCtr;
                            fakeBlock.ComputeWorldCenter(out projectedBlockCtr);
                            Vector3I blockPos = realGrid.WorldToGridInteger(projectedBlockCtr);
                            IMySlimBlock block = realGrid.GetCubeBlock(blockPos);
                            if (block == null) continue;
                            if (block.BlockDefinition != fakeBlock.BlockDefinition) continue;

							MyAPIGateway.Utilities.InvokeOnGameThread(() => realGrid.SkinBlocks(block.Min, block.Max, fakeBlock.ColorMaskHSV, fakeBlock.SkinSubtypeId, true));                        }
                    });

                    return;
                }

                if (package.Type == DataType.SendToAssembler)
                {
                    var packet = MyAPIGateway.Utilities.SerializeFromBinary<ObjectContainer>(package.Data);
                    if (packet == null) return;

                    IMyEntity entity;
                    if (!MyAPIGateway.Entities.TryGetEntityById(packet.blockId, out entity)) return;

                    IMyAssembler assembler = entity as IMyAssembler;
                    if (assembler == null) return;

                    foreach (var comp in packet.comps)
                    {
                        MyDefinitionId id = new MyDefinitionId(typeof(MyObjectBuilder_Component), comp.item);
                        MyBlueprintDefinitionBase bluePrintDefBase = null;
                        MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(id, out bluePrintDefBase);
                        if (bluePrintDefBase == null) continue;
                        if (!assembler.CanUseBlueprint(bluePrintDefBase)) continue;

                        assembler.AddQueueItem(bluePrintDefBase, (MyFixedPoint)comp.amount);
                    }
                }
            }
            catch (Exception /*ex*/)
            {
//                MyLog.Default.WriteLineAndConsole($"M&M Comms Exception: {ex}");
            }
        }
    }
}
