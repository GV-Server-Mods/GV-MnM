using ProtoBuf;
using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.ModAPI;

namespace AutoRepair
{
    public enum RepairErrors
    {
        None,
        Missing_Components,
        Completed,
        Not_Static,
        Velocity_Must_Be_0,
        Not_Enough_Completed_Blocks,
        Ready_To_Activate,
        Projector_Needs_Owner,
        Not_Majority_Owner_Of_Grid
    }

    public enum Mode
    {
        Repair,
        Build,
        Grind
    }

    [ProtoContract]
    public class Settings
    {
        [ProtoMember(1)] public bool? _enabled = null;
        [ProtoMember(2)] public long? _blockId = null;
        [ProtoMember(3)] public int? _buildTimer = null;
        [ProtoMember(4)] public int? _tokenTimer = null;
        [ProtoMember(5)] public bool? _activated = null;
        [ProtoMember(6)] public int? _tokensToAdd = null;
        [ProtoMember(7)] public bool? _allowRepairs = null;
        [ProtoMember(8)] public bool? _allowBuild = null;
        [ProtoMember(9)] public bool? _boostEnabled = null;
        //[ProtoMember(10)] public Mode? _currentMode = null;
        [ProtoMember(11)] public long? _selectedAssembler = null;

        public Settings()
        {
        }

        public Settings(long id)
        {
            _blockId = id;
        }

        public Settings(Settings rhs)
        {
            _enabled = rhs._enabled;
            _blockId = rhs._blockId;
            _buildTimer = rhs._buildTimer;
            _tokenTimer = rhs._tokenTimer;
            _activated = rhs._activated;
            _tokensToAdd = rhs._tokensToAdd;
            _allowRepairs = rhs._allowRepairs;
            _allowBuild = rhs._allowBuild;
            _boostEnabled = rhs._boostEnabled;
            _selectedAssembler = rhs._selectedAssembler;
        }

        /*public long BlockId
        {
            get { return _blockId ?? 0; }
            set
            {
                _blockId = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _blockId = value
                };

                Comms.SyncSettings(settings);
            }
        }*/

        /*public Mode CurrentMode
        {
            get { return _currentMode ?? Mode.Repair; }
            set
            {
                _currentMode = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _currentMode = value
                };

                Comms.SyncSettings(settings);
            }
        }*/

        public long SelectedAssembler
        {
            get { return _selectedAssembler ?? 0; }
            set
            {
                _selectedAssembler = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _selectedAssembler = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public bool BoostEnabled
        {
            get { return _boostEnabled ?? false; }
            set
            {
                _boostEnabled = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _boostEnabled = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public bool AllowRepairs
        {
            get { return _allowRepairs ?? false; }
            set
            {
                _allowRepairs = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _allowRepairs = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public bool AllowBuild
        {
            get { return _allowBuild ?? false; }
            set
            {
                _allowBuild = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _allowBuild = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public bool Activated
        {
            get { return _activated ?? false; }
            set
            {
                _activated = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _activated = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public int TokenTimer
        {
            get { return _tokenTimer ?? 0; }
            set
            {
                _tokenTimer = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _tokenTimer = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public int BuildTimer
        {
            get { return _buildTimer ?? 0; }
            set
            {
                _buildTimer = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _buildTimer = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public bool Enabled
        {
            get { return _enabled ?? false; }
            set
            {
                _enabled = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _enabled = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public int TokensToAdd
        {
            get { return _tokensToAdd ?? 0; }
            set
            {
                _tokensToAdd = value;
                Settings settings = new Settings(_blockId ?? 0)
                {
                    _tokensToAdd = value
                };

                Comms.SyncSettings(settings);
            }
        }

        public static void SyncSettings(Settings settings)
        {
            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(settings._blockId, out entity)) return;

            var logic = entity.GameLogic.GetAs<Logic>();
            if (logic == null) return;

            if (settings._enabled.HasValue)
                logic.settings._enabled = settings._enabled;

            if (settings._buildTimer.HasValue)
                logic.settings._buildTimer = settings._buildTimer;

            if (settings._tokenTimer.HasValue)
                logic.settings._tokenTimer = settings._tokenTimer;

            if (settings._activated.HasValue)
                logic.settings._activated = settings._activated;

            if (settings._tokensToAdd.HasValue)
                logic.settings._tokensToAdd = settings._tokensToAdd;

            if (settings._allowRepairs.HasValue)
                logic.settings._allowRepairs = settings._allowRepairs;

            if (settings._allowBuild.HasValue)
                logic.settings._allowBuild = settings._allowBuild;

            if (settings._boostEnabled.HasValue)
                logic.settings._boostEnabled = settings._boostEnabled;

            //if (settings._currentMode.HasValue)
            //logic.settings._currentMode = settings._currentMode;

            if (settings._selectedAssembler.HasValue)
                logic.settings._selectedAssembler = settings._selectedAssembler;

            if (MyAPIGateway.Session.IsServer)
                logic.SaveSettings(logic.settings);

        }
    }
}
