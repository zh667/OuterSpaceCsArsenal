using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CsArsenal
{
    // The CS tech nodes show a CS2 HUD weapon silhouette instead of the icon copied with the template row (user 11-10).
    // The game draws tech icons from the "Icon_Technology" atlas: white glyphs on opaque black, shader XXY/UI_MainIco,
    // which uses the brightness as the mask and tints it by the node's state (_Color, _IsResearching). Our pictures
    // (Resources/Textures/tech/<tech id>.png, tools/assets/make_icons.py) have the same format, so the icon keeps the
    // game's material and behaviour: only that icon's own copy of the material gets our texture, and its mesh becomes
    // one quad over the cell the game's frame would have covered.
    // Drawn in three places: the tree (TechonologyItem), search results and the pinned panel.
    public static class CsTechIcons
    {
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly ConditionalWeakTable<UISpriteCatPlayer, Texture2D> marked = new ConditionalWeakTable<UISpriteCatPlayer, Texture2D>();
        static readonly ConditionalWeakTable<UISpriteCatPlayer, Material> ownMaterial = new ConditionalWeakTable<UISpriteCatPlayer, Material>();
        public static readonly string[] TechIds = { CsData.TechId, CsData.Tech2Id, CsData.Tech3Id, CsData.Tech4Id };

        public static string PathOf(string techId) => CsDefLoader.ResourcesDir == null ? null : Path.Combine(CsDefLoader.ResourcesDir, "Textures", "tech", techId + ".png");

        static Texture2D Get(string techId)
        {
            if (textures.TryGetValue(techId, out var t)) return t;
            t = null;
            try
            {
                var p = PathOf(techId);
                if (p != null && File.Exists(p))
                {
                    t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "csa_tech_" + techId };
                    ImageConversion.LoadImage(t, File.ReadAllBytes(p), true);
                    t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Trilinear;
                }
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] tech icon " + techId + ": " + e.Message); t = null; }
            textures[techId] = t;
            return t;
        }

        public static void Apply(UISpriteCatPlayer ip, TechnologyDef def)
        {
            try
            {
                if (ip == null) return;
                marked.Remove(ip);   // the same row object can be reused for a game tech (search results, pins)
                if (def == null || Array.IndexOf(TechIds, def.ID) < 0 || ip.player == null) return;
                var tex = Get(def.ID);
                var mats = ip.player.materials;
                if (tex == null || mats == null || mats.Length == 0 || mats[0] == null) return;
                if (mats[0].mainTexture != tex)
                {
                    var m = new Material(mats[0]) { mainTexture = tex, name = "csa_tech_icon" };
                    if (ownMaterial.TryGetValue(ip, out var old) && old != null && old != mats[0]) UnityEngine.Object.Destroy(old);
                    ownMaterial.Remove(ip); ownMaterial.Add(ip, m);
                    mats[0] = m;
                }
                marked.Add(ip, tex);
                ip.SetMaterialDirty(); ip.SetVerticesDirty();
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] tech icon: " + e.Message); }
        }

        public static bool Applied(UISpriteCatPlayer ip) => ip != null && marked.TryGetValue(ip, out var t) && t != null && ip.player?.materials?[0]?.mainTexture == t;

        internal static void Mesh(UISpriteCatPlayer ip, VertexHelper vh)
        {
            if (!marked.TryGetValue(ip, out var tex) || tex == null || vh.currentVertCount == 0) return;
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            Color32 col = Color.white; var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); mn = Vector2.Min(mn, v.position); mx = Vector2.Max(mx, v.position); if (i == 0) col = v.color; }
            Vector2 c = (mn + mx) / 2; float side = Mathf.Max(mx.x - mn.x, mx.y - mn.y);   // our pictures are square
            vh.Clear();
            Vector2[] uv = { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            for (int i = 0; i < 4; i++)
            {
                var q = UIVertex.simpleVert;
                q.position = new Vector2(c.x + (uv[i].x - 0.5f) * side, c.y + (uv[i].y - 0.5f) * side);
                q.color = col; q.uv0 = uv[i]; q.uv1 = uv[i];
                vh.AddVert(q);
            }
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }
    }

    [HarmonyPatch(typeof(TechonologyItem), nameof(TechonologyItem.setTechonology))]
    static class TechTreeIconPatch
    {
        static void Postfix(TechnologyDef techDef, UISpriteCatPlayer ___iconPlayer) => CsTechIcons.Apply(___iconPlayer, techDef);
    }

    [HarmonyPatch(typeof(TechonologySearchResultItem), "refreshIcon")]
    static class TechSearchIconPatch
    {
        static void Postfix(TechnologyDef def, UISpriteCatPlayer ___iconPlayer) => CsTechIcons.Apply(___iconPlayer, def);
    }

    [HarmonyPatch(typeof(TechonologyPinPanel), "paintTechIcon")]
    static class TechPinIconPatch
    {
        static void Postfix(UISpriteCatPlayer iconPlayer, TechnologyDef def) => CsTechIcons.Apply(iconPlayer, def);
    }

    [HarmonyPatch(typeof(UISpriteCatPlayer), "OnPopulateMesh")]
    static class TechIconMeshPatch
    {
        static void Postfix(UISpriteCatPlayer __instance, VertexHelper vh) { var pm = CsPerf.Start(); try { CsTechIcons.Mesh(__instance, vh); } finally { CsPerf.End(CsPerf.H.Mesh, pm); } }
    }
}
