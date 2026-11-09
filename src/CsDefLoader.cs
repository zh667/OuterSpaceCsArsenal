using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsArsenal
{
    // Adds the guns, the CS bench, its recipes and the CS tech nodes while the game loads its tables
    // (ModPluginManager.loadDefFromAssembly, before the rows are initialised and linked). Pictures come from
    // Resources/Textures/role-customize.xml (weapons) and Resources/Textures/icons (item icons).
    public class CsDefLoader : BaseModDefLoader
    {
        readonly ModXmlManifest cfg;
        public static string ResourcesDir;
        public static readonly List<string> Report = new List<string>();

        public CsDefLoader(ModXmlManifest config) : base(config) { cfg = config; }

        public override void initModDef()
        {
            Report.Clear();
            try { ResourcesDir = cfg.getVersionPath("Resources"); } catch (Exception e) { Debug.LogError("[CsArsenal] path: " + e); }
            Try("guns", AddGuns); Try("armor", AddArmor); Try("recipes", AddRecipes); Try("bench", AddBench); Try("tech", AddTech); Try("tech2", AddTech2);
            Try("traders", AddTraders); Try("soldiers", AddSoldiers);
            Debug.Log("[CsArsenal] defs: " + string.Join("; ", Report));
        }

        static void Try(string what, Action a)
        {
            try { a(); } catch (Exception e) { Report.Add(what + " FAILED " + e.Message); Debug.LogError("[CsArsenal] " + what + ": " + e); }
        }

        static void Replace<T>(T row) where T : ImplDef<T>
        {
            ImplDef<T>.beans.RemoveAll(b => b.ID == row.ID);
            ImplDef<T>.beans.Add(row);
        }

        void AddGuns()
        {
            int n = 0;
            var sounds = CsSound.Index();
            Report.Add("sounds " + (sounds?.Count ?? 0));
            foreach (var g in CsData.Guns)
            {
                for (int q = 0; q < 4; q++)
                {
                    T_WeaponDef r = ImplDef<T_WeaponDef>.getTemplateDef(g.Template + q);
                    if (r == null) { Report.Add("no weapon template " + g.Template + q); continue; }
                    r.ID = g.Row(q); r.mod = cfg; r.ClassID = g.Base; r.Name = g.Name;
                    r.Desc = g.Desc + (g.Category == "zeus" ? "" : "\n爆头率 " + Math.Round(g.Headshot * 100) + "%：爆头对人伤害 ×2，对怪物 ×2.5（头盔可能挡住）；用它击杀敌人奖励 " + g.KillReward + " 信用点。");
                    string a = "csa_" + g.Key;
                    r.b_Slot_Weapon = r.f_Slot_Weapon = r.s_Slot_Weapon = r.x3_Slot_Weapon = r.x9_Slot_Weapon = a;
                    r.b_StandBy_Weapon = r.f_StandBy_Weapon = r.s_StandBy_Weapon = r.x3_StandBy_Weapon = r.x9_StandBy_Weapon = a;
                    r.ATK = Math.Max(1, (int)Math.Round(r.ATK * g.AtkMul));
                    r.Max_Bullet = Math.Max(1, r.Max_Bullet + g.MagAdd);
                    if (g.AttackBullet > 0) r.Attack_Bullet = g.AttackBullet;
                    if (g.BulletInterval >= 0) r.Bullet_Inteval = g.BulletInterval;
                    r.Reload *= g.ReloadMul;
                    r.Range = Math.Max(2, r.Range + g.RangeAdd); r.Distance = Math.Max(3, r.Distance + g.RangeAdd);
                    if (g.AntiArmor >= 0) r.AntiArmor = g.AntiArmor;
                    if (g.KnockDown >= 0) r.KnockDown = g.KnockDown;
                    r.Inteval += g.AimAdd;
                    if (g.Drag >= 0) r.Drag = g.Drag;
                    if (g.Shocktime >= 0) r.Shocktime = g.Shocktime;
                    if (g.Accuracy != null) r.Accuracy = g.Accuracy;
                    r.Price = g.Price;
                    // CS2 sounds (CsSound) and the closest of the game's real-bullet effects
                    if (sounds != null && sounds.TryGetValue(g.Key, out var se))
                    {
                        if (se.fire.Count > 0) r.ATKMusic = CsSound.Prefix + g.Key + "_fire";
                        if (se.reload != null) r.ReloadMusic = CsSound.Prefix + g.Key + "_reload";
                    }
                    string fx = g.Category == "zeus" ? "laser" : g.Key == "deagle" || g.Key == "revolver" ? "sniper" : "machine";
                    if (fx == "laser") { r.FireType = "laser\\LaserMuzzle"; r.BulletId = "laser\\Laser Bullet 1"; r.HitHuman = r.HitLand = r.HitOther = "laser\\Laser Hit 1"; }
                    else if (fx == "sniper") { r.FireType = "sniper rifle\\SniperMuzzle"; r.BulletId = "sniper rifle\\Sniper Bullet"; r.HitHuman = r.HitLand = r.HitOther = "sniper rifle\\Sniper Hit"; }
                    else { r.FireType = "machine gun\\MachineMuzzle"; r.BulletId = "machine gun\\Machine Bullet"; r.HitHuman = r.HitLand = r.HitOther = "machine gun\\Machine Hit"; }
                    r.BulletSpeed = fx == "sniper" ? 50f : g.Category == "shotgun" ? 45f : g.Category == "smg" ? 38f : g.Category == "zeus" ? 60f : 35f;
                    r.Obtain = g.Category == "zeus" ? "CS 军械台；商人售卖" : "CS 军械台；商人售卖；敌人掉落";
                    r.RawIcon = "icons/" + g.Key + ".png";
                    Replace(r);
                    ModIconCombine.additions.RemoveAll(b => b is T_WeaponDef && b.ID == r.ID);
                    ModIconCombine.addRow(r);
                    n++;
                }
            }
            Report.Add("weapon rows " + n);
        }

        void AddArmor()
        {
            int n = 0;
            foreach (var a in CsData.Armor)
                for (int q = 0; q < 4; q++)
                {
                    T_ClothDef r = ImplDef<T_ClothDef>.getTemplateDef(a.Template + q);
                    if (r == null) { Report.Add("no armour template " + a.Template + q); continue; }
                    r.ID = a.Row(q); r.mod = cfg; r.ClassID = a.Base; r.Name = a.Name; r.Desc = a.Desc;
                    r.Defend += a.DefendAdd; if (a.HeadProtect >= 0) r.HeadProtect = a.HeadProtect;
                    r.Price = a.Price; r.Obtain = "CS 军械台；商人售卖";
                    Replace(r); n++;
                }
            Report.Add("armour rows " + n);
        }

        void AddTraders()
        {
            // Map caravans draw their goods from MapTraderBuyDef (weapon merchants: group 1, one row per quality with the
            // game's weights 80/16/4/1) and buy from the player what MapTraderSellDef lists. Rows go into both tables.
            var items = new List<(string type, string id, string name, int q, bool armour)>();
            foreach (var g in CsData.Guns) for (int q = 0; q < 4; q++) items.Add(("18", g.Row(q), g.Name, q, false));
            foreach (var a in CsData.Armor) for (int q = 0; q < 4; q++) items.Add(("17", a.Row(q), a.Name, q, true));
            int sold = 0, bought = 0, k = 0;
            foreach (var it in items)
            {
                // goods: templates are the game's own weapon-merchant rows (493..496 standard pistol, 5..8 guerrilla armour)
                string bt = it.armour ? (5 + it.q).ToString() : (493 + it.q).ToString();
                MapTraderBuyDef b = ImplDef<MapTraderBuyDef>.getTemplateDef(bt);
                if (b == null) Report.Add("no merchant goods template " + bt);
                else
                {
                    b.ID = "886" + k.ToString("00"); b.mod = cfg; b.ItemId = it.type + "_" + it.id; b.DescItem = it.name;
                    b.itemReward = new FWRewardBean(int.Parse(it.type), it.id, 0);
                    Replace(b); sold++;
                }
                MapTraderSellDef s = ImplDef<MapTraderSellDef>.getTemplateDef("493");
                if (s == null) Report.Add("no merchant purchase template 493");
                else
                {
                    s.ID = "887" + k.ToString("00"); s.mod = cfg; s.ItemId = it.type + "_" + it.id; s.DescItem = it.name;
                    s.itemReward = new FWRewardBean(int.Parse(it.type), it.id, 0);
                    Replace(s); bought++;
                }
                k++;
            }
            Report.Add("weapon merchant goods " + sold + ", purchases " + bought);
        }

        void AddSoldiers()
        {
            // one enemy kind per gun: a copy of the game's guerrilla of the gun's tier (0 / 10) carrying it, added (0..1)
            // to the squads of that tier that already use the tier's guerrilla or assault soldiers
            int n = 0, squads = 0;
            var byTier = new Dictionary<int, List<string>>();
            for (int i = 0; i < CsData.Guns.Count; i++)
            {
                var g = CsData.Guns[i];
                if (g.Category == "zeus") continue;   // a one-shot stun gun is no raider's weapon
                string tpl = g.Tier == 1 ? CsData.SoldierTemplate : CsData.Soldier2Template;
                ESoliderConfigDef s = ImplDef<ESoliderConfigDef>.getTemplateDef(tpl);
                if (s == null) { Report.Add("no soldier template " + tpl); continue; }
                s.ID = CsData.SoldierId(i); s.mod = cfg;
                var eq = (s.Equip ?? "").Split('_'); if (eq.Length > 0) eq[0] = g.Row(0); s.Equip = string.Join("_", eq);
                s.Name = s.Name + "（" + g.Name + "）";
                Replace(s); n++;
                if (!byTier.TryGetValue(g.Tier, out var l)) byTier[g.Tier] = l = new List<string>();
                l.Add(s.ID);
            }
            foreach (var sq in ImplDef<E_SquadConfig_Def>.beans)
            {
                if (sq == null || sq.SoliderSelected == null || sq.Desc == null || sq.SoliderSelected.Contains("|" + CsData.SoldierBase.Substring(0, 3))) continue;
                int tier = sq.Desc.StartsWith("1级无邦者") ? 1 : sq.Desc.StartsWith("2级无邦者") ? 2 : 0;
                if (tier == 0 || !byTier.TryGetValue(tier, out var ids)) continue;
                string a = tier == 1 ? "0" : "10", b = tier == 1 ? "1" : "11";
                bool uses = false;
                foreach (var part in sq.SoliderSelected.Split('|')) { var id = part.Split('_')[0]; if (id == a || id == b) uses = true; }
                if (!uses) continue;
                var pick = ids[Math.Abs(StableHash(sq.ID)) % ids.Count];
                sq.SoliderSelected += "|" + pick + "_0_1"; squads++;
            }
            Report.Add("soldiers " + n + " in " + squads + " squads");
        }

        static int StableHash(string s) { int h = 17; foreach (char c in s) h = h * 31 + c; return h; }

        void AddRecipes()
        {
            int n = 0;
            foreach (var g in CsData.Guns)
            {
                F_ProductListDef r = ImplDef<F_ProductListDef>.getTemplateDef("61011");
                if (r == null) { Report.Add("no recipe template 61011"); return; }
                r.ID = g.RecipeId; r.mod = cfg; r.Name = g.Name; r.Desc = g.Desc;
                r.Materials = g.Materials; r.OutMaterial = "18_" + g.Row(0) + "_1"; r.Workload = g.Workload;
                r.Show_Toast = "分类：" + (g.Category == "zeus" ? "电击枪" : g.Category == "smg" ? "冲锋枪" : g.Category == "shotgun" ? "霰弹枪" : "手枪");
                Replace(r); n++;
                if (g.RecipeUnlockId == null) continue;
                // a tier 2+ recipe stays locked until its tech is researched (the game's own tier-2 guns do the same)
                TechnologyUnlockItemDef u = ImplDef<TechnologyUnlockItemDef>.getTemplateDef(CsData.RecipeUnlockTemplate);
                if (u == null) { Report.Add("no recipe unlock template"); continue; }
                u.ID = g.RecipeUnlockId; u.mod = cfg; u.SubType = 1; u.AimItem = "1002_" + g.RecipeId; u.Name = g.Name;
                u.Desc = "[a5a5a5]" + g.Desc + "\n加工：CS 军械台。";
                Replace(u);
            }
            foreach (var a in CsData.Armor)
            {
                F_ProductListDef r = ImplDef<F_ProductListDef>.getTemplateDef("61011");
                r.ID = a.RecipeId; r.mod = cfg; r.Name = a.Name; r.Desc = a.Desc;
                r.Materials = a.Materials; r.OutMaterial = "17_" + a.Row(0) + "_1"; r.Workload = a.Workload; r.Show_Toast = "分类：护甲";
                Replace(r); n++;
            }
            Report.Add("recipes " + n);
        }

        void AddBench()
        {
            F_ManufactureDef b = ImplDef<F_ManufactureDef>.getTemplateDef(CsData.BenchTemplate);
            if (b == null) { Report.Add("no bench template"); return; }
            b.ID = CsData.BenchId; b.mod = cfg; b.Name = "CS 军械台";
            b.Desc = "按 CS2 图纸组装手枪、冲锋枪、霰弹枪、电击枪和凯夫拉护甲的工作台。";
            var ids = new List<string>(); foreach (var g in CsData.Guns) ids.Add(g.RecipeId); foreach (var a in CsData.Armor) ids.Add(a.RecipeId);
            b.ProductID = string.Join("|", ids);
            Replace(b);
            TechnologyUnlockItemDef u = ImplDef<TechnologyUnlockItemDef>.getTemplateDef(CsData.BenchUnlockTemplate);
            if (u == null) { Report.Add("no unlock template"); return; }
            u.ID = CsData.BenchUnlockId; u.mod = cfg; u.SubType = 1; u.AimItem = "27_" + CsData.BenchId; u.Name = b.Name; u.Desc = b.Desc;
            Replace(u);
            Report.Add("bench " + b.ID + " with " + ids.Count + " recipes");
        }

        void AddTech()
        {
            TechnologyDef d = ImplDef<TechnologyDef>.getTemplateDef(CsData.TechTemplate);
            if (d == null) { Report.Add("no tech template"); return; }
            // place found by tools/tech_layout.py (no overlap with the game's cards, lines or the EfTurrets nodes)
            d.ID = CsData.TechId; d.mod = cfg; d.Name = "CS-1 手枪训练"; d.Detail = CsData.TechDetail(); d.Detail_2 = "";
            d.Building_unlock = CsData.BenchUnlockId; d.NonBuilding_unlock = "-1";
            d.Pre_technology = "ranged-1"; d.PathNode = "ranged-1,t:950.0,800:l"; d.HexGridX = 1050; d.HexGridY = 800;
            d.S_Materials = "12_15_40|12_16_40|12_58_2"; d.Level = 1;
            Replace(d);
            Report.Add("tech " + d.ID);
        }

        void AddTech2()
        {
            TechnologyDef d = ImplDef<TechnologyDef>.getTemplateDef(CsData.Tech2Parent);
            if (d == null) { Report.Add("no tech template " + CsData.Tech2Parent); return; }
            var unlocks = new List<string>(); foreach (var g in CsData.Guns) if (g.Tier == 2 && g.RecipeUnlockId != null) unlocks.Add(g.RecipeUnlockId);
            // place from tools/tech_layout.py; the guns are made on the CS bench (CS-1), so CS-1 is needed in practice
            d.ID = CsData.Tech2Id; d.mod = cfg; d.Name = "CS-2 近距火力"; d.Detail = CsData.Tech2Detail(); d.Detail_2 = "";
            d.Building_unlock = "-1"; d.NonBuilding_unlock = string.Join("|", unlocks);
            d.Pre_technology = CsData.Tech2Parent; d.PathNode = "ranged-2,t:9900.0,450:l"; d.HexGridX = 10000; d.HexGridY = 450;
            d.S_Materials = "12_24_8|12_33_2|12_62_1"; d.Level = 2;
            Replace(d);
            Report.Add("tech " + d.ID + " with " + unlocks.Count + " recipes");
        }
    }
}
