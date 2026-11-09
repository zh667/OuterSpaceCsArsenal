using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsArsenal
{
    // Adds the guns, the CS bench, its recipes and the CS-1 tech node while the game loads its tables
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
            Try("guns", AddGuns); Try("recipes", AddRecipes); Try("bench", AddBench); Try("tech", AddTech);
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
            foreach (var g in CsData.Guns)
            {
                for (int q = 0; q < 4; q++)
                {
                    T_WeaponDef r = ImplDef<T_WeaponDef>.getTemplateDef(g.Template + q);
                    if (r == null) { Report.Add("no weapon template " + g.Template + q); continue; }
                    r.ID = g.Row(q); r.mod = cfg; r.ClassID = g.Base; r.Name = g.Name; r.Desc = g.Desc;
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
                    r.Price = g.Price;
                    r.Obtain = "CS 军械台；商人售卖；掉落获取";
                    r.RawIcon = "icons/" + g.Key + ".png";
                    Replace(r);
                    ModIconCombine.additions.RemoveAll(b => b is T_WeaponDef && b.ID == r.ID);
                    ModIconCombine.addRow(r);
                    n++;
                }
            }
            Report.Add("weapon rows " + n);
        }

        void AddRecipes()
        {
            int n = 0;
            foreach (var g in CsData.Guns)
            {
                F_ProductListDef r = ImplDef<F_ProductListDef>.getTemplateDef("61011");
                if (r == null) { Report.Add("no recipe template 61011"); return; }
                r.ID = g.RecipeId; r.mod = cfg; r.Name = g.Name; r.Desc = g.Desc;
                r.Materials = g.Materials; r.OutMaterial = "18_" + g.Row(0) + "_1"; r.Workload = g.Workload;
                r.Show_Toast = g.Category == "zeus" ? "分类：电击枪" : "分类：手枪";
                Replace(r); n++;
            }
            Report.Add("recipes " + n);
        }

        void AddBench()
        {
            F_ManufactureDef b = ImplDef<F_ManufactureDef>.getTemplateDef(CsData.BenchTemplate);
            if (b == null) { Report.Add("no bench template"); return; }
            b.ID = CsData.BenchId; b.mod = cfg; b.Name = "CS 军械台";
            b.Desc = "按 CS2 图纸组装手枪与电击枪的工作台。";
            var ids = new List<string>(); foreach (var g in CsData.Guns) ids.Add(g.RecipeId);
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
    }
}
