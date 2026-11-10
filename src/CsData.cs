using System.Collections.Generic;

namespace CsArsenal
{
    // The CS2 guns. Every gun has four quality rows cloned from the game's own gun of the same class and tier
    // (Template + "0".."3": 标准手枪 1011, 轻质冲锋枪 1021, 沙漠之莺 2011, 地头蛇 2051) and scaled, so upgrades and
    // quality growth work like the game's guns. IDs are plain numbers: the game splits "18_<weapon id>_1" on '_'.
    public class GunSpec
    {
        public string Base;            // quality rows Base + "0".."3"; also the ClassID
        public string Template = "1011";
        public int Tier = 1;           // tech CS-<tier>; tier 2+ recipes are locked until that tech is researched
        public string Key;             // sprite stem and attachment name ("csa_" + Key)
        public string Name, Desc, Side = "both";   // Side: CT / T / both (agents of that side get +50 % on the effect)
        public string Category = "pistol";          // pistol / smg / shotgun / zeus
        public float AtkMul = 1f, ReloadMul = 1f, AntiArmor = -1f, BulletInterval = -1f, KnockDown = -1f;
        public float AimAdd;           // extra aiming time before each magazine (R8 trigger pull)
        public float RoundIntervalMul = 1f;   // time between shots (T_Weapon.Attack_Speed)
        public float Drag = -1f, Shocktime = -1f;   // slow on hit: strength and seconds
        public string Accuracy;        // the game's hit curve "inferior_limit_revise_red", null = the template's
        public int MagAdd, RangeAdd, AttackBullet = -1, Price;
        public float Headshot;         // chance per shot
        public int KillReward;         // credits per kill by the player's people
        public string RecipeId, Materials; public int Workload = 40;
        public string RecipeUnlockId;  // G_TechTreeItem "1002_<recipe>" listed by the gun's CS tech (NonBuilding_unlock)
        public string Row(int q) => Base + q;
    }

    public class ArmorSpec
    {
        public string Base, Key, Template, Name, Desc, RecipeId, RecipeUnlockId, Materials; public int DefendAdd, Price, Workload = 30; public float HeadProtect = -1f;   // Key: picture icons/<Key>.png
        public string Row(int q) => Base + q;
    }

    public static class CsData
    {
        // made on the game's own benches (user 11-09: no CS bench), the way the game spreads its guns: a tier's recipes on
        // that tier's gun bench and the next one up
        public static readonly Dictionary<int, string[]> GunBenches = new Dictionary<int, string[]>
        {
            [1] = new[] { "140", "141" }, [2] = new[] { "141", "142" }, [3] = new[] { "142", "143" }, [4] = new[] { "143" },
        };
        public static readonly string[] ArmorBenches = { "150", "151", "152", "153" };
        public const string TechId = "cs-1", TechTemplate = "ranged-1";
        public const string Tech2Id = "cs-2", Tech2Parent = "ranged-2", RecipeUnlockTemplate = "62011";
        public const string Tech3Id = "cs-3", Tech3Parent = "ranged-3", Tech4Id = "cs-4", Tech4Parent = "ranged-4";
        public const float HeadshotMonsterMul = 2.5f, HeadshotPersonMul = 2f;   // people: x2 (user 11-09, not the lethal brain hit)
        public const int ZeusExecuteHp = 1500, ZeusDamage = 400;

