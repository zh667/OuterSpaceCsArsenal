using System.Collections.Generic;

namespace CsArsenal
{
    // CS-1: the six CS2 pistols. Every gun has four quality rows cloned from the game's own pistol of the same
    // quality ("1011" + 0..3, 标准手枪) and scaled, so upgrades and quality growth work like the game's guns.
    // IDs are plain numbers: the game splits "18_<weapon id>_1" style references on '_'.
    public class GunSpec
    {
        public string Base;            // quality rows Base + "0".."3"; also the ClassID
        public string Template = "1011";
        public string Key;             // sprite stem and attachment name ("csa_" + Key)
        public string Name, Desc, Side = "both";   // Side: CT / T / both (agents of that side get +50 % on the effect)
        public string Category = "pistol";
        public float AtkMul = 1f, ReloadMul = 1f, AntiArmor = -1f, BulletInterval = -1f, KnockDown = -1f;
        public int MagAdd, RangeAdd, AttackBullet = -1, Price;
        public float Headshot;         // chance per shot
        public int KillReward;         // credits per kill by the player's people
        public string RecipeId, Materials; public int Workload = 40;
        public string Row(int q) => Base + q;
    }

    public static class CsData
    {
        public const string BenchId = "884", BenchTemplate = "140", BenchUnlockId = "88401", BenchUnlockTemplate = "2334";
        public const string TechId = "cs-1", TechTemplate = "ranged-1";
        public const float HeadshotMonsterMul = 2.5f;
        public const int ZeusExecuteHp = 1500, ZeusDamage = 400;

        public static readonly List<GunSpec> Guns = new List<GunSpec>
        {
            new GunSpec { Base = "8810", Key = "glock18", Name = "格洛克 18", Side = "T", AtkMul = 0.5f, AttackBullet = 3, BulletInterval = 0.08f, MagAdd = -1,
                Price = 4000, Headshot = 0.05f, KillReward = 2250, RecipeId = "88600", Materials = "12_15_40|12_16_70",
                Desc = "T 阵营的经典手枪。【连发切换】站定时三连发，移动时自动切成单发。【经济局】用它击杀的奖励 ×1.5。" },
            new GunSpec { Base = "8811", Key = "usp_silencer", Name = "USP 消音版", Side = "CT", AtkMul = 1.1f, RangeAdd = 3,
                Price = 4200, Headshot = 0.08f, KillReward = 1500, RecipeId = "88601", Materials = "12_15_40|12_16_80|12_58_2",
                Desc = "CT 阵营的消音手枪，安静而精准。【无声暗杀】对满血目标的第一枪必定爆头。" },
            new GunSpec { Base = "8812", Key = "hkp2000", Name = "P2000", Side = "CT", AtkMul = 0.75f, ReloadMul = 0.45f,
                Price = 4200, Headshot = 0.06f, KillReward = 1500, RecipeId = "88602", Materials = "12_15_40|12_16_80|12_58_2",
                Desc = "CT 阵营的警用手枪。【警用快充】打空弹匣后换弹时间 −55%。" },
            new GunSpec { Base = "8813", Key = "p250", Name = "P250", AtkMul = 1.0f, AntiArmor = 0.5f,
                Price = 4500, Headshot = 0.06f, KillReward = 1500, RecipeId = "88603", Materials = "12_16_60|12_58_3|12_25_1",
                Desc = "廉价但凶狠的手枪。【破甲】无视目标 50% 护甲。" },
            new GunSpec { Base = "8814", Key = "elite", Name = "双持贝瑞塔", AtkMul = 0.6f, AttackBullet = 2, BulletInterval = 0.1f, MagAdd = 2, ReloadMul = 1.5f,
                Price = 4500, Headshot = 0.04f, KillReward = 1500, RecipeId = "88604", Materials = "12_15_60|12_16_100",
                Desc = "左右手各一把。【双枪乱舞】每次开火打出两发，附近有两个以上敌人时第二发打另一个目标。" },
            new GunSpec { Base = "8815", Key = "taser", Name = "宙斯 x27", Category = "zeus", AtkMul = 10f, MagAdd = -5, ReloadMul = 15f, RangeAdd = -12, KnockDown = 1f,
                Price = 5000, Headshot = 0f, KillReward = 3000, RecipeId = "88605", Materials = "12_15_30|12_55_20|12_59_2|12_25_1", Workload = 50,
                Desc = "近距离电击枪，只有一发。【电击处决】目标剩余生命不超过 1500 时直接击杀，否则造成 400 伤害；打完约 30 秒充能。" },
        };

        static Dictionary<string, GunSpec> byRow;
        public static GunSpec ByWeapon(string weaponId)
        {
            if (weaponId == null) return null;
            if (byRow == null)
            {
                byRow = new Dictionary<string, GunSpec>();
                foreach (var g in Guns) for (int q = 0; q < 4; q++) byRow[g.Row(q)] = g;
            }
            return byRow.TryGetValue(weaponId, out var s) ? s : null;
        }

        public static string TechDetail()
        {
            var names = new List<string>(); foreach (var g in Guns) names.Add(g.Name);
            return "解锁 CS 军械台，可制造：" + string.Join("、", names) + "。";
        }
    }
}
