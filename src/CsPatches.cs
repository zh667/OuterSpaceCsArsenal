using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CsArsenal
{
    // Harmony patches (Workshop item com.harmony is a declared dependency).
    //   BaseRole.beHit            headshots, Zeus execution, P250 armour piercing, USP first-shot headshot; afterwards
    //                             kill rewards: the hit took a standing target down (dead or fallen: monsters die,
    //                             animals and people fall first, enemies then become prisoners)
    //   (CS-2 signature effects: CsEffects.cs)
    //   BaseHero.doFight_Shoot    Glock: single shots while moving (bursts only when standing); Dual Berettas: the
    //                             second shot goes to another enemy the shooter hates, if one is standing in range
    public static class CsPatches
    {
        public static Harmony H;
        public static string Error;

        public static void Apply()
        {
            if (H != null) return;
            try { H = new Harmony("zh667.csarsenal"); H.PatchAll(typeof(CsPatches).Assembly); Debug.Log("[CsArsenal] patches applied"); }
            catch (Exception e) { Error = e.ToString(); Debug.LogError("[CsArsenal] patches: " + e); }
        }
    }

    // counters for the self-test and the log
    public static class CsStats
    {
        public static readonly Dictionary<string, int> Hits = new Dictionary<string, int>(), Headshots = new Dictionary<string, int>(),
            Kills = new Dictionary<string, int>(), Executes = new Dictionary<string, int>(), Skipped = new Dictionary<string, int>(),
            Retargets = new Dictionary<string, int>(), HelmetSaves = new Dictionary<string, int>(), Shots = new Dictionary<string, int>(),
            Effects = new Dictionary<string, int>();   // CS-2 signature effects, by "gun what"
        public static long Credits;
        // self-test only: who hits the watched targets (role UID -> "source weapon" counts)
        public static readonly HashSet<long> Watched = new HashSet<long>();
        public static readonly Dictionary<string, int> Sources = new Dictionary<string, int>();
        // targets hit by anything at all since the game started (the USP's "unhurt target")
        public static readonly HashSet<long> EverHit = new HashSet<long>();
        public static void Add(Dictionary<string, int> d, string k) { lock (d) { d.TryGetValue(k, out int v); d[k] = v + 1; } }
        public static int Get(Dictionary<string, int> d, string k) { lock (d) { return d.TryGetValue(k, out int v) ? v : 0; } }
    }

    [HarmonyPatch(typeof(BaseRole), nameof(BaseRole.beHit))]
    static class BeHitPatch
    {
        [ThreadStatic] static bool headshot;   // this hit was a headshot (Desert Eagle reset)

        // dead, fallen, or out of health (some deaths are settled on the next logic update)
        static bool Down(BaseRole r) => r.roleBean == null || r.roleBean.isDead() || r.roleBean.isFallDown() || r.roleBean.getHp() <= 0;

        static void Prefix(BaseRole __instance, ref HurtStruct hurtStruct, out bool __state)
        {
            __state = false;
            try
            {
                if (CsStats.Watched.Count > 0 && CsStats.Watched.Contains(__instance.getUID()))
                    CsStats.Add(CsStats.Sources, (hurtStruct.from == null ? "null" : hurtStruct.from.GetType().Name) + " " + (hurtStruct.weaponDef?.ID ?? "-") + " " + hurtStruct.atkType);
                long uid = __instance.getUID(); bool firstHit;
                lock (CsStats.EverHit) { firstHit = CsStats.EverHit.Add(uid); if (CsStats.EverHit.Count > 20000) CsStats.EverHit.Clear(); }
                var g = CsData.ByWeapon(hurtStruct.weaponDef?.ID);
                headshot = false;
                if (Down(__instance)) return;
                CsEffects.OnHit(__instance, ref hurtStruct, g);   // FN57 shred counts for every attacker
                if (g == null) return;
                __state = true;   // a standing target hit by a CS gun: the postfix checks whether this hit took it down
                CsStats.Add(CsStats.Hits, g.Key);
                bool person = __instance.roleBean is FWHeroBean;
                if (g.Category == "zeus")
                {
                    if (__instance.roleBean.getHp() <= CsData.ZeusExecuteHp) { hurtStruct.atk = 999999; CsStats.Add(CsStats.Executes, g.Key); Popup(__instance, "电击处决"); }
                    return;
                }
                if (g.AntiArmor > 0) hurtStruct.ArmorPenetration = Math.Max(hurtStruct.ArmorPenetration, g.AntiArmor);
                float chance = g.Headshot;
                if (g.Key == "usp_silencer" && firstHit) chance = 1f;   // the first hit a target ever takes
                if ((g.Key == "famas" || g.Key == "m4a1_silencer") && CsEffects.TakeCrit(hurtStruct.from)) { chance = 1f; CsStats.Add(CsStats.Effects, g.Key + " crit"); }
                if (chance <= 0 || UnityEngine.Random.value >= chance) return;
                if (person && HelmetBlocks(__instance)) { CsStats.Add(CsStats.HelmetSaves, g.Key); return; }   // the helmet took it
                CsStats.Add(CsStats.Headshots, g.Key);
                // damage only (user 11-09): a headshot on a person no longer forces the brain, which killed outright
                float mul = person ? CsData.HeadshotPersonMul : CsData.HeadshotMonsterMul;
                if (g.Key == "ak47") { mul = 3f; CsStats.Add(CsStats.Effects, "ak47 headshot x3"); }
                hurtStruct.atk = (int)(hurtStruct.atk * mul);
                headshot = true;
                Popup(__instance, "爆头");
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] beHit: " + e.Message); }
        }

        static void Postfix(BaseRole __instance, HurtStruct hurtStruct, bool __state)
        {
            if (!__state) return;
            try
            {
                if (!Down(__instance)) return;
                var g = CsData.ByWeapon(hurtStruct.weaponDef?.ID);
                if (g == null) return;
                CsStats.Add(CsStats.Kills, g.Key);
                CsEffects.OnKill(__instance, hurtStruct, g, headshot);
                if (!(hurtStruct.from is BaseHero shooter) || shooter.getAlliance() != 1 || __instance.getAlliance() == 1) return;
                var gd = UserBean.tryGetInstance()?.gameData;
                if (gd == null) return;
                long reward = (long)Math.Round(g.KillReward * CsConfig.Get().killRewardMultiplier);
                if (reward <= 0) return;
                gd.addCP(reward, "CS 击杀奖励");
                lock (CsStats.Kills) CsStats.Credits += reward;
                Popup(shooter, "+" + reward + " 信用点");
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] kill reward: " + e.Message); }
        }

        // any helmet stops a headshot with its own head protection (T_ClothDef.HeadProtect; 凯夫拉头盔 0.5)
        static bool HelmetBlocks(BaseRole r)
        {
            var hb = r.roleBean as FWHeroBean;
            if (hb?.cloths == null) return false;
            foreach (var e in hb.cloths)
                if (e != null && e.getEquipType() == 1 && e.hasEquip())
                {
                    float p = e.getClothDef()?.HeadProtect ?? 0f;
                    return p > 0 && UnityEngine.Random.value < p;
                }
            return false;
        }


        static void Popup(BaseRole r, string s) { try { GUI_RoleInfo.showRoleInfo(r, s); } catch { } }
    }

    [HarmonyPatch(typeof(BaseHero), nameof(BaseHero.doFight_Shoot))]
    static class ShootPatch
    {
        static bool Prefix(BaseHero __instance, ref SpriteObject target, int shootIndex)
        {
            try
            {
                var w = __instance.heroBean?.CurWeapon?.getWeaponDef();
                var g = CsData.ByWeapon(w?.ID);
                if (g == null) return true;
                CsStats.Add(CsStats.Shots, g.Key);
                if (g.Key == "glock18" && shootIndex % 3 != 0 && __instance.mod_Move != null && !__instance.mod_Move.isFinish())
                { CsStats.Add(CsStats.Skipped, g.Key); return false; }   // moving: single shots
                if ((g.Key == "famas" && shootIndex % 3 == 2) || (g.Key == "m4a1_silencer" && shootIndex == 0)) CsEffects.MarkCrit(__instance);
                if (g.Key == "elite" && shootIndex % 2 == 1)
                {
                    var other = OtherEnemy(__instance, target, w.Range);
                    if (other != null) { target = other; CsStats.Add(CsStats.Retargets, g.Key); }
                }
            }
            catch { }
            return true;
        }

        // another standing enemy in the shooter's range, nearest to the first target: the shooter's hated ones,
        // the map's monsters, and people of the hostile side (alliance 2)
        static SpriteObject OtherEnemy(BaseHero h, SpriteObject current, int rangePx)
        {
            SpriteObject best = null; float bestD = float.MaxValue;
            void Consider(SpriteObject s)
            {
                if (s == null || s == current || s == h) return;
                if (s is BaseRole r && (r.roleBean == null || r.roleBean.isDead() || r.roleBean.isFallDown() || r.getAlliance() == h.getAlliance())) return;
                if (MathTool.getDistanceI(h.localPos.x, h.localPos.y, s.localPos.x, s.localPos.y) > rangePx) return;
                float d = current == null ? 0 : MathTool.getDistanceI(current.localPos.x, current.localPos.y, s.localPos.x, s.localPos.y);
                if (d < bestD) { best = s; bestD = d; }
            }
            var list = h.roleBean?.fightHatreds?.hatreds;
            if (list != null) foreach (var it in list) Consider(it?.getHateSprite());
            var mm = MapManager.getInstance();
            var monsters = mm?.monsterManager?.getMonsters();
            if (monsters != null) foreach (var m in monsters) Consider(m);
            var foes = mm?.heroManager?.getAllianceFaction(2)?.getRoles();
            if (foes != null) foreach (var f in foes) Consider(f);
            return best;
        }
    }
}