        public static readonly List<GunSpec> Guns = new List<GunSpec>
        {
            new GunSpec { Base = "8810", Key = "glock18", Name = "格洛克 18", Side = "T", AtkMul = 0.5f, AttackBullet = 3, BulletInterval = 0.08f, MagAdd = -1,
                Price = 4000, Headshot = 0.05f, KillReward = 2250, RecipeId = "88600", RecipeUnlockId = "88500", Materials = "12_15_40|12_16_70",
                Desc = "T 阵营的经典手枪。【连发切换】站定时三连发，移动时自动切成单发。【经济局】用它击杀的奖励 ×1.5。" },
            new GunSpec { Base = "8811", Key = "usp_silencer", Name = "USP 消音版", Side = "CT", AtkMul = 1.1f, RangeAdd = 3,
                Price = 4200, Headshot = 0.08f, KillReward = 1500, RecipeId = "88601", RecipeUnlockId = "88501", Materials = "12_15_40|12_16_80|12_58_2",
                Desc = "CT 阵营的消音手枪，安静而精准。【无声暗杀】对从没受过伤的目标，第一枪必定爆头。" },
            new GunSpec { Base = "8812", Key = "hkp2000", Name = "P2000", Side = "CT", AtkMul = 0.75f, ReloadMul = 0.45f,
                Price = 4200, Headshot = 0.06f, KillReward = 1500, RecipeId = "88602", RecipeUnlockId = "88502", Materials = "12_15_40|12_16_80|12_58_2",
                Desc = "CT 阵营的警用手枪。【警用快充】打空弹匣后换弹时间 −55%。" },
            new GunSpec { Base = "8813", Key = "p250", Name = "P250", AtkMul = 1.0f, AntiArmor = 0.5f,
                Price = 4500, Headshot = 0.06f, KillReward = 1500, RecipeId = "88603", RecipeUnlockId = "88503", Materials = "12_16_60|12_58_3|12_25_1",
                Desc = "廉价但凶狠的手枪。【破甲】无视目标 50% 护甲。" },
            new GunSpec { Base = "8814", Key = "elite", Name = "双持贝瑞塔", AtkMul = 0.6f, AttackBullet = 2, BulletInterval = 0.1f, MagAdd = 2, ReloadMul = 1.5f,
                Price = 4500, Headshot = 0.04f, KillReward = 1500, RecipeId = "88604", RecipeUnlockId = "88504", Materials = "12_15_60|12_16_100",
                Desc = "左右手各一把。【双枪乱舞】每次开火打出两发，附近有两个以上敌人时第二发打另一个目标。" },
            new GunSpec { Base = "8815", Key = "taser", Name = "宙斯 x27", Category = "zeus", AtkMul = 10f, MagAdd = -5, ReloadMul = 15f, RangeAdd = -12, KnockDown = 1f,
                Price = 5000, Headshot = 0f, KillReward = 3000, RecipeId = "88605", RecipeUnlockId = "88505", Materials = "12_15_30|12_55_20|12_59_2|12_25_1", Workload = 50,
                Desc = "近距离电击枪，只有一发。【电击处决】目标剩余生命不超过 1500 时直接击杀，否则造成 400 伤害；打完约 30 秒充能。" },

            // ---- CS-2 近距火力 (tier 2: pistols from 沙漠之莺 2011, SMGs from 轻质冲锋枪 1021 raised to tier-2 numbers,
            // shotguns from 地头蛇 2051) ----
            new GunSpec { Base = "8830", Key = "tec9", Name = "Tec-9", Tier = 2, Template = "2011", Side = "T", AtkMul = 0.7f, AttackBullet = 2, BulletInterval = 0.08f, MagAdd = 2,
                Price = 36000, Headshot = 0.05f, KillReward = 1500, RecipeId = "88610", RecipeUnlockId = "88510", Materials = "12_24_36|12_61_15|12_33_3", Workload = 30,
                Desc = "T 阵营的冲锋手枪，每次扣扳机两发。【跑轰】移动中射速 +30%；拿着它移动速度 +10%。" },
            new GunSpec { Base = "8831", Key = "fiveseven", Name = "FN57", Tier = 2, Template = "2011", Side = "CT", AtkMul = 0.85f, MagAdd = 3, AntiArmor = 0.6f,
                Price = 36500, Headshot = 0.06f, KillReward = 1500, RecipeId = "88611", RecipeUnlockId = "88511", Materials = "12_24_36|12_61_15|12_33_3", Workload = 30,
                Desc = "CT 阵营的穿甲手枪。【穿甲弹】每次命中让目标护甲 −10%（所有人打它都算），最多叠 3 层，持续 8 秒。" },
            new GunSpec { Base = "8832", Key = "cz75a", Name = "CZ75", Tier = 2, Template = "2011", AtkMul = 0.65f, MagAdd = 5, ReloadMul = 1.4f,
                Price = 35000, Headshot = 0.05f, KillReward = 500, RecipeId = "88612", RecipeUnlockId = "88512", Materials = "12_24_36|12_61_15|12_33_3", Workload = 30,
                Desc = "全自动手枪，弹匣大、换弹慢。【急速清空】每个弹匣的前 6 发射速 ×2。" },
            new GunSpec { Base = "8833", Key = "deagle", Name = "沙漠之鹰", Tier = 2, Template = "2011", AtkMul = 1.6f, AntiArmor = 0.7f, ReloadMul = 1.1f,
                Price = 38000, Headshot = 0.40f, KillReward = 1500, RecipeId = "88613", RecipeUnlockId = "88513", Materials = "12_24_36|12_61_15|12_33_3", Workload = 30,
                Desc = "大口径手枪。【一枪头】爆头率 40%；爆头击杀后立刻可以开下一枪，可以连续一枪头。" },
            new GunSpec { Base = "8834", Key = "revolver", Name = "R8 左轮", Tier = 2, Template = "2011", AtkMul = 2.0f, MagAdd = -1, AimAdd = 0.4f, AntiArmor = 0.7f,
                Price = 37000, Headshot = 0.15f, KillReward = 1500, RecipeId = "88614", RecipeUnlockId = "88514", Materials = "12_24_36|12_61_15|12_33_3", Workload = 30,
                Desc = "重型左轮。【扳机蓄力】每个弹匣开火前多蓄力 0.4 秒，伤害 ×2。【重创】被它打中的目标 4 秒内受到的所有伤害 +20%（谁打都算）。" },
            new GunSpec { Base = "8835", Key = "mac10", Name = "MAC-10", Tier = 2, Template = "1021", Category = "smg", Side = "T", AtkMul = 1.05f, MagAdd = 3, AntiArmor = 0.4f,
                Price = 38000, Headshot = 0.03f, KillReward = 3000, RecipeId = "88615", RecipeUnlockId = "88515", Materials = "12_24_36|12_61_20|12_33_3",
                Desc = "T 阵营的冲锋枪，射速快。【冲锋】拿着它移动速度 +15%；击杀后 3 秒内再 +25%。" },
            new GunSpec { Base = "8836", Key = "mp9", Name = "MP9", Tier = 2, Template = "1021", Category = "smg", Side = "CT", AtkMul = 1.0f, MagAdd = 2, AntiArmor = 0.5f,
                Price = 38500, Headshot = 0.04f, KillReward = 3000, RecipeId = "88616", RecipeUnlockId = "88516", Materials = "12_24_36|12_61_20|12_33_3",
                Desc = "CT 阵营的冲锋枪。【麻痹弹】命中叠加减速（每层 −12%，3 秒），叠满 5 层定身 1 秒。" },
            new GunSpec { Base = "8837", Key = "mp7", Name = "MP7", Tier = 2, Template = "1021", Category = "smg", AtkMul = 1.15f, MagAdd = 1, AntiArmor = 0.56f, Accuracy = "15_70_4_0.15",
                Price = 39000, Headshot = 0.10f, KillReward = 3000, RecipeId = "88617", RecipeUnlockId = "88517", Materials = "12_24_36|12_61_20|12_33_3",
                Desc = "精准的冲锋枪。【稳定点射】命中率按精确步枪算；爆头率 10%（冲锋枪里最高）。" },
            new GunSpec { Base = "8838", Key = "mp5sd", Name = "MP5-SD", Tier = 2, Template = "1021", Category = "smg", AtkMul = 1.1f, MagAdd = 2, AntiArmor = 0.5f,
                Price = 39000, Headshot = 0.05f, KillReward = 3000, RecipeId = "88618", RecipeUnlockId = "88518", Materials = "12_24_36|12_61_20|12_33_3",
                Desc = "消音冲锋枪。【消音游击】击杀后 3 秒内移动速度 +30%，并恢复 5% 意识。" },
            new GunSpec { Base = "8839", Key = "ump45", Name = "UMP-45", Tier = 2, Template = "1021", Category = "smg", AtkMul = 1.35f, MagAdd = 1, AntiArmor = 0.6f,
                Price = 38500, Headshot = 0.04f, KillReward = 3000, RecipeId = "88619", RecipeUnlockId = "88519", Materials = "12_24_36|12_61_20|12_33_3",
                Desc = "大口径冲锋枪。【近身重击】5 格以内伤害 +40%，并让目标 1 秒内无法开火。" },
            new GunSpec { Base = "8840", Key = "bizon", Name = "PP 野牛", Tier = 2, Template = "1021", Category = "smg", AtkMul = 0.9f, MagAdd = 8, ReloadMul = 0.4f, AntiArmor = 0.4f,
                Price = 38000, Headshot = 0.03f, KillReward = 3000, RecipeId = "88620", RecipeUnlockId = "88520", Materials = "12_24_36|12_61_20|12_33_3",
                Desc = "螺旋弹匣冲锋枪，弹匣极大。【螺旋弹匣】换弹只要 1 秒；每次击杀返还 10 发子弹。" },
            new GunSpec { Base = "8841", Key = "nova", Name = "新星", Tier = 2, Template = "2051", Category = "shotgun", AtkMul = 0.56f, AttackBullet = 9, Drag = 0.8f, Shocktime = 1.5f,
                Price = 42000, Headshot = 0.03f, KillReward = 4500, RecipeId = "88621", RecipeUnlockId = "88521", Materials = "12_24_32|12_61_30|12_33_2",
                Desc = "泵动霰弹枪。【重锤】命中让目标减速 80%，持续 1.5 秒。【近距破甲】3 格以内无视 40% 护甲。" },
            new GunSpec { Base = "8842", Key = "sawedoff", Name = "截短霰弹枪", Tier = 2, Template = "2051", Category = "shotgun", Side = "T", AtkMul = 1.0f, AttackBullet = 8, RangeAdd = -6, MagAdd = -2,
                Price = 41000, Headshot = 0.02f, KillReward = 4500, RecipeId = "88622", RecipeUnlockId = "88522", Materials = "12_24_32|12_61_30|12_33_2",
                Desc = "T 阵营的短管霰弹枪，一次 8 颗弹丸。【近距离撕裂】3 格以内伤害 ×1.5，越远越弱（最远处 ×0.5）。" },
            new GunSpec { Base = "8843", Key = "mag7", Name = "MAG-7", Tier = 2, Template = "2051", Category = "shotgun", Side = "CT", AtkMul = 0.75f, AttackBullet = 8, MagAdd = -2,
                Price = 43000, Headshot = 0.03f, KillReward = 4500, RecipeId = "88623", RecipeUnlockId = "88523", Materials = "12_24_32|12_61_30|12_33_2",
                Desc = "CT 阵营的弹匣式霰弹枪。【近身护卫】命中敌人后，自己 2 秒内受到的伤害 −25%。" },
            new GunSpec { Base = "8844", Key = "xm1014", Name = "XM1014", Tier = 2, Template = "2051", Category = "shotgun", AtkMul = 0.67f, AttackBullet = 6,
                Price = 44000, Headshot = 0.03f, KillReward = 4500, RecipeId = "88624", RecipeUnlockId = "88524", Materials = "12_24_32|12_61_30|12_33_2",
                Desc = "半自动霰弹枪。【连喷】连续命中同一个目标时每颗弹丸伤害 +3%，最多 +60%，换目标清零。" },

            // ---- CS-3 突击步枪 (tier 3: rifles from 碎颌者 3031, P90 from 向量改良 3021, SSG 08 from 监视 3061) ----
            new GunSpec { Base = "8850", Key = "galilar", Name = "加利尔 AR", Tier = 3, Template = "3031", Category = "rifle", Side = "T", AtkMul = 0.95f, MagAdd = 15,
                Price = 92000, Headshot = 0.05f, KillReward = 2250, RecipeId = "88625", RecipeUnlockId = "88525", Materials = "12_20_84|12_24_52|12_23_27", Workload = 75,
                Desc = "T 阵营的廉价步枪。【廉价火力】弹匣 35 发；击杀奖励 ×1.5。" },
            new GunSpec { Base = "8851", Key = "famas", Name = "法玛斯", Tier = 3, Template = "3031", Category = "rifle", Side = "CT", AtkMul = 1.0f, AttackBullet = 3, BulletInterval = 0.06f, MagAdd = -10,
                Price = 93000, Headshot = 0.06f, KillReward = 1500, RecipeId = "88626", RecipeUnlockId = "88526", Materials = "12_20_84|12_24_52|12_23_27", Workload = 75,
                Desc = "CT 阵营的三连发步枪。【三连点射】一轮三发全部打中同一个目标时，第三发必定爆头。" },
            new GunSpec { Base = "8852", Key = "ak47", Name = "AK-47", Tier = 3, Template = "3031", Category = "rifle", Side = "T", AtkMul = 1.3f, RoundIntervalMul = 1.1f,
                Price = 98000, Headshot = 0.20f, KillReward = 1500, RecipeId = "88627", RecipeUnlockId = "88527", Materials = "12_20_84|12_24_52|12_23_27", Workload = 75,
                Desc = "T 阵营的经典步枪，威力大。【一枪头】任何距离爆头率 20%，爆头伤害 ×3（其他枪 ×2）。" },
            new GunSpec { Base = "8853", Key = "m4a4", Name = "M4A4", Tier = 3, Template = "3031", Category = "rifle", Side = "CT", AtkMul = 1.15f, MagAdd = 10,
                Price = 97000, Headshot = 0.06f, KillReward = 1500, RecipeId = "88628", RecipeUnlockId = "88528", Materials = "12_20_84|12_24_52|12_23_27", Workload = 75,
                Desc = "CT 阵营的主战步枪。【持续压制】连续命中同一目标每发伤害 +5%（最多 +30%），被命中的目标减速 20%。" },
            new GunSpec { Base = "8854", Key = "m4a1_silencer", Name = "M4A1 消音版", Tier = 3, Template = "3031", Category = "rifle", Side = "CT", AtkMul = 1.15f, MagAdd = -5,
                Price = 97000, Headshot = 0.15f, KillReward = 1500, RecipeId = "88629", RecipeUnlockId = "88529", Materials = "12_20_84|12_24_52|12_23_27", Workload = 75,
                Desc = "CT 阵营的消音步枪，弹匣小而精准。【静默点射】每个弹匣的第一发打中必定爆头；爆头率 15%。" },
            new GunSpec { Base = "8855", Key = "sg556", Name = "SG 553", Tier = 3, Template = "3031", Category = "rifle", Side = "T", AtkMul = 1.25f, MagAdd = 10,
                Price = 99000, Headshot = 0.10f, KillReward = 1500, RecipeId = "88630", RecipeUnlockId = "88530", Materials = "12_20_84|12_24_52|12_23_27", Workload = 75,
                Desc = "T 阵营的带镜步枪。【自动开镜】站定 1 秒后开镜，伤害 +30%，一移动就收镜。" },
            new GunSpec { Base = "8856", Key = "aug", Name = "AUG", Tier = 3, Template = "3031", Category = "rifle", Side = "CT", AtkMul = 1.2f, MagAdd = 10,
                Price = 99000, Headshot = 0.10f, KillReward = 1500, RecipeId = "88631", RecipeUnlockId = "88531", Materials = "12_20_84|12_24_52|12_23_27", Workload = 75,
                Desc = "CT 阵营的带镜步枪。【开镜破甲】站定 1 秒后开镜，伤害 +30% 并无视 30% 护甲，一移动就收镜。" },
            new GunSpec { Base = "8857", Key = "p90", Name = "P90", Tier = 3, Template = "3021", Category = "smg", AtkMul = 0.85f, MagAdd = 8,
                Price = 94000, Headshot = 0.03f, KillReward = 1500, RecipeId = "88632", RecipeUnlockId = "88532", Materials = "12_20_100|12_24_100", Workload = 50,
                Desc = "弹匣 50 发的冲锋枪。【火力倾泻】弹匣只剩最后 20% 时射速 +30%。" },
            new GunSpec { Base = "8858", Key = "ssg08", Name = "SSG 08", Tier = 3, Template = "3061", Category = "sniper", AtkMul = 0.7f, MagAdd = 3, RoundIntervalMul = 0.7f,
                Price = 96000, Headshot = 0.25f, KillReward = 1500, RecipeId = "88633", RecipeUnlockId = "88533", Materials = "12_20_66|12_23_22|12_22_55", Workload = 70,
                Desc = "轻型狙击枪，开火间隔比同档狙击枪短 30%。【轻狙】拿着它移动速度 +10%；击杀后 3 秒内移动速度再 +30%。" },

            // ---- CS-4 精确与压制 (tier 4: snipers from 零号守备 4061, machine guns from 捍卫者机枪 4041) ----
            new GunSpec { Base = "8860", Key = "awp", Name = "AWP", Tier = 4, Template = "4061", Category = "sniper", AtkMul = 2.6f, MagAdd = -8, RoundIntervalMul = 1.5f,
                Price = 520000, Headshot = 0.30f, KillReward = 500, RecipeId = "88634", RecipeUnlockId = "88534", Materials = "12_20_60|12_39_40|12_42_3", Workload = 50,
                Desc = "重型狙击枪，伤害极高。【一枪一个】打满血的人形敌人一枪直接打倒；打怪物伤害 ×1.5。拿着它移动速度 −15%。" },
            new GunSpec { Base = "8861", Key = "g3sg1", Name = "G3SG1", Tier = 4, Template = "4061", Category = "sniper", Side = "T", AtkMul = 0.65f, MagAdd = 7, RoundIntervalMul = 0.3f,
                Price = 500000, Headshot = 0.20f, KillReward = 1500, RecipeId = "88635", RecipeUnlockId = "88535", Materials = "12_20_60|12_39_40|12_42_3", Workload = 50,
                Desc = "T 阵营的连发狙击枪。【连狙压制】命中让目标 2 秒内无法开火。" },
            new GunSpec { Base = "8862", Key = "scar20", Name = "SCAR-20", Tier = 4, Template = "4061", Category = "sniper", Side = "CT", AtkMul = 0.7f, MagAdd = 7, RoundIntervalMul = 0.3f, RangeAdd = 6,
                Price = 500000, Headshot = 0.25f, KillReward = 1500, RecipeId = "88636", RecipeUnlockId = "88536", Materials = "12_20_60|12_39_40|12_42_3", Workload = 50,
                Desc = "CT 阵营的连发狙击枪。【精确连狙】射程比同档狙击枪远 6 格，爆头率 25%。" },
            new GunSpec { Base = "8863", Key = "m249", Name = "M249", Tier = 4, Template = "4041", Category = "mg", AtkMul = 1.1f,
                Price = 490000, Headshot = 0.03f, KillReward = 1500, RecipeId = "88637", RecipeUnlockId = "88537", Materials = "12_20_25|12_39_32|12_42_10", Workload = 60,
                Desc = "轻机枪。【火力网】持续开火时，身边 5 格内的友军受到的伤害 −15%。" },
            new GunSpec { Base = "8864", Key = "negev", Name = "内格夫", Tier = 4, Template = "4041", Category = "mg", AtkMul = 0.9f, AttackBullet = 150, BulletInterval = 0.07f,
                Price = 495000, Headshot = 0.02f, KillReward = 1500, RecipeId = "88638", RecipeUnlockId = "88538", Materials = "12_20_25|12_39_32|12_42_10", Workload = 60,
                Desc = "射速极高的机枪，一次打 150 发。【越打越准】持续开火 2 秒后伤害 +50%，停火即重置。" },
        };

