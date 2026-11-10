using System;
using System.Collections.Generic;
using System.Linq;
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
            Try("guns", AddGuns); Try("armor", AddArmor); Try("recipes", AddRecipes); Try("benches", AddToBenches); Try("tech", AddTech); Try("tech2", AddTech2); Try("tech3", AddTech3); Try("tech4", AddTech4);
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
                    r.Attack_Speed *= g.RoundIntervalMul;
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
                    string fx = g.Category == "zeus" ? "laser" : g.Key == "deagle" || g.Key == "revolver" || g.Category == "sniper" ? "sniper" : "machine";
                    if (fx == "laser") { r.FireType = "laser\\LaserMuzzle"; r.BulletId = "laser\\Laser Bullet 1"; r.HitHuman = r.HitLand = r.HitOther = "laser\\Laser Hit 1"; }
                    else if (fx == "sniper") { r.FireType = "sniper rifle\\SniperMuzzle"; r.BulletId = "sniper rifle\\Sniper Bullet"; r.HitHuman = r.HitLand = r.HitOther = "sniper rifle\\Sniper Hit"; }
                    else { r.FireType = "machine gun\\MachineMuzzle"; r.BulletId = "machine gun\\Machine Bullet"; r.HitHuman = r.HitLand = r.HitOther = "machine gun\\Machine Hit"; }
                    r.BulletSpeed = fx == "sniper" ? 50f : g.Category == "shotgun" || g.Category == "rifle" || g.Category == "mg" ? 45f : g.Category == "smg" ? 38f : g.Category == "zeus" ? 60f : 35f;
                    r.Obtain = BenchNames(CsData.GunBenches[g.Tier]) + "；商人售卖" + (g.Category == "zeus" ? "" : "；敌人掉落");
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
                    r.ID = a.Row(q); r.mod = cfg; r.ClassID = a.Base; r.Name = a.Name; r.Desc = "（已停产）" + a.Desc;
                    r.Defend += a.DefendAdd; if (a.HeadProtect >= 0) r.HeadProtect = a.HeadProtect;
                    r.Price = a.Price; r.Obtain = "已停产，只剩旧存档里已有的（商人不卖也不收）";   // discontinued 11-10 (CsData.Armor)
                    r.RawIcon = "icons/" + a.Key + ".png";   // CS2 HUD silhouette (tools/assets/make_icons.py)
                    Replace(r); n++;
                    ModIconCombine.additions.RemoveAll(b => b is T_ClothDef && b.ID == r.ID);
                    ModIconCombine.addRow(r);
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
                    b.ID = "886" + k.ToString("000"); b.mod = cfg; b.ItemId = it.type + "_" + it.id; b.DescItem = it.name;
                    b.itemReward = new FWRewardBean(int.Parse(it.type), it.id, 0);
                    // discontinued armour: the row stays (a caravan in a save may still hold it) but in no goods group,
                    // so no merchant stocks it any more; nor buys it (the purchase row below, user 11-10)
                    if (it.armour) b.ItemGroup = -1;
                    Replace(b); if (!it.armour) sold++;
                }
                MapTraderSellDef s = ImplDef<MapTraderSellDef>.getTemplateDef("493");
                if (s == null) Report.Add("no merchant purchase template 493");
                else
                {
                    s.ID = "887" + k.ToString("000"); s.mod = cfg; s.ItemId = it.type + "_" + it.id; s.DescItem = it.name;
                    s.itemReward = new FWRewardBean(int.Parse(it.type), it.id, 0);
                    if (it.armour) s.ItemGroup = -1;
                    Replace(s); if (!it.armour) bought++;
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
                string tpl = g.Tier == 1 ? CsData.SoldierTemplate : g.Tier == 2 ? CsData.Soldier2Template : g.Tier == 3 ? CsData.Soldier3Template : CsData.Soldier4Template;
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
                int tier = sq.Desc.StartsWith("1级无邦者") ? 1 : sq.Desc.StartsWith("2级无邦者") ? 2 : sq.Desc.StartsWith("3级无邦者") ? 3 : sq.Desc.StartsWith("4级无邦者") ? 4 : 0;
                if (tier == 0 || !byTier.TryGetValue(tier, out var ids)) continue;
                // the tier's guerrilla, assault and (tier 3) rifle soldiers
                var kinds = tier == 1 ? new[] { "0", "1" } : tier == 2 ? new[] { "10", "11" } : tier == 3 ? new[] { "20", "21", "22" } : new[] { "32", "33", "35" };
                bool uses = false;
                foreach (var part in sq.SoliderSelected.Split('|')) { var id = part.Split('_')[0]; if (kinds.Contains(id)) uses = true; }
                if (!uses) continue;
                var pick = ids[Math.Abs(StableHash(sq.ID)) % ids.Count];
                sq.SoliderSelected += "|" + pick + "_0_1"; squads++;
            }
            Report.Add("soldiers " + n + " in " + squads + " squads");
        }

        void AddTech3()
        {
            TechnologyDef d = ImplDef<TechnologyDef>.getTemplateDef(CsData.Tech3Parent);
            if (d == null) { Report.Add("no tech template " + CsData.Tech3Parent); return; }
            var unlocks = CsData.Guns.Where(g => g.Tier == 3 && g.RecipeUnlockId != null).Select(g => g.RecipeUnlockId).ToList();
            d.ID = CsData.Tech3Id; d.mod = cfg; d.Name = "CS-3 突击步枪"; d.Detail = CsData.Tech3Detail(); d.Detail_2 = "";
            d.Building_unlock = "-1"; d.NonBuilding_unlock = string.Join("|", unlocks);
            d.Pre_technology = CsData.Tech3Parent; d.PathNode = "ranged-3,t:16250.0,925:l"; d.HexGridX = 16350; d.HexGridY = 925;   // tools/tech_layout.py
            d.S_Materials = "12_41_1|12_20_12|12_33_4"; d.Level = 3;
            Replace(d);
            Report.Add("tech " + d.ID + " with " + unlocks.Count + " recipes");
        }

        void AddTech4()
        {
            TechnologyDef d = ImplDef<TechnologyDef>.getTemplateDef(CsData.Tech4Parent);
            if (d == null) { Report.Add("no tech template " + CsData.Tech4Parent); return; }
            var unlocks = CsData.Guns.Where(g => g.Tier == 4 && g.RecipeUnlockId != null).Select(g => g.RecipeUnlockId).ToList();
            d.ID = CsData.Tech4Id; d.mod = cfg; d.Name = "CS-4 精确与压制"; d.Detail = CsData.Tech4Detail(); d.Detail_2 = "";
            d.Building_unlock = "-1"; d.NonBuilding_unlock = string.Join("|", unlocks);
            d.Pre_technology = CsData.Tech4Parent; d.PathNode = "ranged-4,b:22450.0,250:l"; d.HexGridX = 22550; d.HexGridY = 250;   // tools/tech_layout.py
            d.S_Materials = "12_42_18|12_39_22|12_41_3"; d.Level = 4;
            Replace(d);
            Report.Add("tech " + d.ID + " with " + unlocks.Count + " recipes");
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
                r.Show_Toast = "分类：" + (g.Category == "zeus" ? "电击枪" : g.Category == "smg" ? "冲锋枪" : g.Category == "shotgun" ? "霰弹枪" : g.Category == "rifle" ? "步枪" : g.Category == "sniper" ? "狙击枪" : g.Category == "mg" ? "机枪" : "手枪");
                Icon(r, "icons/" + g.Key + ".png");
                Replace(r); n++;
                // the recipe stays locked until its CS tech is researched (the game's own guns do the same)
                RecipeUnlock(g.RecipeUnlockId, g.RecipeId, g.Name, g.Desc, CsData.GunBenches[g.Tier], "icons/" + g.Key + ".png");
            }
            // discontinued armour (11-10): its recipe and unlock rows stay defined for saves that already know them, but
            // no bench lists the recipe and no tech unlocks it
            foreach (var a in CsData.Armor)
            {
                F_ProductListDef r = ImplDef<F_ProductListDef>.getTemplateDef("61011");
                r.ID = a.RecipeId; r.mod = cfg; r.Name = a.Name; r.Desc = a.Desc;
                r.Materials = a.Materials; r.OutMaterial = "17_" + a.Row(0) + "_1"; r.Workload = a.Workload; r.Show_Toast = "分类：护甲";
                Icon(r, "icons/" + a.Key + ".png");
                Replace(r); n++;
                RecipeUnlock(a.RecipeUnlockId, a.RecipeId, a.Name, a.Desc, CsData.ArmorBenches, "icons/" + a.Key + ".png");
            }
            Report.Add("recipes " + n);
        }

        // Our picture on a row cloned from a game row (CommonIcon draws RawIcon when a row has one); otherwise the
        // template's icon shows, e.g. the game's pistol in the tech tree's "unlocked items" (user 11-10)
        static void Icon(BaseDef row, string rawIcon)
        {
            row.RawIcon = rawIcon;
            ModIconCombine.additions.RemoveAll(b => b.GetType() == row.GetType() && b.ID == row.ID);
            ModIconCombine.addRow(row);
        }

        void RecipeUnlock(string id, string recipe, string name, string desc, string[] benches, string rawIcon)
        {
            if (id == null) return;
            TechnologyUnlockItemDef u = ImplDef<TechnologyUnlockItemDef>.getTemplateDef(CsData.RecipeUnlockTemplate);
            if (u == null) { Report.Add("no recipe unlock template"); return; }
            u.ID = id; u.mod = cfg; u.SubType = 1; u.AimItem = "1002_" + recipe; u.Name = name;
            u.Desc = "[a5a5a5]" + desc + "\n加工：" + BenchNames(benches) + "。";
            Icon(u, rawIcon);
            Replace(u);
        }

        static string BenchNames(string[] ids) =>
            string.Join("、", ids.Select(id => ImplDef<F_ManufactureDef>.beans.FirstOrDefault(b => b.ID == id)?.Name ?? id));

        // the game's own benches list the CS recipes too (no bench of our own, user 11-09)
        void AddToBenches()
        {
            int n = 0;
            void Put(string benchId, string recipe)
            {
                var b = ImplDef<F_ManufactureDef>.beans.FirstOrDefault(x => x.ID == benchId);
                if (b == null) { Report.Add("no bench " + benchId); return; }
                var list = (b.ProductID ?? "").Split('|').Where(s => s.Length > 0 && s != "-1").ToList();
                if (!list.Contains(recipe)) { list.Add(recipe); b.ProductID = string.Join("|", list); n++; }
            }
            foreach (var g in CsData.Guns) foreach (var id in CsData.GunBenches[g.Tier]) Put(id, g.RecipeId);
            Report.Add("recipes on the game's benches " + n);
        }

        void AddTech()
        {
            TechnologyDef d = ImplDef<TechnologyDef>.getTemplateDef(CsData.TechTemplate);
            if (d == null) { Report.Add("no tech template"); return; }
            // place found by tools/tech_layout.py (no overlap with the game's cards, lines or the EfTurrets nodes)
            d.ID = CsData.TechId; d.mod = cfg; d.Name = "CS-1 手枪训练"; d.Detail = CsData.TechDetail(); d.Detail_2 = "";
            var unlocks = CsData.Guns.Where(g => g.Tier == 1).Select(g => g.RecipeUnlockId).Where(x => x != null);   // no armour since 11-10
            d.Building_unlock = "-1"; d.NonBuilding_unlock = string.Join("|", unlocks);
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
            // place from tools/tech_layout.py. Below its parent, so the line leaves the parent's bottom edge ("b:"); "t:"
            // starts at the top edge and ran through the parent's icon (user 11-10)
            d.ID = CsData.Tech2Id; d.mod = cfg; d.Name = "CS-2 近距火力"; d.Detail = CsData.Tech2Detail(); d.Detail_2 = "";
            d.Building_unlock = "-1"; d.NonBuilding_unlock = string.Join("|", unlocks);
            d.Pre_technology = CsData.Tech2Parent; d.PathNode = "ranged-2,b:9900.0,450:l"; d.HexGridX = 10000; d.HexGridY = 450;
            d.S_Materials = "12_24_8|12_33_2|12_62_1"; d.Level = 2;
            Replace(d);
            Report.Add("tech " + d.ID + " with " + unlocks.Count + " recipes");
        }
    }
}
