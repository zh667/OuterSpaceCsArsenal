using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsArsenal
{
    // Adds the round-0 guns and clothes while the game loads its tables (ModPluginManager.loadDefFromAssembly).
    // The pictures come from Resources/Textures/role-customize.xml (the game's own mod interface).
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
            try { AddGuns(); } catch (Exception e) { Debug.LogError("[CsArsenal] guns: " + e); }
            try { AddClothes(); } catch (Exception e) { Debug.LogError("[CsArsenal] clothes: " + e); }
            Debug.Log("[CsArsenal] defs: " + string.Join("; ", Report));
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
                T_WeaponDef r = ImplDef<T_WeaponDef>.getTemplateDef(g.Template);
                if (r == null) { Report.Add("no weapon template " + g.Template); continue; }
                r.ID = g.Id; r.mod = cfg; r.Name = g.Name; r.Desc = g.Desc; r.ClassID = g.Id.Substring(0, 5);
                r.b_Slot_Weapon = r.f_Slot_Weapon = r.s_Slot_Weapon = r.x3_Slot_Weapon = r.x9_Slot_Weapon = g.Attach;
                r.b_StandBy_Weapon = r.f_StandBy_Weapon = r.s_StandBy_Weapon = r.x3_StandBy_Weapon = r.x9_StandBy_Weapon = g.Attach;
                Replace(r); n++;
            }
            Report.Add("guns " + n);
        }

        void AddClothes()
        {
            int n = 0;
            foreach (var c in CsData.Clothes)
            {
                T_ClothDef r = ImplDef<T_ClothDef>.getTemplateDef(c.Template);
                if (r == null) { Report.Add("no cloth template " + c.Template); continue; }
                r.ID = c.Id; r.mod = cfg; r.Name = c.Name; r.Skin = c.Skin + "|" + c.Skin;   // male|female
                // HeroAnim.appointCloth draws clothSkin (parsed from Skin), and the copy still points at the template's
                r.clothSkin = new ClothSkin { boyWares = new[] { c.Skin }, girlWares = new[] { c.Skin } };
                Replace(r); n++;
            }
            Report.Add("clothes " + n);
        }
    }
}
