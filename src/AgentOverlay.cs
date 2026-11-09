using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CsArsenal
{
    // Route 2 (test only): hides a colonist's own Spine drawing and draws frames rendered from the CS2 agent model in
    // its place, like the Tactical Doll mod does. Idle frames when standing, walk frames when moving, mirrored by
    // the walking direction. The game re-enables renderers, so they are hidden again every frame.
    public class AgentOverlay : MonoBehaviour
    {
        public BaseHero hero;
        Sprite[] idle, walk;
        SpriteRenderer sr;
        Vector3 last; float t; bool flip; float footX, footY;
        public string Note = "";

        static Sprite[] Load(string dir, string prefix, float ppu)
        {
            var list = new List<Sprite>();
            for (int i = 0; i < 32; i++)
            {
                var p = Path.Combine(dir, prefix + "_" + i + ".png");
                if (!File.Exists(p)) break;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                ImageConversion.LoadImage(tex, File.ReadAllBytes(p));
                tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
                list.Add(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), ppu));
            }
            return list.ToArray();
        }

        public static AgentOverlay Attach(BaseHero h, string agent)
        {
            var dir = Path.Combine(CsDefLoader.ResourcesDir, "Textures", "agents", "route2");
            Renderer main = null; float best = 0;
            foreach (var r in h.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                float s = r.bounds.size.y; if (s > best) { best = s; main = r; }
            }
            if (main == null) return null;
            var b = main.bounds; var pos = h.transform.position;
            // frames: character height = render height / 1.15 (bl_agent.py ortho margin); match the colonist's height
            var probe = Path.Combine(dir, agent + "_idle_0.png");
            if (!File.Exists(probe)) return null;
            var t0 = new Texture2D(2, 2); ImageConversion.LoadImage(t0, File.ReadAllBytes(probe));
            // the colonist bounds include the big helmet / hair: the body is ~85 % of them
            float ppu = (t0.height / 1.15f) / Mathf.Max(0.01f, b.size.y * 0.85f);
            Destroy(t0);
            var go = new GameObject("CsArsenal_Route2_" + agent);
            go.layer = h.gameObject.layer;
            var o = go.AddComponent<AgentOverlay>();
            o.hero = h; o.idle = Load(dir, agent + "_idle", ppu); o.walk = Load(dir, agent + "_walk", ppu);
            if (o.walk.Length == 0) o.walk = o.idle;
            o.sr = go.AddComponent<SpriteRenderer>();
            // the colonists' Spine material does not draw plain sprites (they come out almost transparent) and the
            // URP sprite shaders are not found by name in the built game: plain sprite shader, tinted towards the
            // map's cold ambient light so the figure does not glow next to the lit colonists
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) o.sr.sharedMaterial = new Material(sh);
            o.sr.color = new Color(0.78f, 0.82f, 0.92f, 1f);
            o.sr.sortingLayerID = main.sortingLayerID; o.sr.sortingOrder = main.sortingOrder;
            o.footX = b.center.x - pos.x; o.footY = b.min.y - pos.y;
            o.last = pos; o.sr.sprite = o.idle.Length > 0 ? o.idle[0] : null;
            o.Note = (sh != null ? sh.name : "no shader") + " " + agent + " frames idle " + o.idle.Length + " walk " + o.walk.Length + " ppu " + ppu.ToString("0.0") + " heroH " + b.size.y.ToString("0.00");
            return o;
        }

        void LateUpdate()
        {
            try
            {
                if (hero == null) { Destroy(gameObject); return; }
                foreach (var r in hero.GetComponentsInChildren<Renderer>(true))
                    if (r != sr && r.enabled) r.enabled = false;
                var pos = hero.transform.position;
                bool moving = (pos - last).sqrMagnitude > 1e-6f;
                if (moving && Mathf.Abs(pos.x - last.x) > 1e-4f) flip = pos.x < last.x;
                var frames = moving ? walk : idle;
                t += Time.unscaledDeltaTime;
                if (frames.Length > 0) sr.sprite = frames[(int)(t * (moving ? 10f : 5f)) % frames.Length];
                sr.flipX = flip;
                transform.position = new Vector3(pos.x + footX, pos.y + footY, pos.z - 0.1f);
                last = pos;
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] overlay: " + e.Message); }
        }
    }
}
