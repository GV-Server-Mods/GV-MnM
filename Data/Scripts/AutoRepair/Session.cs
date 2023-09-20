using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace AutoRepair
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class Session : MySessionComponentBase
    {
        public static Session Instance;
        private readonly string BlockSubtype = "MnM";
        private bool controlsInit;
        private bool controlsCreated;
        public Config config;
        public static IMyTerminalControlOnOffSwitch refreshtoggle;
        public string token;
        public IMyTerminalBlock current;
        public bool inControlPanel;
        //public long controlPanelId;
        public List<IMyProjector> projectorBlocks = new List<IMyProjector>();
        private HashSet<IMyCubeGrid> gridGroup = new HashSet<IMyCubeGrid>();
        private List<IMyAssembler> assemblerBlocks = new List<IMyAssembler>();
        private Dictionary<string, MyFixedPoint> totalInventory = new Dictionary<string, MyFixedPoint>();

        public bool InControlPanel
        {
            get { return inControlPanel; }
            set
            {
                inControlPanel = value;
                Comms.SendControlPanelUpdate(value, current.EntityId);
            }
        }

        public override void BeforeStart()
        {
            Instance = this;
            MyAPIGateway.Multiplayer.RegisterMessageHandler(6200, Comms.MessageHandler);

            if (MyAPIGateway.Session.IsServer)
            {
                config = Config.LoadConfig();
                //MyEntities.OnEntityCreate += EntityCreated;

            }  
            else
                Comms.RequestConfig(MyAPIGateway.Multiplayer.MyId);
        }

        private void EntityCreated(MyEntity ent)
        {
            IMyTerminalBlock tBlock = ent as IMyTerminalBlock;
            if (tBlock == null) return;
            if (!tBlock.HasInventory) return;


        }

        public void InitControls()
        {
            if (!controlsInit)
            {
                MyAPIGateway.TerminalControls.CustomControlGetter += CreateControls;
                controlsInit = true;
            }
        }

        private void CreateControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block == null || block as IMyProjector == null) return;
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;
            if (logic.settings == null)
            {
                Comms.RequestSettings(MyAPIGateway.Multiplayer.MyId, block.EntityId);
                return;
            }


            current = block;
            GetRefreshToggle();

            foreach (var control in controls)
            {
                if (control.Id.Contains("Label"))
                {
                    var label = control as IMyTerminalControlLabel;
                    if (label == null) continue;

                    string info = label.Label.ToString();
                    if (info.Contains("Spend"))
                    {
                        label.Label = MyStringId.GetOrCompute($"Spend {GetTokenAmount()} {token}\nfor {TimeSpan.FromSeconds(config.boostTime * GetTokenAmount())} of boost");
                        label.UpdateVisual();
                        label.RedrawControl();
                        break;
                    }
                }
            }

            if (controlsCreated) return;
            controlsCreated = true;

            // Seperate A
            var sepA = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyProjector>("SepARepair");
            sepA.Enabled = Block => true;
            sepA.SupportsMultipleBlocks = false;
            sepA.Visible = Block => IsProjector(Block);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(sepA);
            controls.Add(sepA);

            // M&M Switch
            var repairSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyProjector>("AutoRepairEnable");
            repairSwitch.Enabled = Block => true;
            repairSwitch.SupportsMultipleBlocks = false;
            repairSwitch.Visible = Block => IsProjector(Block);
            repairSwitch.Title = MyStringId.GetOrCompute("Enable M&M");
            repairSwitch.OnText = MyStringId.GetOrCompute("On");
            repairSwitch.OffText = MyStringId.GetOrCompute("Off");
            repairSwitch.Getter = Block => IsRepairEnabled(Block);
            repairSwitch.Setter = (Block, Builder) => SetRepairEnabled(Block, Builder);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(repairSwitch);
            controls.Add(repairSwitch);

            // Boost Switch
            var boostSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyProjector>("BoostSwitch");
            boostSwitch.Enabled = Block => true;
            boostSwitch.SupportsMultipleBlocks = false;
            boostSwitch.Visible = Block => IsProjector(Block);
            boostSwitch.Title = MyStringId.GetOrCompute("Enable Boost");
            boostSwitch.OnText = MyStringId.GetOrCompute("On");
            boostSwitch.OffText = MyStringId.GetOrCompute("Off");
            boostSwitch.Getter = Block => GetBoostEnabled(Block);
            boostSwitch.Setter = (Block, Builder) => SetBoostEnabled(Block, Builder);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(boostSwitch);
            controls.Add(boostSwitch);

            // Allow Repairs
            var repairs = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyProjector>("AllowRepairs");
            repairs.Enabled = Block => true;
            repairs.SupportsMultipleBlocks = false;
            repairs.Visible = Block => IsProjector(Block);
            repairs.Title = MyStringId.GetOrCompute("Allow Repairs");
            repairs.Getter = Block => GetAllowRepairs(Block);
            repairs.Setter = (Block, Value) => SetAllowRepairs(Block, Value);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(repairs);
            controls.Add(repairs);

            // Allow Building
            var building = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyProjector>("AllowRBuilding");
            building.Enabled = Block => true;
            building.SupportsMultipleBlocks = false;
            building.Visible = Block => IsProjector(Block);
            building.Title = MyStringId.GetOrCompute("Allow Building");
            building.Getter = Block => GetAllowBuilding(Block);
            building.Setter = (Block, Value) => SetAllowBuilding(Block, Value);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(building);
            controls.Add(building);

            // Tag Warning
            var tagLabel = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyProjector>("TagLabel");
            tagLabel.Enabled = Block => true;
            tagLabel.SupportsMultipleBlocks = false;
            tagLabel.Visible = Block => IsProjectorAndTaggable(Block);
            tagLabel.Label = MyStringId.GetOrCompute($"***Don't Forget To Add Tags\n    To Containers: {config.containerTag}***");
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(tagLabel);
            controls.Add(tagLabel);

            // Activate
            var activate = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("Activate");
            activate.Enabled = Block => AllowActivation(Block);
            activate.SupportsMultipleBlocks = false;
            activate.Visible = Block => IsProjector(Block);
            activate.Title = MyStringId.GetOrCompute("Activate");
            activate.Action = Block => ActivateAutoRepair(Block);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(activate);
            controls.Add(activate);

            // Token Info Label
            var tokenLabel = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyProjector>("TokenLabel");
            tokenLabel.Enabled = Block => true;
            tokenLabel.SupportsMultipleBlocks = false;
            tokenLabel.Visible = Block => IsProjector(Block);
            tokenLabel.Label = MyStringId.GetOrCompute($"Spend {GetTokenAmount()} {token}\nfor {TimeSpan.FromSeconds(config.boostTime * GetTokenAmount())} of boost");
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(tokenLabel);
            controls.Add(tokenLabel);

            // Tokens to spend
            var text = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyProjector>("Text");
            text.Enabled = Block => true;
            text.SupportsMultipleBlocks = false;
            text.Visible = Block => IsProjector(Block);
            text.Title = MyStringId.GetOrCompute("To Spend");
            text.Getter = Block => GetText(Block);
            text.Setter = (Block, Builder) => SetText(Block, Builder);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(text);
            controls.Add(text);

            // Apply Boost Timer
            var boost = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("Boost");
            boost.Enabled = Block => true;
            boost.SupportsMultipleBlocks = false;
            boost.Visible = Block => IsProjector(Block);
            boost.Title = MyStringId.GetOrCompute("Add Boost Time");
            boost.Action = Block => AddBoostTime(Block);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(boost);
            controls.Add(boost);

            // Assembler List
            var assemblerLists = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyProjector>("AssemblerList");
            assemblerLists.Enabled = Block => true;
            assemblerLists.SupportsMultipleBlocks = false;
            assemblerLists.Visible = Block => IsProjector(Block);
            assemblerLists.Title = MyStringId.GetOrCompute("Select Assembler To Queue Components");
            assemblerLists.ListContent = GetAssemblerList;
            assemblerLists.VisibleRowsCount = 10;
            assemblerLists.Multiselect = false;
            assemblerLists.ItemSelected = SetAssembler;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(assemblerLists);
            controls.Add(assemblerLists);

            // Missing Comps 
            var missingComps = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("MissingComps");
            missingComps.Enabled = Block => CanCheckForMissingComponents(Block);
            missingComps.SupportsMultipleBlocks = false;
            missingComps.Visible = Block => IsProjector(Block);
            missingComps.Title = MyStringId.GetOrCompute("Check Missing Comps");
            missingComps.Action = Block => CheckForMissingComponents(Block);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(missingComps);
            controls.Add(missingComps);

            // Color Label
            var colorLabel = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyProjector>("ColorLabel");
            colorLabel.Enabled = Block => true;
            colorLabel.SupportsMultipleBlocks = false;
            colorLabel.Visible = Block => IsProjector(Block);
            colorLabel.Label = MyStringId.GetOrCompute($"ReColor Grid To Match Projection");
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(colorLabel);
            controls.Add(colorLabel);

            // Match Projection Color
            var color = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("Color");
            color.Enabled = Block => IsValidProjection(Block);
            color.SupportsMultipleBlocks = false;
            color.Visible = Block => IsProjector(Block);
            color.Title = MyStringId.GetOrCompute("Color Blocks");
            color.Action = Block => ColorBlocks(Block);
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(color);
            controls.Add(color);
        }

        private void GetAssemblerList(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> listItems, List<MyTerminalControlListBoxItem> selectedItems)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            long objectEntityId = 0;
            var dummy = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("-Select Assembler Below-"), MyStringId.GetOrCompute("-Select Assembler Below-"), objectEntityId);
            listItems.Add(dummy);

            if (logic.settings.SelectedAssembler == 0)
            {
                selectedItems.Add(dummy);
            }

            assemblerBlocks.Clear();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(block.CubeGrid).GetBlocksOfType(assemblerBlocks);

            foreach (var bk in assemblerBlocks)
            {
                objectEntityId = bk.EntityId;
                var toList = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(bk.CustomName), MyStringId.GetOrCompute(bk.CustomName), objectEntityId);
                if (logic.settings.SelectedAssembler == bk.EntityId)
                {
                    selectedItems.Add(toList);
                }

                listItems.Add(toList);
            }
        }

        private void SetAssembler(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> listItems)
        {
            if (listItems.Count == 0) return;
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            logic.settings.SelectedAssembler = (long)listItems[0].UserData;
        }

        private void CheckForMissingComponents(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null)
                return;

            // TODO: An issue with the current design, when you get missing comps, the block
            // source generally null and we haven't collected blocks. Probably want to allocate
            // source when the UI changes after a short cooldown delay.
            Dictionary<string, int> missingComps = new Dictionary<string, int>();
            bool createdBlockSource = false;
            if (logic.myBlockSource == null )
            {
                if (Logic.MGPAdapter.Available)
                    logic.myBlockSource = new BlockSourceMGP(logic);
                else
                    logic.myBlockSource = new BlockSourceNormal(logic);

                logic.myBlockSource.CollectBlocks();
                createdBlockSource = true;
            }

            logic.myBlockSource.GetMissingComponentsAll(missingComps);
            logic.myInventoryHelper.GetInventoriesContainingMissingComponents(block.CubeGrid, missingComps, null);

            MyAPIGateway.Utilities.ShowMissionScreen("Summary of Missing Components", "", null, FormatCheckForMissingComponents(logic, missingComps), (ResultEnum result) =>
            {
                if (result == ResultEnum.OK)
                {
                    Comms.SendCompsToAssembler(logic.settings.SelectedAssembler, missingComps);
                }

            }, "Send To Assembler");

            if (createdBlockSource)
            {
                logic.myBlockSource = null;
            }
        }

        private string FormatCheckForMissingComponents( Logic logic, Dictionary<string, int> someMissingComps )
        {
            StringBuilder builder = new StringBuilder();

            if (logic.settings.AllowRepairs && !logic.settings.AllowBuild)
                builder.Append("\n~~ Components Missing for Repairs ONLY ~~\n");

            if (!logic.settings.AllowRepairs && logic.settings.AllowBuild)
                builder.Append("\n~~ Components Missing for Building Projection ONLY ~~\n");

            if (logic.settings.AllowRepairs && logic.settings.AllowBuild)
                builder.Append("\n~~ Components Missing for Both Repairs and Building Projection ~~\n");

            builder.Append("\n Component   |   Qty\n");
            builder.Append("---------------------------\n\n");
            foreach(var pair in someMissingComps)
            {
                builder.Append($" {pair.Key} - {pair.Value}\n");
            }

            builder.Append("\n\nPress button to send required components to selected assembler or hit 'X' (top right corner) to cancel.");

            return builder.ToString();
        }

        private bool CanCheckForMissingComponents(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null)
                return false;

            return logic.settings.Enabled;
        }

        private bool IsProjectorAndTaggable(IMyTerminalBlock block)
        {
            if (!IsProjector(block)) return false;

            return !string.IsNullOrEmpty(config.containerTag);
        }

        private void ColorBlocks(IMyTerminalBlock block)
        {
            Comms.ColorBlocksToServer(block.EntityId);
        }

        private bool IsValidProjection(IMyTerminalBlock block)
        {
            IMyProjector projector = block as IMyProjector;            
            if (projector == null) 
                return false;

            return projector.IsProjecting;
        }

        private bool GetAllowRepairs(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return false;

            return logic.settings.AllowRepairs;
        }

        private void SetAllowRepairs(IMyTerminalBlock block, bool value)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            logic.settings.AllowRepairs = value;
        }

        private bool GetAllowBuilding(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return false;

            return logic.settings.AllowBuild;
        }

        private void SetAllowBuilding(IMyTerminalBlock block, bool value)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            logic.settings.AllowBuild = value;
        }

        private int GetTokenAmount()
        {
            if (current == null) return 0;
            var logic = current.GameLogic.GetAs<Logic>();
            if (logic == null) return 0;

            return logic.settings.TokensToAdd;
        }

        private bool CheckInventories(Logic logic)
        {
            /*MyDefinitionId id;
            MyDefinitionId.TryParse(config.boostItem, out id);
            if (id == null) return false;

            MyDefinitionBase def;
            MyDefinitionManager.Static.TryGetDefinition(id, out def);
            if (def == null) return false;*/

            MyFixedPoint tokens = 0;
            MyFixedPoint tokensNeeded = logic.settings.TokensToAdd;

            List<IMyTerminalBlock> fatBlocks = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(current.CubeGrid).GetBlocksOfType(fatBlocks, x => x.HasInventory);

            foreach(var bk in fatBlocks)
            {
                MyInventory blockInv = (MyInventory)bk.GetInventory();
                if (blockInv == null) continue;

                var invList = blockInv.GetItems();
                foreach (var item in invList)
                {
                    if (item.Content.SubtypeName.Contains(token))
                        tokens += item.Amount;

                    if (tokens >= tokensNeeded) return true;
                }

                if (tokens >= tokensNeeded) return true;
            }

            return false;
        }

        private void AddBoostTime(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            bool flag = CheckInventories(logic);
            if (flag)
                Comms.RemoveCompFromInventory(block.CubeGrid.EntityId, token, logic.settings.TokensToAdd);
            else
                Audio.PlayClip("RealHudUnable");

            if (flag)
                logic.settings.TokenTimer += config.boostTime * logic.settings.TokensToAdd;
        }

        private StringBuilder GetText(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return new StringBuilder();

            StringBuilder sb = new StringBuilder();
            return sb.Append(logic.settings.TokensToAdd);
        }

        private void SetText(IMyTerminalBlock block, StringBuilder sb)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            int num = 0;
            string text = sb.ToString();
            int.TryParse(text, out num);
            if (num < 0) num = 0;

            logic.settings.TokensToAdd = num;
            RefreshControls(block);
        }

        private bool AllowActivation(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null)
                return false;

            if (logic.settings.Activated || !logic.settings.Enabled)
                return false;

            return logic.CanConstruct();
        }

        private void ActivateAutoRepair(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null)
                return;

            logic.settings.Activated = true;
        }

        private bool IsProjector(IMyTerminalBlock block)
        {
            if (block as IMyProjector != null)
            {
                if (block.BlockDefinition.SubtypeName.Contains(BlockSubtype))
                {
                    return true;
                }
            }

            return false;
        }

        private bool GetBoostEnabled(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return false;

            return logic.settings.BoostEnabled;
        }

        private void SetBoostEnabled(IMyTerminalBlock block, bool value)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            logic.settings.BoostEnabled = value;
        }

        private bool IsRepairEnabled(IMyTerminalBlock block)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return false;

            InControlPanel = true;
            return logic.settings.Enabled;
        }

        private void SetRepairEnabled(IMyTerminalBlock block, bool value)
        {
            var logic = block.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            logic.settings.Enabled = value;
            if (value)
                logic.settings.Activated = false;

            RefreshControls(block);
        }

        public static void GetRefreshToggle()
        {
            return;
            /*
            List<IMyTerminalControl> items;
            MyAPIGateway.TerminalControls.GetControls<IMyTerminalBlock>(out items);

            foreach (var item in items)
            {
                if (item.Id == "ShowInToolbarConfig")
                {
                    refreshtoggle = (IMyTerminalControlOnOffSwitch)item;
                    break;
                }
            }*/
        }

        public static void RefreshControls(IMyTerminalBlock b)
        {
            /*if (MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.ControlPanel) return;
            if (refreshtoggle != null)
            {
                var originalSetting = refreshtoggle.Getter(b);
                refreshtoggle.Setter(b, !originalSetting);
                refreshtoggle.Setter(b, originalSetting);
            }*/

            if (MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
            {
                var myCubeBlock = b as MyCubeBlock;

                if (myCubeBlock.IDModule != null)
                {

                    var share = myCubeBlock.IDModule.ShareMode;
                    var owner = myCubeBlock.IDModule.Owner;
                    myCubeBlock.ChangeOwner(owner, share == MyOwnershipShareModeEnum.None ? MyOwnershipShareModeEnum.All : MyOwnershipShareModeEnum.None);
                    myCubeBlock.ChangeOwner(owner, share);
                }
            }
        }

        protected override void UnloadData()
        {
            Instance = null;
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(6200, Comms.MessageHandler);
            //MyEntities.OnEntityCreate -= EntityCreated;
            if (controlsInit)
                MyAPIGateway.TerminalControls.CustomControlGetter -= CreateControls;
        }
    }
}
