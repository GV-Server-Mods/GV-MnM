using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using VRage.Game;

namespace AutoRepair
{
    [ProtoContract]
    public class Config
    {
        [ProtoIgnore] [XmlIgnore] public string Version = "1.00";
        [ProtoMember(1)] [XmlElement("Boost_Consumption_Item")] public string boostItem = "MyObjectBuilder_Component/ZoneChip";
        [ProtoMember(2)] [XmlElement("Boost_Time_Increase_Amount_Seconds")] public int boostTime = 600;
        [ProtoMember(3)] [XmlElement("Boost_Multiplier")] public float boostAmount = 1.5f;
        [ProtoMember(4)] [XmlElement("Base_Time_Multiplier")] public float baseAmount = 1.1f;
        [ProtoMember(5)] [XmlElement("Built_Percentage")] public float built;
        [ProtoMember(6)] [XmlElement("ContainerTag")] public string containerTag = "[mnm]";

        public Config()
        {
        }

        public static Config LoadConfig()
        {
            Config config = new Config();
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage("Config.xml", typeof(Config)) == true)
            {
                var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("Config.xml", typeof(Config));
                config = MyAPIGateway.Utilities.SerializeFromXML<Config>(reader.ReadToEnd());
                reader.Close();
            }
            else
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("Config.xml", typeof(Config)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML<Config>(config));
                    writer.Close();
                }
            }

            MyDefinitionId id;
            if (MyDefinitionId.TryParse(config.boostItem, out id))
                Session.Instance.token = id.SubtypeName;

            return config;
        }
    }
}
