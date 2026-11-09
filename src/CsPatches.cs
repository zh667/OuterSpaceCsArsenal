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
    //   FWRoleBean.randomHurtPart a headshot on a person hits the brain (the game's own lethal part, DeadType 1)
    //   BaseHero.doFight_Shoot    Glock: single shots while moving (bursts only when standing)
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
            Kills = new Dictionary<string, int>(), Executes = new Dictionary<string, int>(), Skipped = new Dictionary<string, int>();
        public static long Credits;
        // self-test only: who hits the watched targets (role UID -> "source weapon" counts)
        public static readonly HashSet<long> Watched = new HashSet<long>();
        public static readonly Dictionary<string, int> Sources = new Dictionary<string, int>();
        public static void Add(Dictionary<string, int> d, string k) { lock (d) { d.TryGetValue(k, out int v); d[k] = v + 1; } }
        public static int Get(Dictionary<string, int> d, string k) { lock (d) { return d.TryGetValue(k, out int v) ? v : 0; } }
    }

    [HarmonyPatch(typeof(BaseRole), nameof(BaseRole.beHit))]
    static class BeHitPatch
    {
        [ThreadStatic] public static bool ForceHead;

        // dead, fallen, or out of health (some deaths are settled on the next logic update)
        static bool Down(BaseRole r) => r.roleBean == null || r.roleBean.isDead() || r.roleBean.isFallDown() || r.roleBean.getHp() <= 0;

        static void Prefix(BaseRole __instance, ref HurtStruct hurtStruct, out bool __state)
        {
            __state = false;
            try
            {
                if (CsStats.Watched.Count > 0 && CsStats.Watched.Contains(__instance.getUID()))
                    CsStats.Add(CsStats.Sources, (hurtStruct.from == null ? "null" : hurtStruct.from.GetType().Name) + " " + (hurtStruct.weaponDef?.ID ?? "-") + " " + hurtStruct.atkType);
                var g = CsData.ByWeapon(hurtStruct.weaponDef?.ID);
                if (g == null || Down(__instance)) return;
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
                if (g.Key == "usp_silencer" && __instance.roleBean.getHpProgress_总血量() >= 0.999f) chance = 1f;   // unhurt target
                if (chance <= 0 || UnityEngine.Random.value >= chance) return;
                CsStats.Add(CsStats.Headshots, g.Key);
                if (person) ForceHead = true;                                   // randomHurtPart returns the brain
                else hurtStruct.atk = (int)(hurtStruct.atk * CsData.HeadshotMonsterMul);
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
                if (!(hurtStruct.from is BaseHero shooter) || shooter.getAlliance() != 1 || __instance.getAlliance() == 1) return;
                var gd = UserBean.tryGetInstance()?.gameData;
                if (gd == null) return;
                gd.addCP(g.KillReward, "CS 击杀奖励");
                lock (CsStats.Kills) CsStats.Credits += g.KillReward;
                Popup(shooter, "+" + g.KillReward + " 信用点");
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] kill reward: " + e.Message); }
        }

        static Exception Finalizer(Exception __exception) { ForceHead = false; return __exception; }

        static void Popup(BaseRole r, string s) { try { GUI_RoleInfo.showRoleInfo(r, s); } catch { } }
    }

    [HarmonyPatch(typeof(FWRoleBean), nameof(FWRoleBean.randomHurtPart))]
    static class HurtPartPatch
    {
        static void Postfix(FWRoleBean __instance, ref FWRolePart __result)
        {
            if (!BeHitPatch.ForceHead) return;
            foreach (var p in __instance.parts)
            {
                var d = p?.getDef();
                if (d != null && d.DeadType == 1) { __result = p; return; }
            }
        }
    }

    [HarmonyPatch(typeof(BaseHero), nameof(BaseHero.doFight_Shoot))]
    static class GlockPatch
    {
        // Glock: the burst is three shots (shootIndex 0..2); while moving only the first one is fired
        static bool Prefix(BaseHero __instance, int shootIndex)
        {
            try
            {
                var w = __instance.heroBean?.CurWeapon?.getWeaponDef();
                var g = CsData.ByWeapon(w?.ID);
                if (g == null || g.Key != "glock18" || shootIndex % 3 == 0) return true;
                if (__instance.mod_Move != null && !__instance.mod_Move.isFinish()) { CsStats.Add(CsStats.Skipped, g.Key); return false; }
            }
            catch { }
            return true;
        }
    }
}
