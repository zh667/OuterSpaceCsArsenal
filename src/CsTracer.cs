using System;
using HarmonyLib;
using UnityEngine;

namespace CsArsenal
{
    // CS look for bullets and shotguns.
    //   Bullet_Normal.shootBullet postfix  a CS gun's bullet hides the game's bullet particle and draws a CS tracer: a thin
    //                                      pale-yellow streak, bright at the head, fading to the tail, following the
    //                                      bullet's drawn position (BulletAnim_Base offx/offy, displayRotate)
    //   BulletManager.addBullet prefix     CS shotguns: each pellet gets its own spread angle (a cone, like CS), so at range
    //                                      some pellets miss; the game's bullets are real rays, the spread is real
    //   AimingDevice.logic postfix         CS shotguns fire the whole round of pellets in the same frame (the game's timer
    //                                      lets one bullet out per frame, which looked like a burst)
    public class CsTracer : MonoBehaviour
    {
        static Sprite sprite;
        SpriteRenderer sr; BaseBullet bullet; Transform anim;
        // the game bullet's own renderers (its particle effect), found once: searching them every frame was 60 % of the
        // mod's time in a 12-gun fight (perf test 11-10). Searched again when the bullet's children change (the particle
        // effect can be created after the shot) or the bullet is reused.
        Renderer[] gameRenderers; int scannedChildren = -1;
        public bool On;
        public static int Live;   // tracer components alive (perf log)
        void OnDestroy() { Live--; }
        public float Length = 46f, Width = 1.6f;

        static readonly AccessTools.FieldRef<BulletAnim_Base, float> offx = AccessTools.FieldRefAccess<BulletAnim_Base, float>("offx");
        static readonly AccessTools.FieldRef<BulletAnim_Base, float> offy = AccessTools.FieldRefAccess<BulletAnim_Base, float>("offy");
        static readonly AccessTools.FieldRef<BulletAnim_Base, float> rot = AccessTools.FieldRefAccess<BulletAnim_Base, float>("displayRotate");
        static readonly AccessTools.FieldRef<BaseBullet, BulletAnim_Base> animOf = AccessTools.FieldRefAccess<BaseBullet, BulletAnim_Base>("anim");

        // self-test: CS bullets show the streak and hide the game's particle; other bullets show the game's particle
        public static void Audit(int[] count)
        {
            foreach (var t in FindObjectsOfType<CsTracer>())
            {
                if (t.bullet == null || !t.bullet.gameObject.activeInHierarchy || t.anim == null) continue;
                bool gameShown = false;
                foreach (var r in t.anim.GetComponentsInChildren<Renderer>(true)) if (r != t.sr && r.enabled) gameShown = true;
                if (t.On && t.Length > 0) { count[0]++; if (t.sr.enabled && !gameShown) count[1]++; }
                else { count[2]++; if (!t.sr.enabled && (gameShown || t.anim.GetComponentsInChildren<Renderer>(true).Length == 0)) count[3]++; }
            }
        }

        public static void WarmSprite() { Streak(); }

        static Sprite Streak()
        {
            if (sprite != null) return sprite;
            // 32 x 2, transparent tail (left) to opaque head (right); pivot at the head
            var t = new Texture2D(32, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 32; x++)
            {
                float a = Mathf.Pow(x / 31f, 1.6f);
                var c = Color.Lerp(new Color(1f, 0.78f, 0.35f, 0f), new Color(1f, 0.97f, 0.8f, 1f), a); c.a = a;
                t.SetPixel(x, 0, c); t.SetPixel(x, 1, c);
            }
            t.Apply(false);
            return sprite = Sprite.Create(t, new Rect(0, 0, 32, 2), new Vector2(1f, 0.5f), 1f);
        }

        public static void Attach(BaseBullet b, bool on, GunSpec g)
        {
            var tr = b.GetComponent<CsTracer>();
            if (tr == null)
            {
                if (!on) return;
                tr = b.gameObject.AddComponent<CsTracer>(); Live++;
                var go = new GameObject("csa_tracer"); go.transform.SetParent(b.transform, false);
                tr.sr = go.AddComponent<SpriteRenderer>(); tr.sr.sprite = Streak();
                tr.sr.sortingLayerID = FairySortingLayerConfig.getLayerSystemId(SortingDef.LAYERS.Effect);
                tr.sr.sortingOrder = SortingDef.Effect_Bullet + 1;
                tr.bullet = b;
            }
            tr.On = on;
            if (g != null) { tr.Length = g.Category == "shotgun" ? 26f : g.Category == "zeus" ? 0f : 46f; tr.Width = g.Category == "shotgun" ? 1.1f : 1.6f; }
            tr.sr.enabled = on && tr.Length > 0;
            var na = animOf(b) != null ? animOf(b).transform : null;
            if (na != tr.anim) { tr.anim = na; tr.gameRenderers = null; }
            tr.ShowGameBullet(!on || tr.Length <= 0);
            tr.Follow();
        }