namespace CsArsenal
{
    // Self-test only: the save's other colonists stand still during a fight test (they used to run over, shoot the
    // monsters and take the last hit). Empty outside the tests.
    [HarmonyLib.HarmonyPatch(typeof(BaseRole), "runLive")]
    static class TestFreezePatch
    {
        public static readonly System.Collections.Generic.HashSet<long> Frozen = new System.Collections.Generic.HashSet<long>();
        static bool Prefix(BaseRole __instance)
        {
            if (Frozen.Count == 0) return true;
            lock (Frozen) return !Frozen.Contains(__instance.getUID());
        }
    }

    // Game bug (0.9.0020, SpineCombine.createAttachment): the first attachment made from a mod weapon picture gets the
    // picture's size, every later one made from the same picture comes from the static pathAtlas cache through a
    // SetRegion overload that scales by 0.01, so it is 1/100 of the size. Each picture is used by two or more slots
    // (e.g. weapon_handGun_s and weapon_handGunShot_s), and the shot slots are the ones drawn while aiming and firing:
    // the guns vanished in hand (11-09). Dropping our picture from the cache before each attachment keeps every one on
    // the first, correct path.
    [HarmonyLib.HarmonyPatch]
    static class WeaponAttachmentSizePatch
    {
        static System.Reflection.MethodBase TargetMethod() => HarmonyLib.AccessTools.Method(typeof(SpineCombine), "createAttachment");
        static System.Reflection.FieldInfo cache;
        public static int Cleared;

        static void Prefix(string useTex, ModSpineWeapon weaponConfig)
        {
            try
            {
                if (weaponConfig?.Name == null || !weaponConfig.Name.StartsWith("csa_") || useTex == null) return;
                if (cache == null) cache = HarmonyLib.AccessTools.Field(typeof(SpineCombine), "pathAtlas");
                if (cache?.GetValue(null) is System.Collections.IDictionary d && d.Contains(useTex)) { d.Remove(useTex); Cleared++; }
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("[CsArsenal] weapon attachment: " + e.Message); }
        }
    }
}
