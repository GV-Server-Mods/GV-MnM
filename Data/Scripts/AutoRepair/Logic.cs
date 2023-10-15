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

/*
 * TODO: Use MySync (or messaging) to update the client to "Static / Not Static" state as the client is *unreliable*
namespace of MySync<, > (VRage.Game.ModAPI.Network.* from VRage.Game.dll)
     SyncDirection.BothWays, SyncDirection.FromServer, SyncExtensions,
https://discord.com/channels/125011928711036928/126460115204308993/938979913162170478

Move ActiveBlock to be an interface to an object, not an object itself. It should be owned by the BlockSource.
*/

namespace AutoRepair
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), false, "MnMLarge", "MnMSmall")]
    public class Logic : MyGameLogicComponent
    {
        private readonly Guid cpmID = new Guid("801f61c8-140e-4f2e-9a0a-b14889776868");
        public bool isServer;
        public bool isDedicated;
        public int gameThreadId;
        public IMyProjector Projector { get; private set; }
        public Settings settings;
        private int myTicks;

        // UI
        private RepairErrors results = RepairErrors.None;
        public bool myControlPanelOnOpenUpdate;
        private int myConstructionCompleteETC;
        private StringBuilder myDetailBuilder = new StringBuilder();
        public string myDetailInfo;
        public string DetailInfo
        {
            get { return myDetailInfo; }
            set
            {
                myDetailInfo = value;
                Projector.RefreshCustomInfo();
                Session.RefreshControls(Projector);
                Comms.SyncDetailInfo(myDetailInfo, Projector.EntityId);
            }
        }


        // Construction
        public static int ourConstructionDelaySeconds = 10;
        public bool IsBoosted { get; private set; }
        public float WelderSpeed { get; private set; }
        
        private bool myCanConstruct;
        public float myBuiltRatio;
        public List<IMySlimBlock> myBuiltRatioScratchpad = new List<IMySlimBlock>();
        public List<IMyCubeGrid> myGridGroupScratchpad = new List<IMyCubeGrid>();

        private int retryCount = 0;

        private bool myHasRunConstructionInit;
        private bool myHasCompletedConstruction;
        private Settings mySettingsAtConstructionTime;
        public IBlockSource myBlockSource;
        private ActiveBlock myActiveBlock;
        public InventoryHelper myInventoryHelper = new InventoryHelper();

        // MGP - Multi-Grid Projector
        static private MGPAdapter myMGPAdapter;
        static public MGPAdapter MGPAdapter => myMGPAdapter ?? (myMGPAdapter = new MGPAdapter() );



        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            isServer = MyAPIGateway.Session.IsServer;
            isDedicated = MyAPIGateway.Utilities.IsDedicated;
            Projector = Entity as IMyProjector;
            gameThreadId = Environment.CurrentManagedThreadId;

            if (Projector.CubeGrid?.Physics == null)
                return;

            Projector.AppendingCustomInfo += CustomInfo;
            Session.Instance.InitControls();
            WelderSpeed = MyAPIGateway.Session.WelderSpeedMultiplier;

            if (isServer)
            {
                Projector.IsWorkingChanged += CheckIsWorking;
                settings = LoadSettings();
            }
            else
                Comms.RequestSettings(MyAPIGateway.Multiplayer.MyId, Projector.EntityId);

            Session.Instance.projectorBlocks.Add(Projector);
        }

        public override void UpdateBeforeSimulation10()
        {
            try
            {
                if (!isServer)
                    return;

                myTicks += 10;

                if (myTicks % 60 != 0)
                    return;

                myTicks = 0;

                if (settings == null)
                    return;

                if (!settings.Enabled || !Projector.IsWorking)
                    return;

                if (settings.Activated)
                {
                    if (!myHasRunConstructionInit)
                    {
                        ConstructionInit();
                    }

                    if (ConstructionSettingsChanged())
                    {
                        ConstructionReset();
                    }

                    if (myCanConstruct)
                     {
                        if (myHasCompletedConstruction)
                        {
                            ConstructionReset();
                        }
                        else
                        {
                            if (myActiveBlock != null)
                                Boost();

                            if (settings.BuildTimer > 0)
                            {
                                settings.BuildTimer--;
                            }
                            else
                            {
                                int checkedBlocks = 0;
                                while (checkedBlocks < Session.Instance.config.zeroTimeBlocksPerPass)
                                {
                                    checkedBlocks++;
                                    // Find Block
                                    if (myActiveBlock == null)
                                    {
                                        myActiveBlock = new ActiveBlock();
                                        RepairErrors getBlockResult = myBlockSource.GetActiveBlock(myInventoryHelper, myActiveBlock);
                                        if (getBlockResult == RepairErrors.None)
                                        {
                                            retryCount = 0;
                                            settings.BuildTimer = (int)myActiveBlock.myBuildTime;
                                        }
                                        else
                                        {
                                            if (++retryCount < 4)
                                            {
                                                // MyLog.Default.WriteLineAndConsole($"M&M retrying ({retryCount}) {myActiveBlock?.myBlockToConstruct?.myBlock?.BlockDefinition?.DisplayNameText}");
                                                myActiveBlock = null;
                                                SetDetailInfo();
                                                return;
                                            }

                                            results = getBlockResult;
                                            myActiveBlock = null;
                                            myHasCompletedConstruction = true;
                                            ConstructionReset();
                                            break;
                                        }
                                    }
                                    if (settings.BuildTimer > 0) {
//                                      MyLog.Default.WriteLineAndConsole($"M&M Breaking due to build wait");
                                        break;
                                    }
                                    if (myActiveBlock != null)
                                    {
                                        bool hasComponents = false;
                                        if (!myActiveBlock.IsValid())
                                            throw new Exception(" !myActiveBlock.IsValid() ");

                                        // Note: Someone may have hand welded the block while the build timer was going so we need to update our missing components.
                                        myActiveBlock.UpdateMissingComponents();
                                        // Optimization: We try and use the original inventories we found our needed components to avoid a secondary query.
                                        hasComponents = myInventoryHelper.DoInventoriesContainMissingComponents(myActiveBlock.myMissingComponents, myActiveBlock.myInventories);
                                        if (!hasComponents)
                                        {
                                            // Recovery: We didn't have what we needed in our cached inventories and since our inventory checks are destructive,
                                            // we need to get missing components *again* and do a full search. Might be worth using a scratchpad / copy here.
                                            myActiveBlock.UpdateMissingComponents();
                                            hasComponents = myInventoryHelper.GetInventoriesContainingMissingComponents(Projector.CubeGrid, myActiveBlock.myMissingComponents, myActiveBlock.myInventories);
                                        }

                                        if (hasComponents)
                                        {
                                            switch (myActiveBlock.myBlockToConstruct.myConstructType)
                                            {
                                                case ConstructType.Build:
//                                                    MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild ActiveBlock {myActiveBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                                                    MoveAndBuild(myActiveBlock);
                                                    myActiveBlock = null;
                                                    break;
                                            case ConstructType.Repair:
//                                                    MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair ActiveBlock {myActiveBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                                                    MoveAndRepair(myActiveBlock);
                                                    myActiveBlock = null;
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            myActiveBlock = null;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                SetDetailInfo();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"M&M UpdateBeforeSimulation10 Exception: {ex}");
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            try
            {
                if (settings == null && Projector != null)
                {
                    if (isServer)
                        settings = LoadSettings();
                    else
                        Comms.RequestSettings(MyAPIGateway.Multiplayer.MyId, Projector.EntityId);
                    return;
                }

                if (!isServer)
                    return;

                if (!settings.Enabled || !Projector.IsWorking)
                    return;

                myCanConstruct = CanConstruct();
                if (settings.Activated)
                {
                    // QUERY: This seems fragile, like it might overwrite more valuable repair state.
                    // TODO: On further reflection, GetGridBlocksRatio should return a RepairErrors
                    if (myCanConstruct)
                        results = RepairErrors.None;
                    else
                        ConstructionReset();

                    if (myControlPanelOnOpenUpdate && myBlockSource != null)
                    {
                        myConstructionCompleteETC = myBlockSource.GetBlockBuildTimeTotal();
                        myControlPanelOnOpenUpdate = false;
                    }
                }
            }
            catch(Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"M&M UpdateBeforeSimulation100 Exception: {ex}");
            }
        }

        private void Boost()
        {
            if (settings.TokenTimer <= 0 || !settings.BoostEnabled)
            {
                if (IsBoosted)
                {
                    IsBoosted = false;
                    settings.BuildTimer = (int)(settings.BuildTimer * Session.Instance.config.boostAmount);
                }
            }
            else if (settings.BoostEnabled)
            {
                if (myActiveBlock.IsValid())
                    settings.TokenTimer--;

                if (!IsBoosted)
                {
                    IsBoosted = true;
                    settings.BuildTimer = (int)(settings.BuildTimer / Session.Instance.config.boostAmount);
                }
            }
        }


        private bool MoveAndBuild(ActiveBlock activeBlock)
        {
            MyDefinitionBase definitionBase = null;
            try
            {
                IMySlimBlock blockToBuild = activeBlock.myBlockToConstruct.myBlock;
                definitionBase = blockToBuild.BlockDefinition;

                if (activeBlock.myBlockToConstruct.myConstructType != ConstructType.Build ||
                    blockToBuild.IsDestroyed ||
                    !myMGPAdapter.ProjectorCanBuild(Projector, activeBlock) )
                {
                    MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild Can't Build {myActiveBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                    return false;
                }

                IMyCubeGrid blockCubeGrid = Projector.CubeGrid;
                if ( MGPAdapter.Available )
                {
                    blockCubeGrid = MGPAdapter.MgpAgent.GetBuiltGrid(Projector.EntityId, myActiveBlock.myBlockToConstruct.myMGPBlockLocation.Value.GridIndex);
                }
                if (blockCubeGrid == null)
                    return false;

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild A");

                // NOTE: Because various block limiter plugins can delete blocks via callbacks,
                // we need to try and re-get our block by position after the build.
                Vector3D projectedBlockCtr;
                blockToBuild.ComputeWorldCenter(out projectedBlockCtr);
                Vector3I blockPos = blockCubeGrid.WorldToGridInteger(projectedBlockCtr);

                // API NOTE: Calls MyProjectorBase.BuildInternal
                myMGPAdapter.ProjectorBuild( Projector, activeBlock );

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild B");

                // BUG: This seem late. Remove or move above the positioning.
                Projector.UpdateOffsetAndRotation();

                // NOTE: Reacquire the block by position.
                blockToBuild = blockCubeGrid.GetCubeBlock(blockPos);

                if (blockToBuild == null )
                {
                    MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild Failed Reaquire {myActiveBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                    return false;
                }

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild C");

                // NOTE: Projector.Build allegedly fully builds the block without materials.
                // This code is to allow us to consume materials by unrepairing it and rerepairing it.
                blockToBuild.DecreaseMountLevel(blockToBuild.Integrity, null);
//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild D");
                blockToBuild.ClearConstructionStockpile(null);
//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild E");
                foreach (var inventory in activeBlock.myInventories)
                {
                    if (inventory == null)
                        continue;

                    blockToBuild.MoveItemsToConstructionStockpile(inventory);
                }
                // ================================================
                // = WARNING FROM THIS POINT ON, ITEM LOSS OCCURS =
                // ================================================\
//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild F");
                // Verify Block Materials
                // OPTIONAL? Does this ever fail?
                {
                    // REFACTOR? This is ugly, but I want to reuse that collection
                    activeBlock.myMissingComponents.Clear();
                    blockToBuild.GetMissingComponents(activeBlock.myMissingComponents);

                    if (activeBlock.myMissingComponents.Count != 0)
                    {
                        StringBuilder builder = new StringBuilder();
                        builder.AppendLine($"M&M Failed to retrieve all components:\nBlock: {activeBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                        foreach( var component in activeBlock.myMissingComponents )
                        {
                            builder.AppendLine($"{component.Key} = {component.Value}");
                        }
                        throw new Exception(builder.ToString());
                    }
                }
//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild G");

                // QUERY: I'm guessing that this is for animated blocks that have mechanical subgrids?
                MyCubeBlock cubeBlock = blockToBuild.FatBlock as MyCubeBlock;
                if (cubeBlock != null)
                {
                    if (!cubeBlock.CanContinueBuild())
                    {
                        throw new Exception($"Failed MyCubeBlock.CanContinueBuild:\nBlock: {activeBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                    }
                }

                blockToBuild.IncreaseMountLevel(blockToBuild.MaxIntegrity - blockToBuild.Integrity, Projector.OwnerId);

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild H");

                if (cubeBlock != null)
                {
                    var owner = Projector.CubeGrid.BigOwners[0] != 0 ? Projector.CubeGrid.BigOwners[0] : Projector.OwnerId;
                    cubeBlock.ChangeBlockOwnerRequest(0, MyOwnershipShareModeEnum.Faction);
                    cubeBlock.ChangeBlockOwnerRequest(owner, MyOwnershipShareModeEnum.Faction);
                }

                return true;
            }
            catch (Exception ex)
            {
                string blockName = definitionBase != null ? definitionBase.DisplayNameText : "null";
                MyLog.Default.WriteLineAndConsole($"M&M MoveAndBuild Exception: Block: {blockName} {ex}");
            }
            return false;
        }
        private bool MoveAndRepair(ActiveBlock activeBlock)
        {
            MyDefinitionBase definitionBase = null;
            try
            {
//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair A");
                IMySlimBlock blockToBuild = activeBlock.myBlockToConstruct.myBlock;
                definitionBase = blockToBuild.BlockDefinition;

                if (activeBlock.myBlockToConstruct.myConstructType != ConstructType.Repair ||
                    blockToBuild.IsDestroyed)
                {
                    return false;
                }

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair B");

                if (blockToBuild.HasDeformation)
                    blockToBuild.FixBones(0, 10);

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair C");

                foreach (var inventory in activeBlock.myInventories)
                {
                    if (inventory == null)
                        continue;
                    blockToBuild.MoveItemsToConstructionStockpile(inventory);
                }

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair D");

                // REFACTOR? This is ugly, but I want to reuse that collection
                activeBlock.myMissingComponents.Clear();
                blockToBuild.GetMissingComponents(activeBlock.myMissingComponents);

                if (activeBlock.myMissingComponents.Count != 0)
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine($"M&M MoveAndRepair Failed To Move Components:\nBlock: {activeBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                    foreach (var component in activeBlock.myMissingComponents)
                    {
                        builder.AppendLine($"{component.Key} = {component.Value}");
                    }
                    MyLog.Default.WriteLineAndConsole(builder.ToString());

                    return false;
                }

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair E");

                // QUERY: I'm guessing that this is for animated blocks that have mechanical subgrids?
                MyCubeBlock cubeBlock = blockToBuild.FatBlock as MyCubeBlock;
                if (cubeBlock != null)
                {
                    if (!cubeBlock.CanContinueBuild())
                    {
                        MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair Failed MyCubeBlock.CanContinueBuild\nBlock: {activeBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                        return false;
                    }
                }

//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair F");

                blockToBuild.IncreaseMountLevel(blockToBuild.MaxIntegrity - blockToBuild.Integrity, Projector.OwnerId);
                blockToBuild.UpdateVisual();


//                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair G");

                if (cubeBlock != null)
                {
                    var owner = Projector.CubeGrid.BigOwners[0] != 0 ? Projector.CubeGrid.BigOwners[0] : Projector.OwnerId;
                    if ( cubeBlock.OwnerId != owner )
                    {
                        cubeBlock.ChangeBlockOwnerRequest(0, MyOwnershipShareModeEnum.Faction);
                        cubeBlock.ChangeBlockOwnerRequest(owner, MyOwnershipShareModeEnum.Faction);
                    }
                }

                return true;

            }
            catch (Exception ex)
            {
                string blockName = definitionBase != null ? definitionBase.DisplayNameText : "null";
                MyLog.Default.WriteLineAndConsole($"M&M MoveAndRepair Exception: Block: {blockName} {ex}");
            }
            return false;
        }

        private void OnGridChanged(IMyCubeGrid aMainGrid, IMyCubeGrid aSubGrid)
        {
            ConstructionReset();
        }

        public bool CanConstruct()
        {
            if (!IsStatic()) 
                return false;

            if (Projector.OwnerId == 0)
            {
                results = RepairErrors.Projector_Needs_Owner;
                return false;
            }

            IMyFaction gridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(Projector.CubeGrid.BigOwners[0]);
            IMyFaction projectorFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(Projector.OwnerId);

            if (gridFaction != null && projectorFaction != null && gridFaction != projectorFaction)
            {
                results = RepairErrors.Not_Majority_Owner_Of_Grid;
                return false;
            }

            if (gridFaction == null || projectorFaction == null)
            {
                if (Projector.CubeGrid.BigOwners[0] != Projector.OwnerId)
                {
                    results = RepairErrors.Not_Majority_Owner_Of_Grid;
                    return false;
                }
            }

            if (Session.Instance.config.built <= 0)
                return true;

            if (settings.AllowRepairs && !settings.AllowBuild)
                return true;

            if (!Projector.IsProjecting)
                return false;

            float projectionRatio = (float)(Projector.TotalBlocks - Projector.RemainingBlocks) / Projector.TotalBlocks;
            if (projectionRatio < Session.Instance.config.built)
            {
                results = RepairErrors.Not_Enough_Completed_Blocks;
                return false;
            }

            IMyCubeGrid grid = Projector.ProjectedGrid;
            if (grid == null)
                return false;


            if (Session.Instance.config.built == 0.0f)
                return true;
            else
            {
                myBuiltRatioScratchpad.Clear();
                grid.GetBlocks(myBuiltRatioScratchpad);
                int built = 0;

                foreach (var bk in myBuiltRatioScratchpad)
                {
                    if (bk.Dithering != -1f)
                        continue;
                    if (bk.IsFullIntegrity)
                        built++;
                }

                myBuiltRatio = (float)built / Projector.TotalBlocks;
                if (myBuiltRatio >= Session.Instance.config.built)
                    return true;

                results = RepairErrors.Not_Enough_Completed_Blocks;
                return false;
            }
        }

        private bool IsStatic()
        {
            /*
            if (grid.GridSizeEnum == VRage.Game.MyCubeSize.Small)
            {
            */
            MyPhysicsComponentBase physics = Projector.CubeGrid?.Physics;
            if (physics == null) 
                return true;

            var gridVel = (Vector3D)physics.LinearVelocity;
            double gridSpeed = gridVel.Length();

            if (gridSpeed > 2)
            {
                results = RepairErrors.Velocity_Must_Be_0;
                return false;
            }
            return true;
                /*
            }
            if (!grid.IsStatic)
            {
                myGridGroupScratchpad.Clear();
                MyAPIGateway.GridGroups.GetGroup(Projector.CubeGrid, GridLinkTypeEnum.Physical, myGridGroupScratchpad);

                if (myGridGroupScratchpad.Count > 1)
                {
                    foreach(var connectedGrid in myGridGroupScratchpad)
                    {
                        if (!connectedGrid.IsStatic) continue;
                        return true;
                    }
                }

                results = RepairErrors.Not_Static;
                return false;
            }
            return true;*/

        }

        public static MyObjectBuilder_CubeBlockDefinition GetOB(MyDefinitionBase item)
        {
            MyObjectBuilder_DefinitionBase myObjectBuilder_DefinitionBase = MyDefinitionManagerBase.GetObjectFactory().CreateObjectBuilder<MyObjectBuilder_DefinitionBase>(item);
            myObjectBuilder_DefinitionBase.Id = item.Id;
            myObjectBuilder_DefinitionBase.Description = (item.DescriptionEnum.HasValue ? item.DescriptionEnum.Value.ToString() : ((item.DescriptionString != null) ? item.DescriptionString.ToString() : null));
            myObjectBuilder_DefinitionBase.DisplayName = (item.DisplayNameEnum.HasValue ? item.DisplayNameEnum.Value.ToString() : ((item.DisplayNameString != null) ? item.DisplayNameString.ToString() : null));
            myObjectBuilder_DefinitionBase.Icons = item.Icons;
            myObjectBuilder_DefinitionBase.Public = item.Public;
            myObjectBuilder_DefinitionBase.Enabled = item.Enabled;
            myObjectBuilder_DefinitionBase.DescriptionArgs = item.DescriptionArgs;
            myObjectBuilder_DefinitionBase.AvailableInSurvival = item.AvailableInSurvival;

            return myObjectBuilder_DefinitionBase as MyObjectBuilder_CubeBlockDefinition;
        }

        private void CustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            //builder.Clear();
            builder.Append(DetailInfo);
        }

        private void CheckIsWorking(IMyCubeBlock block)
        {
            if (!Projector.IsWorking)
            {
                ConstructionReset();
                settings.Enabled = false;
                results = RepairErrors.None;
                SetDetailInfo();
            }
        }

        private void ConstructionReset(bool doResetActivated = true)
        {
            myCanConstruct = false;
            myHasRunConstructionInit = false;
            myBuiltRatio = 0;
            myHasCompletedConstruction = false;

            myActiveBlock = null;
            myBlockSource = null;
            settings.BuildTimer = 0;

            retryCount = 0;

            if (doResetActivated)
            {
                settings.Activated = false;

                ConstructionCallbackRegistration(false);
            }
        }
        
        private bool ConstructionSettingsChanged()
        {
            if (settings != null && mySettingsAtConstructionTime != null )
            {
                if (settings.Activated != mySettingsAtConstructionTime.Activated) 
                    return true;
                if (settings.AllowBuild != mySettingsAtConstructionTime.AllowBuild)
                    return true;
                if (settings.AllowRepairs != mySettingsAtConstructionTime.AllowRepairs)
                    return true;

            }
            return false;
        }

        private void SetDetailInfo(string text = null)
        {
            myDetailBuilder.Clear();

            myDetailBuilder.AppendLine($"\n[ M&M Info w/{MGPAdapter.LogVersionShort()} ]:");

            if (!settings.Enabled)
            {
                myDetailBuilder.AppendLine("Status = Off");
            }
            else
            {
                if (settings.TokenTimer > 0 && settings.BoostEnabled)
                    myDetailBuilder.AppendLine($" Boost [ON] | Remaining: {TimeSpan.FromSeconds(settings.TokenTimer)}");
                else
                    myDetailBuilder.AppendLine($" Boost [OFF] | Remaining: {TimeSpan.FromSeconds(settings.TokenTimer)}");

                if (settings.Activated && !myHasCompletedConstruction)
                {
                    myDetailBuilder.AppendLine($"Status: Constructing {myBlockSource.RemainingBlocks}/{myBlockSource.TotalBlocks}");
                    myDetailBuilder.AppendLine($"   ETC: {TimeSpan.FromSeconds(myConstructionCompleteETC)}");

                    if (myActiveBlock != null && myActiveBlock.IsValid())
                        myDetailBuilder.AppendLine($"Block: {myActiveBlock.myBlockToConstruct.myBlock.BlockDefinition.DisplayNameText}");
                    else
                        myDetailBuilder.AppendLine("Searching ...");

                    myDetailBuilder.AppendLine($"   ETC: {TimeSpan.FromSeconds(settings.BuildTimer)}");
                }
                else if (!settings.Activated && results == RepairErrors.None)
                    myDetailBuilder.AppendLine("Status: Ready To Activate");
                else
                    myDetailBuilder.AppendLine("Status: Idle");

                if (results != RepairErrors.None)
                {
                    myDetailBuilder.AppendLine($"   Job Result:{results}");
                }
            }

            if (!string.IsNullOrEmpty(text))
                myDetailBuilder.AppendLine(text);

            DetailInfo = myDetailBuilder.ToString();
        }

        public void GetMissingComponentsCustom(IMySlimBlock block, ref Dictionary<string, int> dict)
        {
            if (block == null) return;
            dict.Clear();

            MyCubeBlockDefinition cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
            foreach (var item in cubeDef.Components)
            {
                if (dict.ContainsKey(item.Definition.Id.SubtypeName))
                    dict[item.Definition.Id.SubtypeName] += item.Count;
                else
                    dict.Add(item.Definition.Id.SubtypeName, item.Count);
            } 
        }

        private Settings LoadSettings()
        {
            try
            {
                Settings data = new Settings(Projector.EntityId);
                if (Projector.Storage != null)
                {
                    byte[] byteData;

                    string storage = Projector.Storage[cpmID];
                    byteData = Convert.FromBase64String(storage);
                    data = MyAPIGateway.Utilities.SerializeFromBinary<Settings>(byteData);

                    data._blockId = Projector.EntityId;
                    return data;
                }

                return data;
            }
            catch(Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"M&M LoadSettings Exception: {ex}");
                Settings data = new Settings(Projector.EntityId);
                return data;
            }
        }

        public void SaveSettings(Settings settings)
        {
            IMyEntity entity = null;
            MyAPIGateway.Entities.TryGetEntityById(settings._blockId, out entity);
            if (entity == null) return;

            if (entity.Storage != null)
            {
                var newByteData = MyAPIGateway.Utilities.SerializeToBinary(settings);
                var base64string = Convert.ToBase64String(newByteData);
                entity.Storage[cpmID] = base64string;
            }
            else
            {
                entity.Storage = new MyModStorageComponent();

                var newByteData = MyAPIGateway.Utilities.SerializeToBinary(settings);
                var base64string = Convert.ToBase64String(newByteData);
                entity.Storage[cpmID] = base64string;
            }
        }

        public override void OnRemovedFromScene()
        {
            //Unregister any handlers here
            if (Projector == null) return;
            if (Projector.CubeGrid?.Physics == null) return;
            Session.Instance.projectorBlocks.Remove(Projector);
            if (isServer)
            {
                Projector.IsWorkingChanged -= CheckIsWorking;
            }
        }

        private void ConstructionInit()
        {
            ConstructionReset(false);
            ConstructionCallbackRegistration(true);
            mySettingsAtConstructionTime = new Settings(settings);

            results = RepairErrors.None;

            if (MGPAdapter.Available)
                myBlockSource = new BlockSourceMGP(this);
            else
                myBlockSource = new BlockSourceNormal(this);

            myBlockSource.CollectBlocks();

            myHasRunConstructionInit = true;


            // This is to prevent people from using a timer block to rapidly turn on and off the MnM to build blocks faster.
            settings.BuildTimer = Logic.ourConstructionDelaySeconds;
        }

        private void ConstructionCallbackRegistration( bool attach )
        {
            if (attach)
            {
                Projector.CubeGrid.OnGridSplit += OnGridChanged;
                Projector.CubeGrid.OnGridMerge += OnGridChanged;
            }
            else
            {
                Projector.CubeGrid.OnGridSplit -= OnGridChanged;
                Projector.CubeGrid.OnGridMerge -= OnGridChanged;
            }

            /* I don't see a reason for these at this time, maybe OnClose?
             * grid.OnHierarchyUpdated -= OnGridChanged;
             * grid.OnFatBlockAdded -= FatBlockAdded;
             * grid.OnFatBlockRemoved -= FatBlockRemoved;
             * grid.OnClose -= OnGridClose;
            */
        }

    }
}
