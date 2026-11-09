using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace CsArsenal
{
    // CS-2 signature effects. State is kept per role UID and expires on a real-time clock (seconds).
    //   hit (BaseRole.beHit prefix, via OnHit)  FN57 armour shred (any attacker), MP9 slow stacks and root, UMP-45 close
    //                                            range, sawed-off distance falloff, XM1014 consecutive-hit stacks
    //   kill (beHit postfix, via OnKill)         MAC-10 / MP5-SD speed bursts, MP5-SD heal, PP-Bizon ammo refund,
    //                                            Desert Eagle headshot kill resets the shot interval
    //   AimingDevice.logic prefix                fire rate (Tec-9 moving, CZ75 first 6 shots), refunds and resets
    //   BaseHero.getSpeed postfix                Tec-9 / MAC-10 carry bonus and the kill bursts
    //   BaseBuild.beHit prefix                   MAG-7 double damage to buildings and doors
    public static class CsEffects
    {
        static readonly Stopwatch clock = Stopwatch.StartNew();
        public static double Now => clock.Elapsed.TotalSeconds;
        static readonly object gate = new object();

        class Timed { public int Stacks; public float Value; public double Until; }
        static readonly Dictionary<long, Timed> shred = new Dictionary<long, Timed>(), slow = new Dictionary<long, Timed>(), speed = new Dictionary<long, Timed>();
        static readonly Dictionary<long, KeyValuePair<long, int>> streak = new Dictionary<long, KeyValuePair<long, int>>();   // shooter -> (target, hits)
        // waiting for the shooter's next firing step (a kill often lands between magazines or while switching targets)
        static readonly Dictionary<long, KeyValuePair<int, double>> refund = new Dictionary<long, KeyValuePair<int, double>>();
        static readonly Dictionary<long, double> reset = new Dictionary<long, double>();
        static readonly Dictionary<long, float> rateCarry = new Dictionary<long, float>();
        static readonly Dictionary<long, double> lastMove = new Dictionary<long, double>();      // role -> last time it moved
        static readonly Dictionary<long, double> crit = new Dictionary<long, double>();          // shooter -> its next hit is a headshot (until)

        public static void Moved(BaseRole r) { lock (gate) lastMove[r.getUID()] = Now; }
        static bool Scoped(SpriteObject shooter) => shooter != null && (!lastMove.TryGetValue(shooter.getUID(), out double t) || Now - t >= 1.0);

        // FAMAS third shot of a burst, M4A1-S first shot of a magazine (BaseHero.doFight_Shoot)
        public static void MarkCrit(BaseRole shooter) { lock (gate) crit[shooter.getUID()] = Now + 1.5; }
        public static bool TakeCrit(SpriteObject shooter)
        {
            if (shooter == null) return false;
            lock (gate) { if (crit.TryGetValue(shooter.getUID(), out double u)) { crit.Remove(shooter.getUID()); return u >= Now; } }
            return false;
        }

        static float Active(Dictionary<long, Timed> d, long uid, out int stacks)
        {
            stacks = 0;
            if (!d.TryGetValue(uid, out var t)) return 0f;
            if (t.Until < Now) { d.Remove(uid); return 0f; }
            stacks = t.Stacks; return t.Value;
        }

        static float Cells(SpriteObject a, SpriteObject b) =>
            a == null || b == null ? 0f : MathTool.getDistanceF(a.localPos.x, a.localPos.y, b.localPos.x, b.localPos.y) / 32f;

        // before damage; g = the CS gun that fired (null for any other attacker)
        public static void OnHit(BaseRole target, ref HurtStruct h, GunSpec g)
        {
            long uid = target.getUID();
            lock (gate)
            {
                Active(shred, uid, out int sh);
                if (sh > 0) { h.ArmorPenetration = Math.Min(1f, h.ArmorPenetration + 0.1f * sh); CsStats.Add(CsStats.Effects, "fiveseven shred applied"); }
                if (g == null) return;
                switch (g.Key)
                {
                    case "fiveseven":
                        if (!shred.TryGetValue(uid, out var s) || s.Until < Now) shred[uid] = s = new Timed();
                        s.Stacks = Math.Min(3, s.Stacks + 1); s.Until = Now + 8; CsStats.Add(CsStats.Effects, "fiveseven stack");
                        break;
                    case "mp9":
                        if (!slow.TryGetValue(uid, out var m) || m.Until < Now) slow[uid] = m = new Timed();
                        m.Stacks++; m.Until = Now + 3;
                        if (m.Stacks >= 5) { h.slowLow = 0.99f; h.slowFps = 1f; m.Stacks = 0; CsStats.Add(CsStats.Effects, "mp9 root"); }
                        else { h.slowLow = Math.Max(h.slowLow, 0.12f * m.Stacks); h.slowFps = Math.Max(h.slowFps, 3f); CsStats.Add(CsStats.Effects, "mp9 slow"); }
                        break;
                    case "ump45":
                        if (Cells(h.from, target) <= 5f) { h.atk = (int)(h.atk * 1.4f); CsStats.Add(CsStats.Effects, "ump45 close"); }
                        break;
                    case "sawedoff":
                        {
                            float d = Cells(h.from, target), max = Math.Max(4f, (h.weaponDef?.Range ?? 400) / 32f);
                            float mul = d <= 3f ? 1.5f : 1.5f - (d - 3f) / Math.Max(1f, max - 3f);
                            h.atk = (int)(h.atk * Mathf.Clamp(mul, 0.5f, 1.5f)); CsStats.Add(CsStats.Effects, d <= 3f ? "sawedoff close" : "sawedoff far");
                            break;
                        }
                    case "m4a4":
                        {
                            long su = h.from?.getUID() ?? 0;
                            int n = streak.TryGetValue(su, out var kv) && kv.Key == uid ? kv.Value : 0;
                            h.atk = (int)(h.atk * (1f + 0.05f * Math.Min(6, n)));
                            streak[su] = new KeyValuePair<long, int>(uid, n + 1);
                            h.slowLow = Math.Max(h.slowLow, 0.2f); h.slowFps = Math.Max(h.slowFps, 1f);
                            CsStats.Add(CsStats.Effects, n > 0 ? "m4a4 stack" : "m4a4 hit");
                            break;
                        }
                    case "sg556":
                    case "aug":
                        if (Scoped(h.from))
                        {
                            h.atk = (int)(h.atk * 1.3f);
                            if (g.Key == "aug") h.ArmorPenetration = Math.Min(1f, h.ArmorPenetration + 0.3f);
                            CsStats.Add(CsStats.Effects, g.Key + " scoped");
                        }
                        break;
                    case "xm1014":
                        {
                            long su = h.from?.getUID() ?? 0;
                            int n = streak.TryGetValue(su, out var kv) && kv.Key == uid ? kv.Value : 0;
                            h.atk = (int)(h.atk * (1f + 0.03f * Math.Min(20, n)));
                            streak[su] = new KeyValuePair<long, int>(uid, n + 1);
                            if (n >= 20) CsStats.Add(CsStats.Effects, "xm1014 full stack");
                            break;
                        }
                }
            }
        }

        // the hit took the target down
        public static void OnKill(BaseRole target, HurtStruct h, GunSpec g, bool headshot)
        {
            if (!(h.from is BaseRole shooter)) return;
            long su = shooter.getUID();
            lock (gate)
            {
                switch (g.Key)
                {
                    case "mac10": speed[su] = new Timed { Value = 1.25f, Until = Now + 3 }; CsStats.Add(CsStats.Effects, "mac10 burst"); break;
                    case "mp5sd":
                        speed[su] = new Timed { Value = 1.30f, Until = Now + 3 }; CsStats.Add(CsStats.Effects, "mp5sd burst");
                        try { shooter.attrDriven.addHp((float)(shooter.roleBean.getMaxHp() * 0.05)); CsStats.Add(CsStats.Effects, "mp5sd heal"); } catch { }
                        break;
                    case "bizon":
                        int r = refund.TryGetValue(su, out var kv) && kv.Value > Now ? kv.Key : 0;
                        refund[su] = new KeyValuePair<int, double>(r + 10, Now + 5); CsStats.Add(CsStats.Effects, "bizon refund"); break;
                    case "deagle": if (headshot) { reset[su] = Now + 3; CsStats.Add(CsStats.Effects, "deagle reset"); } break;
                    case "ssg08": speed[su] = new Timed { Value = 1.30f, Until = Now + 3 }; CsStats.Add(CsStats.Effects, "ssg08 burst"); break;
                }
            }
        }

        public static float SpeedMul(BaseHero h)
        {
            float m = 1f;
            var g = CsData.ByWeapon(h.heroBean?.CurWeapon?.getWeaponDef()?.ID);
            if (g != null) { if (g.Key == "tec9" || g.Key == "ssg08") m *= 1.10f; else if (g.Key == "mac10") m *= 1.15f; }
            lock (gate) { float b = Active(speed, h.getUID(), out _); if (b > 0) m *= b; }
            return m;
        }

        // fire rate, refunds and resets on the shooter's aiming device (it counts the magazine and times the shots)
        public static void OnAim(BaseRole role, T_WeaponDef w, sbyte step, TimeDelay_Game ready, TimeDelay_Game round, TimeDelay_Game bullet, ref int fired)
        {
            var g = CsData.ByWeapon(w?.ID);
            if (g == null || role == null) return;
            long uid = role.getUID();
            if (role.mod_Move != null && !role.mod_Move.isFinish()) Moved(role);
            lock (gate)
            {
                // steps: 0 idle, 1 aiming, 2 firing the magazine, 3 magazine done (reloading: a refund is moot then)
                if (refund.TryGetValue(uid, out var rf))
                {
                    if (rf.Value < Now || step == 3) refund.Remove(uid);
                    else if (step == 2) { refund.Remove(uid); fired = Math.Max(0, fired - rf.Key); CsStats.Add(CsStats.Effects, "bizon rounds back"); }
                }
                if (reset.TryGetValue(uid, out double until))
                {
                    if (until < Now) reset.Remove(uid);
                    else if (step == 1) { reset.Remove(uid); ready.doFinish(); CsStats.Add(CsStats.Effects, "deagle next shot now"); }
                    else if (step == 2) { reset.Remove(uid); round.doFinish(); bullet.doFinish(); CsStats.Add(CsStats.Effects, "deagle next shot now"); }
                }
                float rate = 1f;
                if (g.Key == "tec9" && role.mod_Move != null && !role.mod_Move.isFinish()) rate = 1.3f;
                else if (g.Key == "cz75a" && fired < 6) rate = 2f;
                else if (g.Key == "p90" && fired >= 0.8f * w.Max_Bullet * w.Attack_Bullet) rate = 1.3f;
                if (rate <= 1f || step != 2) return;
                float extra = TimeInfo.elapsedTime * (rate - 1f);
                round.caseStep(extra); bullet.caseStep(extra);
                CsStats.Add(CsStats.Effects, g.Key + " fast step");
            }
        }

        public static void Clear() { lock (gate) { shred.Clear(); slow.Clear(); speed.Clear(); streak.Clear(); refund.Clear(); reset.Clear(); rateCarry.Clear(); lastMove.Clear(); crit.Clear(); } }
    }

    [HarmonyPatch(typeof(AimingDevice), nameof(AimingDevice.logic))]
    static class AimPatch
    {
        static void Prefix(BaseRole ___role, T_WeaponDef ___weaponDef, sbyte ___step, TimeDelay_Game ___readyInteval, TimeDelay_Game ___roundInteval, TimeDelay_Game ___bulletInteval, ref int ___totleBulletSum)
        {
            try { CsEffects.OnAim(___role, ___weaponDef, ___step, ___readyInteval, ___roundInteval, ___bulletInteval, ref ___totleBulletSum); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[CsArsenal] aim: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseHero), nameof(BaseHero.getSpeed))]
    static class SpeedPatch
    {
        static void Postfix(BaseHero __instance, ref float __result)
        {
            try
            {
                float m = CsEffects.SpeedMul(__instance); if (m != 1f) __result *= m;
                if (__instance.mod_Move != null && !__instance.mod_Move.isFinish()) CsEffects.Moved(__instance);   // SG 553 / AUG scope
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(BaseBuild), nameof(BaseBuild.beHit))]
    static class BuildHitPatch
    {
        static void Prefix(ref HurtStruct hurtStruct)
        {
            try
            {
                var g = CsData.ByWeapon(hurtStruct.weaponDef?.ID);
                if (g?.Key == "mag7") { hurtStruct.atk *= 2; CsStats.Add(CsStats.Effects, "mag7 building x2"); }
            }
            catch { }
        }
    }
}