        void ShowGameBullet(bool show)
        {
            if (anim == null) return;
            int n = anim.hierarchyCount;
            if (gameRenderers == null || n != scannedChildren) { gameRenderers = anim.GetComponentsInChildren<Renderer>(true); scannedChildren = n; }
            for (int i = 0; i < gameRenderers.Length; i++) { var r = gameRenderers[i]; if (r != null && r != sr && r.enabled != show) r.enabled = show; }
        }

        void LateUpdate() { var pm = CsPerf.Start(); try { Follow(); } finally { CsPerf.End(CsPerf.H.TracerFrame, pm); } }

        void Follow()
        {
            if (!On || sr == null || bullet == null || Length <= 0) return;
            var a = animOf(bullet);
            if (a == null) return;
            ShowGameBullet(false);   // the particle effect may be created after the shot
            float deg = rot(a);
            sr.transform.localPosition = a.transform.localPosition + new Vector3(offx(a), offy(a), 0f);
            sr.transform.localRotation = Quaternion.Euler(0, 0, deg);
            sr.transform.localScale = new Vector3(Length / 32f, Width / 2f, 1f);
        }
    }

    [HarmonyPatch(typeof(Bullet_Normal), nameof(Bullet_Normal.shootBullet))]
    static class TracerPatch
    {
        public static int Tracers;
        static void Postfix(Bullet_Normal __instance, BulletStruct _bulletStruct) { var pm = CsPerf.Start(); try { PostfixBody(__instance, _bulletStruct); } finally { CsPerf.End(CsPerf.H.Tracer, pm); } }
        // timed for the perf log (CsPerf); the work is in PostfixBody
        static void PostfixBody(Bullet_Normal __instance, BulletStruct _bulletStruct)
        {
            try
            {
                var g = CsData.ByWeapon(_bulletStruct.weaponDef?.ID);
                CsTracer.Attach(__instance, g != null && g.Category != "zeus", g);
                if (g != null) Tracers++;
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] tracer: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(BulletManager), nameof(BulletManager.addBullet))]
    static class PelletSpreadPatch
    {
        public static int Spread;
        static readonly System.Random rnd = new System.Random();
        static void Prefix(ref BulletStruct _bulletStruct) { var pm = CsPerf.Start(); try { PrefixBody(ref _bulletStruct); } finally { CsPerf.End(CsPerf.H.Spread, pm); } }
        // timed for the perf log (CsPerf); the work is in PrefixBody
        static void PrefixBody(ref BulletStruct _bulletStruct)
        {
            if (!MainThread.onMain) return;   // addBullet re-posts itself to the main thread: spread once, there
            var g = CsData.ByWeapon(_bulletStruct.weaponDef?.ID);
            if (g == null || g.Category != "shotgun") return;
            float deg = g.Key == "sawedoff" ? 9f : g.Key == "mag7" ? 5f : 6.5f;   // half-cone
            float u = (float)(rnd.NextDouble() + rnd.NextDouble() - 1.0);          // denser in the middle
            _bulletStruct.shootRadionOffset += u * deg * Mathf.Deg2Rad;
            Spread++;
        }
    }

    [HarmonyPatch(typeof(AimingDevice), nameof(AimingDevice.logic))]
    static class VolleyPatch
    {
        [ThreadStatic] static bool inside;
        public static int Volleys;
        static readonly AccessTools.FieldRef<AimingDevice, int> roundSum = AccessTools.FieldRefAccess<AimingDevice, int>("roundBulletSum");
        static readonly AccessTools.FieldRef<AimingDevice, sbyte> stepOf = AccessTools.FieldRefAccess<AimingDevice, sbyte>("step");

        static void Postfix(AimingDevice __instance, T_WeaponDef ___weaponDef) { var pm = CsPerf.Start(); try { PostfixBody(__instance, ___weaponDef); } finally { CsPerf.End(CsPerf.H.Volley, pm); } }
        // timed for the perf log (CsPerf); the work is in PostfixBody
        static void PostfixBody(AimingDevice __instance, T_WeaponDef ___weaponDef)
        {
            if (inside) return;
            var g = CsData.ByWeapon(___weaponDef?.ID);
            if (g == null || g.Category != "shotgun") return;
            inside = true;
            try
            {
                bool more = false;
                // the rest of this round's pellets now: each logic() call lets one out (the pellet timer is 0)
                for (int i = 0; i < 16 && stepOf(__instance) == 2 && roundSum(__instance) > 0; i++) { __instance.logic(); more = true; }
                if (more) Volleys++;
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] volley: " + e.Message); }
            finally { inside = false; }
        }
    }
}
