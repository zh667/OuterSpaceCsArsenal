using System.Collections.Generic;

namespace CsArsenal
{
    // Round 0: the six CS-1 guns plus the AK-47 (to see a rifle), and two agents' clothes (route 1).
    // IDs are plain numbers: the game splits "18_<weapon id>_1" style references on '_'.
    public class GunSpec
    {
        public string Id, Template, Name, Desc, Attach;   // Attach = attachment name in role-customize.xml
        public GunSpec(string id, string template, string name, string attach, string desc) { Id = id; Template = template; Name = name; Attach = attach; Desc = desc; }
    }

    public class ClothSpec
    {
        public string Id, Template, Name, Skin, Agent;
        public int Slot;   // CharacterEquip EquipType: 1 hat, 4 top, 6 trousers
        public ClothSpec(string id, string template, int slot, string name, string skin, string agent) { Id = id; Template = template; Slot = slot; Name = name; Skin = skin; Agent = agent; }
    }

    public static class CsData
    {
        public static readonly List<GunSpec> Guns = new List<GunSpec>
        {
            new GunSpec("881010", "10110", "格洛克 18", "csa_glock18", "CS2 格洛克 18（第 0 轮外观测试）"),
            new GunSpec("881020", "10110", "USP 消音版", "csa_usp_silencer", "CS2 USP-S（第 0 轮外观测试）"),
            new GunSpec("881030", "10110", "P2000", "csa_hkp2000", "CS2 P2000（第 0 轮外观测试）"),
            new GunSpec("881040", "10110", "P250", "csa_p250", "CS2 P250（第 0 轮外观测试）"),
            new GunSpec("881050", "10110", "双持贝瑞塔", "csa_elite", "CS2 双持贝瑞塔（第 0 轮外观测试）"),
            new GunSpec("881060", "10110", "宙斯 x27", "csa_taser", "CS2 宙斯 x27（第 0 轮外观测试）"),
            new GunSpec("883010", "10310", "AK-47", "csa_ak47", "CS2 AK-47（第 0 轮外观测试）"),
            // size variants for the in-hand comparison (round 0c)
            new GunSpec("881011", "10110", "格洛克 18 A2", "csa_glock18_a2", "加大版 A2"),
            new GunSpec("881012", "10110", "格洛克 18 A3", "csa_glock18_a3", "加大版 A3"),
            new GunSpec("883011", "10310", "AK-47 A2", "csa_ak47_a2", "加大版 A2"),
            new GunSpec("883012", "10310", "AK-47 A3", "csa_ak47_a3", "加大版 A3"),
            new GunSpec("881013", "10110", "格洛克 18 A4", "csa_glock18_a4", "加大 + 压暗 A4"),
            new GunSpec("883013", "10310", "AK-47 A4", "csa_ak47_a4", "加大 + 压暗 A4"),
        };

        // templates: 10140 作训上衣, 10160 作训下装, 12510 游击兵头盔
        public static readonly List<ClothSpec> Clothes = new List<ClothSpec>
        {
            new ClothSpec("882011", "10140", 4, "SAS 作战服", "CSA_SAS_TOP", "sas"),
            new ClothSpec("882012", "10160", 6, "SAS 作战裤", "CSA_SAS_BOTTOMS", "sas"),
            new ClothSpec("882013", "12510", 1, "SAS 头盔与防毒面具", "CSA_SAS_HELMET", "sas"),
            new ClothSpec("882021", "10140", 4, "凤凰战士夹克", "CSA_PHX_TOP", "phoenix"),
            new ClothSpec("882022", "10160", 6, "凤凰战士长裤", "CSA_PHX_BOTTOMS", "phoenix"),
            new ClothSpec("882023", "12510", 1, "凤凰战士头套", "CSA_PHX_HELMET", "phoenix"),
        };
    }
}
