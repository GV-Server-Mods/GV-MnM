using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
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

    public enum ConstructType
    {
        Build,
        Repair,
    }
    public enum FailReasonType
    {
        None,
        MissingComponents,
        Blocked,
        ConvertBuildToRepair,
        ConvertRepairToBuild,
    }

    public class ActiveBlock
    {
        public BlockToConstruct myBlockToConstruct = null;
        public Dictionary<string, int> myMissingComponents = new Dictionary<string, int>();
        public List<IMyInventory> myInventories = new List<IMyInventory>();
        public float myBuildTime;
        public void Initialize(BlockToConstruct aBlockToConstruct, float aBuildTime)
        {
            myBlockToConstruct = aBlockToConstruct;
            myBuildTime = aBuildTime;
        }
        public void UpdateMissingComponents()
        {
            myMissingComponents.Clear();
            BlockSourceBase.GetMissingComponents(myBlockToConstruct.myBlock, myBlockToConstruct.myConstructType, myMissingComponents);
        }

        public bool IsValid()
        {
            return myBlockToConstruct != null && myBlockToConstruct.myBlock != null;
        }

    }

    public class BlockToConstruct
    {
        public IMySlimBlock myBlock;
        public ConstructType myConstructType;
        public FailReasonType myLastFailReason = FailReasonType.None;
        public MultigridProjector.Api.BlockLocation? myMGPBlockLocation;

        public bool IsFatBlock
        {
            get { return myBlock.FatBlock != null; }
        }

        public BlockToConstruct(IMySlimBlock aBlock, ConstructType aConstructType, MultigridProjector.Api.BlockLocation? aBlockLocation)
        {
            myBlock = aBlock;
            myConstructType = aConstructType;
            myMGPBlockLocation = aBlockLocation;
        }
        public BlockToConstruct(BlockToConstruct aBlockToConstruct)
        {
            myBlock = aBlockToConstruct.myBlock;
            myConstructType = aBlockToConstruct.myConstructType;
            myMGPBlockLocation = aBlockToConstruct.myMGPBlockLocation;
        }

        public void GetMissingComponents(Dictionary<string, int> someMissingComponents)
        {
            BlockSourceBase.GetMissingComponents(myBlock, myConstructType, someMissingComponents);
        }
    }

    public interface IBlockSource
    {
        int TotalBlocks { get; }
        int RemainingBlocks { get; }

        bool IsBuilding { get; }
        bool IsRepairing { get; }

        void CollectBlocks();
        RepairErrors GetActiveBlock(InventoryHelper anInventoryHelper, ActiveBlock anActiveBlock);

        int GetBlockBuildTimeTotal();
        void GetMissingComponentsAll(Dictionary<string, int> someMissingComponents);

    }

    public abstract class BlockSourceBase : IBlockSource
    {
        public Logic Logic { get; protected set; }
        public IMyProjector Projector { get { return Logic.Projector; } }

        public int TotalBlocks { get; protected set; }
        public int RemainingBlocks { get { return myFoundBlocks.Count; } }

        public bool IsBuilding { get { return Logic.settings.AllowBuild; } }
        public bool IsRepairing { get { return Logic.settings.AllowRepairs; } }

        protected float WelderSpeed { get { return Logic.WelderSpeed; } }
        protected bool IsBoosted { get { return Logic.IsBoosted; } }

        protected Queue<BlockToConstruct> myFoundBlocks = new Queue<BlockToConstruct>();

        protected List<IMyCubeGrid> myCubeGridsScratchpad = new List<IMyCubeGrid>();

        protected bool AppendDamagedBlock(IMySlimBlock block)
        {
            if (!block.IsFullIntegrity || block.HasDeformation)
                myFoundBlocks.Enqueue(new BlockToConstruct(block, ConstructType.Repair, null));

            return false;
        }
        protected bool AppendProjectedBlock(IMySlimBlock block)
        {
            BuildCheckResult canBuildResult = Projector.CanBuild(block, false);
            if (canBuildResult == BuildCheckResult.OK || canBuildResult == BuildCheckResult.NotConnected)
            {
                myFoundBlocks.Enqueue(new BlockToConstruct(block, ConstructType.Build, null));
            }

            return false;
        }

        static public int GetBlockBuildTime(MyDefinitionBase aDefinition, float aWelderSpeed, bool isBoosted)
        {
            var cubeDef = aDefinition as MyCubeBlockDefinition;
            if (cubeDef == null)
                return 0;

            double assembleTime = cubeDef.MaxIntegrity / cubeDef.IntegrityPointsPerSec;
            assembleTime = (assembleTime / aWelderSpeed) * Session.Instance.config.baseAmount;

            if (isBoosted)
                assembleTime = assembleTime / Session.Instance.config.boostAmount;

            return (int)Math.Ceiling(assembleTime);
        }

        public int GetBlockBuildTime(MyDefinitionBase aDefinition)
        {
            return BlockSourceBase.GetBlockBuildTime(aDefinition, WelderSpeed, IsBoosted);
        }
        public int GetBlockBuildTime(IMySlimBlock block, ConstructType type)
        {
             if ( type == ConstructType.Repair)
             {
                 int time = BlockSourceBase.GetBlockBuildTime(block.BlockDefinition, WelderSpeed, IsBoosted);
                 float buildRatio = Math.Min(block.BuildLevelRatio, block.DamageRatio);
                 float timeModifier = MathHelper.Clamp(1.0f - buildRatio, 0.0f, 1.0f);
                 time = Math.Max(0, (int)((float)time *  timeModifier));
                 return time;
             }
            return BlockSourceBase.GetBlockBuildTime(block.BlockDefinition, WelderSpeed, IsBoosted);
        }
        public int GetBlockBuildTimeTotal()
        {
            int totalTime = 0;
            int zeroTimeBlocks = 0;
            foreach (var block in myFoundBlocks)
            {
                int time = GetBlockBuildTime(block.myBlock, block.myConstructType);
                if (time == 0) { zeroTimeBlocks++; } else { totalTime += time; }
            }
            return totalTime + Logic.ourConstructionDelaySeconds + (int)Math.Ceiling((float)zeroTimeBlocks / (float)Session.Instance.config.zeroTimeBlocksPerPass);

            // Optimization here using Projector data, but ugly
            // Dictionary<MyDefinitionBase, int> remaining = projector.RemainingBlocksPerType;
            // foreach (var def in remaining.Keys)
            // .. buildETA += assembleTime * remaining[def];
        }


        public static void GetMissingComponents(IMySlimBlock aBlock, ConstructType aConstructType, Dictionary<string, int> someMissingComponents, bool shouldClearComponents = true)
        {
            if (shouldClearComponents)
                someMissingComponents.Clear();

            if (aConstructType == ConstructType.Build)
            {
                MyCubeBlockDefinition cubeDef = aBlock.BlockDefinition as MyCubeBlockDefinition;
                foreach (var item in cubeDef.Components)
                {
                    if (someMissingComponents.ContainsKey(item.Definition.Id.SubtypeName))
                        someMissingComponents[item.Definition.Id.SubtypeName] += item.Count;
                    else
                        someMissingComponents.Add(item.Definition.Id.SubtypeName, item.Count);
                }
            }
            else
            {
                aBlock.GetMissingComponents(someMissingComponents);
            }
        }
        public void GetMissingComponentsAll(Dictionary<string, int> someMissingComponents)
        {
            someMissingComponents.Clear();
            foreach (var block in myFoundBlocks)
            {
                BlockSourceBase.GetMissingComponents(block.myBlock, block.myConstructType, someMissingComponents, false);
            }
        }

        protected void CollectBlocksRepair()
        {
            myCubeGridsScratchpad.Clear();
            MyAPIGateway.GridGroups.GetGroup(Projector.CubeGrid, GridLinkTypeEnum.Mechanical, myCubeGridsScratchpad);

            foreach (var grid in myCubeGridsScratchpad)
            {
                grid.GetBlocks(null, AppendDamagedBlock);
            }
        }

        public virtual void CollectBlocks()
        {
            myFoundBlocks.Clear();
        }

        public abstract RepairErrors GetActiveBlock(InventoryHelper anInventoryHelper, ActiveBlock anActiveBlock);

    }

    public class BlockSourceMGP : BlockSourceBase
    {
        public BlockSourceMGP(Logic aLogic)
        {
            Logic = aLogic;
        }

        public override void CollectBlocks()
        {
            base.CollectBlocks();

            if (IsBuilding)
            {
//                MyLog.Default.WriteLineAndConsole($"M&M BlockSourceMGP.CollectBlocks Called A");
                Logic.MGPAdapter.CollectBlocks(Projector, myFoundBlocks);
            }

            if (IsRepairing)
            {
                CollectBlocksRepair();
            }

//            MyLog.Default.WriteLineAndConsole($"M&M BlockSourceMGP.CollectBlocks Collected {myFoundBlocks.Count}");

            TotalBlocks = myFoundBlocks.Count;
        }

        public override RepairErrors GetActiveBlock(InventoryHelper anInventoryHelper, ActiveBlock anActiveBlock)
        {
            const bool DEBUG = false;
            int maxIterations = myFoundBlocks.Count;
            while (myFoundBlocks.Count > 0 && maxIterations > 0)
            {
                maxIterations--;
                BlockToConstruct block = myFoundBlocks.Dequeue();

                if (DEBUG)
                    MyLog.Default.WriteLineAndConsole($"M&M GetActiveBlock Checking: {block.myConstructType} - {block.myBlock.BlockDefinition.DisplayNameText}");

                switch (block.myConstructType)
                {
                    case ConstructType.Build:
                        if (Projector.ProjectedGrid != null)
                        {
                            if (Logic.MGPAdapter.ProjectorCanBuild(Projector, block))
                            {
                                block.GetMissingComponents(anActiveBlock.myMissingComponents);
                                if (anInventoryHelper.GetInventoriesContainingMissingComponents(Projector.CubeGrid, anActiveBlock.myMissingComponents, anActiveBlock.myInventories))
                                {

                                    if (DEBUG)
                                        MyLog.Default.WriteLineAndConsole($"M&M Initialized ActiveBlock {block.myBlock.BlockDefinition.DisplayNameText}");

                                    anActiveBlock.Initialize(block, GetBlockBuildTime(block.myBlock, block.myConstructType));
                                    return RepairErrors.None;
                                }
                                else
                                {
                                    block.myLastFailReason = FailReasonType.MissingComponents;
                                }
                            }
                            else
                            {
                                block.myLastFailReason = FailReasonType.Blocked;
                            }
                            
                            if (Logic.MGPAdapter.ProjectorShouldChangeBuildToRepair(Projector, block))
                            {
                                // The block we have cached is in the Projected Preview Grid, we need the Built Block
                                IMyCubeGrid builtGrid = Logic.MGPAdapter.MgpAgent.GetBuiltGrid(Projector.EntityId, block.myMGPBlockLocation.Value.GridIndex);
                                if (builtGrid != null)
                                {
                                    var builtBlock = builtGrid.GetCubeBlock(block.myMGPBlockLocation.Value.Position);
                                    if (builtBlock != null)
                                    {
                                        if (DEBUG)
                                            MyLog.Default.WriteLineAndConsole($"M&M GetBlock Changed Build Block to Repair {block.myBlock.BlockDefinition.DisplayNameText}");

                                        block.myBlock = builtBlock;
                                        block.myConstructType = ConstructType.Repair;
                                        block.myLastFailReason = FailReasonType.ConvertBuildToRepair;
                                    }
                                }
                            }

                            // We cannot build this block at this time.
                            // Note: We do not requeue if the projector has no projected grid
                            myFoundBlocks.Enqueue(block);
                        }
                        break;
                    case ConstructType.Repair:
                        if (Logic.MGPAdapter.ProjectorShouldChangeRepairToBuild(Projector, block))
                        {
                            if (DEBUG)
                                MyLog.Default.WriteLineAndConsole($"M&M GetBlock Changed Repair Block to Build {block.myBlock.BlockDefinition.DisplayNameText}");

                            block.myConstructType = ConstructType.Build;
                            block.myLastFailReason = FailReasonType.ConvertRepairToBuild;
                        }
                        else
                        {
                            block.GetMissingComponents(anActiveBlock.myMissingComponents);
                            if (anInventoryHelper.GetInventoriesContainingMissingComponents(Projector.CubeGrid, anActiveBlock.myMissingComponents, anActiveBlock.myInventories))
                            {
                                if (DEBUG)
                                    MyLog.Default.WriteLineAndConsole($"   Found Block");

                                anActiveBlock.Initialize(block, GetBlockBuildTime(block.myBlock, block.myConstructType));
                                return RepairErrors.None;
                            }
                            else
                            {
                                if (DEBUG)
                                {
                                    StringBuilder components = new StringBuilder();
                                    components.AppendLine($"\n   Missing Components:");
                                    block.GetMissingComponents(anActiveBlock.myMissingComponents);
                                    foreach (var comp in anActiveBlock.myMissingComponents)
                                    {

                                        components.AppendLine($"{comp.Value} - {comp.Key}");
                                    }
                                    components.AppendLine($"   Inventories:");
                                    foreach (var inventory in anActiveBlock.myInventories)
                                    {
                                        IMyTerminalBlock terminalBlock = inventory.Owner as IMyTerminalBlock;
                                        if (block != null)
                                            components.AppendLine($"{terminalBlock.CustomName}");
                                        else
                                            components.AppendLine($"?");
                                    }
                                    MyLog.Default.WriteLineAndConsole(components.ToString());
                                }

                                block.myLastFailReason = FailReasonType.MissingComponents;
                            }
                        }
                        // We cannot build this block at this time.
                        myFoundBlocks.Enqueue(block);

                        break;
                }
            }

            if (DEBUG && myFoundBlocks.Count > 0)
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("M&M GetBlock Failed w/Remaining:");
                foreach (var block in myFoundBlocks)
                {
                    builder.AppendLine($"{block.myConstructType} {block.myBlock.BlockDefinition.DisplayNameText} - {block.myLastFailReason}");
                }
                Dictionary<string, MyFixedPoint> items = new Dictionary<string, MyFixedPoint>();
                anInventoryHelper.GetGridInventoryItems(Projector.CubeGrid, items);
                builder.AppendLine("M&M Tracked Items:");
                foreach (var pair in items)
                {
                    builder.AppendLine($"{pair.Key} - {pair.Value.ToIntSafe()}");
                }

                MyLog.Default.WriteLineAndConsole(builder.ToString());
            }

            // We looped all the way around. We are out of blocks at this time.
            return myFoundBlocks.Count > 0 ? RepairErrors.Missing_Components : RepairErrors.Completed;
        }
    }

    public class BlockSourceNormal : BlockSourceBase
    {
        public BlockSourceNormal(Logic aLogic)
        {
            Logic = aLogic;
        }
        private bool AppendProjectedBlockNormal(IMySlimBlock block)
        {
            BuildCheckResult canBuildResult = Projector.CanBuild(block, false);
            if (canBuildResult == BuildCheckResult.OK || canBuildResult == BuildCheckResult.NotConnected)
            {
                myFoundBlocks.Enqueue(new BlockToConstruct(block, ConstructType.Build, null));
            }

            return false;
        }

        private void CollectBlocksBuildableNormal()
        {
            if (Projector.IsProjecting)
            {
                IMyCubeGrid projectedGrid = Projector.ProjectedGrid;
                if (projectedGrid != null)
                {
                    projectedGrid.GetBlocks(null, AppendProjectedBlockNormal);
                }
            }
        }
        public override void CollectBlocks()
        {
            base.CollectBlocks();

            if (IsBuilding)
            {
                CollectBlocksBuildableNormal();
            }
            if (IsRepairing)
            {
                CollectBlocksRepair();
            }

            // Sort?
//            MyLog.Default.WriteLineAndConsole($"M&M BlockSourceNormal.CollectBlocks Collected {myFoundBlocks.Count}");

            TotalBlocks = myFoundBlocks.Count;
        }

        public override RepairErrors GetActiveBlock(InventoryHelper anInventoryHelper, ActiveBlock anActiveBlock)
        {
            int maxIterations = myFoundBlocks.Count;
            while (myFoundBlocks.Count > 0 && maxIterations > 0)
            {
                maxIterations--;
                BlockToConstruct block = myFoundBlocks.Dequeue();

                switch (block.myConstructType)
                {
                    case ConstructType.Build:
                        if (Projector.ProjectedGrid != null)
                        {
                            if (Projector.CanBuild(block.myBlock, true) == BuildCheckResult.OK)
                            {
                                block.GetMissingComponents(anActiveBlock.myMissingComponents);
                                if (anInventoryHelper.GetInventoriesContainingMissingComponents(Projector.CubeGrid, anActiveBlock.myMissingComponents, anActiveBlock.myInventories))
                                {
                                    anActiveBlock.Initialize(block, GetBlockBuildTime(block.myBlock, block.myConstructType));
                                    return RepairErrors.None;
                                }
                            }
                            // We cannot build this block at this time.
                            // Note: We do not requeue if the projector has no projected grid
                            myFoundBlocks.Enqueue(block);
                        }
                        break;
                    case ConstructType.Repair:
                        block.GetMissingComponents(anActiveBlock.myMissingComponents);
                        if (anInventoryHelper.GetInventoriesContainingMissingComponents(Projector.CubeGrid, anActiveBlock.myMissingComponents, anActiveBlock.myInventories))
                        {
                            anActiveBlock.Initialize(block, GetBlockBuildTime(block.myBlock, block.myConstructType));
                            return RepairErrors.None;
                        }
                        else
                        {
                            // We cannot build this block at this time.
                            myFoundBlocks.Enqueue(block);
                        }
                        break;
                }
            }

            if (myFoundBlocks.Count > 0)
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("M&M GetBlock Failed w/Remaining:");
                foreach (var block in myFoundBlocks)
                {
                    builder.AppendLine($"{block.myBlock.BlockDefinition.DisplayNameText}");
                }
                MyLog.Default.WriteLineAndConsole(builder.ToString());
            }

            // We looped all the way around. We are out of blocks at this time.
            return myFoundBlocks.Count > 0 ? RepairErrors.Missing_Components : RepairErrors.Completed;
        }
    }

    public class MGPAdapter
    {
        private MultigridProjector.Api.MultigridProjectorModAgent myMGPAgent = new MultigridProjector.Api.MultigridProjectorModAgent();
        public MultigridProjector.Api.MultigridProjectorModAgent MgpAgent => myMGPAgent ?? (myMGPAgent = new MultigridProjector.Api.MultigridProjectorModAgent());

        public string LogVersion()
        {
            var mgpVersion = Available ? MgpAgent.Version : "Not available";
            return $"Multigrid Projector= {mgpVersion}";
        }

        public string LogVersionShort()
        {
            var mgpVersion = Available ? MgpAgent.Version : "MGP n/a";
            return $"MGP {mgpVersion}";
        }

        Dictionary<Vector3I, MultigridProjector.Api.BlockState> myBlockStatesScratchpad = new Dictionary<Vector3I, MultigridProjector.Api.BlockState>();
        public bool Available { get { return myMGPAgent.Available; } }

        public MultigridProjector.Api.BlockState GetBlockState(IMyProjector aProjector, BlockToConstruct aBlockToConstruct)
        {
            if (myMGPAgent.Available)
            {
                if (!aBlockToConstruct.myMGPBlockLocation.HasValue)
                    return MultigridProjector.Api.BlockState.Unknown;

                MultigridProjector.Api.BlockState blockState =
                    myMGPAgent.GetBlockState(
                        aProjector.EntityId,
                        aBlockToConstruct.myMGPBlockLocation.Value.GridIndex,
                        aBlockToConstruct.myMGPBlockLocation.Value.Position);

                return blockState;
            }
            return MultigridProjector.Api.BlockState.Unknown;
        }

        public bool ProjectorCanBuild(IMyProjector aProjector, ActiveBlock anActiveBlock)
        {
            return ProjectorCanBuild(aProjector, anActiveBlock.myBlockToConstruct);
        }

        public bool ProjectorCanBuild(IMyProjector aProjector, BlockToConstruct aBlockToConstruct)
        {
            if (myMGPAgent.Available)
                return GetBlockState(aProjector, aBlockToConstruct) == MultigridProjector.Api.BlockState.Buildable;
            else
                return aProjector.CanBuild(aBlockToConstruct.myBlock, true) == BuildCheckResult.OK;
        }

        public bool ProjectorShouldChangeRepairToBuild(IMyProjector aProjector, BlockToConstruct aBlockToConstruct)
        {
            if (myMGPAgent.Available)
            {
                var blockState = GetBlockState(aProjector, aBlockToConstruct);
                return blockState == MultigridProjector.Api.BlockState.Buildable || blockState == MultigridProjector.Api.BlockState.NotBuildable;
            }
            else
            {
                BuildCheckResult result = aProjector.CanBuild(aBlockToConstruct.myBlock, true);
                return result == BuildCheckResult.OK ||  result == BuildCheckResult.NotConnected;
            }
        }

        public bool ProjectorShouldChangeBuildToRepair(IMyProjector aProjector, BlockToConstruct aBlockToConstruct)
        {
            if (myMGPAgent.Available)
                return GetBlockState(aProjector, aBlockToConstruct) == MultigridProjector.Api.BlockState.BeingBuilt;
            else
                return aProjector.CanBuild(aBlockToConstruct.myBlock, false) == BuildCheckResult.AlreadyBuilt;
        }

        public void ProjectorBuild(IMyProjector aProjector, ActiveBlock anActiveBlock)
        {
            var ownerId = aProjector.OwnerId;
            var builtBy = aProjector.OwnerId;
            if ( myMGPAgent.Available )
            {
                if (anActiveBlock.myBlockToConstruct.myMGPBlockLocation.HasValue)
                {
                    builtBy = (long)anActiveBlock.myBlockToConstruct.myMGPBlockLocation.Value.GridIndex;
                }
            }
            aProjector.Build(anActiveBlock.myBlockToConstruct.myBlock, ownerId, aProjector.EntityId, true, builtBy);
        }

        private static readonly BoundingBoxI UnlimitedBoundingBoxI = new BoundingBoxI(Vector3I.MinValue, Vector3I.MaxValue);
        private const int CollectBlockStateMask = (int)MultigridProjector.Api.BlockState.NotBuildable | (int)MultigridProjector.Api.BlockState.Buildable;
//        private const int ConstructionBlockStateMask = (int)MultigridProjector.Api.BlockState.Buildable;

        public void CollectBlocks(IMyProjector aProjector, Queue<BlockToConstruct> someFoundBlocks )
        {
            myBlockStatesScratchpad.Clear();
            var subgridCount = myMGPAgent.GetSubgridCount(aProjector.EntityId);
            for (var subgridIndex = 0; subgridIndex < subgridCount; subgridIndex++)
            {
                var previewGrid = myMGPAgent.GetPreviewGrid(aProjector.EntityId, subgridIndex);
                if (previewGrid == null)
                    continue;
  
                if (!myMGPAgent.GetBlockStates(myBlockStatesScratchpad, aProjector.EntityId, subgridIndex, UnlimitedBoundingBoxI, CollectBlockStateMask))
                    continue;
                
                foreach (var location in myBlockStatesScratchpad.Keys)
                {
                    var block = previewGrid.GetCubeBlock(location);
                    if (block == null)
                    {
                        continue;
                    }
                        

                    BlockToConstruct blockToConstruct = new BlockToConstruct(block, ConstructType.Build, new MultigridProjector.Api.BlockLocation(subgridIndex, location));
                    someFoundBlocks.Enqueue(blockToConstruct);
                }

                myBlockStatesScratchpad.Clear();
            }
        }
    }


}