        // Kevlar vest and helmet: discontinued (user 11-10). The item, recipe and unlock rows stay defined so saves that
        // hold them still load, but no bench, tech or merchant offers them any more (CsDefLoader). ArmorBenches is where
        // they used to be made; the self-test checks they are gone from there.
        public static readonly List<ArmorSpec> Armor = new List<ArmorSpec>
        {
            new ArmorSpec { Base = "8820", Key = "kevlar", Template = "1252", Name = "凯夫拉防弹衣", DefendAdd = 4, Price = 3800, RecipeId = "88606", RecipeUnlockId = "88506",
                Materials = "12_16_40|12_1_20|12_58_2", Desc = "CS 同款防弹背心。比同档护甲多 4 点防御。" },
            new ArmorSpec { Base = "8821", Key = "helmet", Template = "1751", Name = "凯夫拉头盔", DefendAdd = 2, HeadProtect = 0.5f, Price = 2600, RecipeId = "88607", RecipeUnlockId = "88507",
                Materials = "12_16_30|12_1_10|12_58_1", Desc = "CS 同款头盔。【防爆头】被爆头的几率减半（所有头盔都按自己的头部保护降低被爆头的几率）。" },
        };

        // enemies: copies of the game's tier-1 guerrilla (soldier 0) carrying one CS pistol each, added (0..1) to the
        // tier-1 squads that already use soldiers 0 or 1; their guns drop like the game's (T_Weapon.Drop)
        public const string SoldierTemplate = "0", SoldierBase = "8890";   // tier 1 (guerrilla 0); tier 2 copies guerrilla 10
        public const string Soldier2Template = "10", Soldier3Template = "20", Soldier4Template = "30";
        // CS-1 kept their first IDs (88900..88904); later guns are 88910+
        public static string SoldierId(int gunIndex) => gunIndex < 10 ? SoldierBase + gunIndex : "889" + gunIndex;

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

        // one sentence like the game's own techs: the unlock popup has room for about two lines above its row of item
        // icons, and a list of every gun ran into them (user 11-10). The icons name the guns (hover or click).
        public static string TechDetail() => "粗制枪械加工台、自动枪械组装台可制造 CS 手枪与宙斯电击枪。";
        public static string Tech2Detail() => "自动枪械组装台、枪械组装流水线可制造 CS 手枪、冲锋枪与霰弹枪。";
        public static string Tech3Detail() => "枪械组装流水线、枪械组装中心可制造 CS 步枪、P90 与 SSG 08。";
        public static string Tech4Detail() => "枪械组装中心可制造 CS 狙击枪与机枪。";
    }
}
