using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace CsArsenal
{
    // CS2 original gun sounds. The game plays weapon sounds as FMOD events (MusicListIDDef -> FMOD path), which a mod
    // cannot add to; our weapon rows carry ids "csa_<gun>_fire" / "csa_<gun>_reload" instead, SoundStruct.playAtk and
    // playReload are intercepted for those ids, and the clips (Resources/Sounds, tools/assets/make_sounds.py) are played
    // through Unity audio: the game's sound volume, louder near the middle of the screen, silent far off it.
    public static class CsSound
    {
        public const string Prefix = "csa_";
        static Dictionary<string, List<AudioClip>> clips;
        static readonly System.Random rnd = new System.Random();
        static AudioSource[] pool; static int next;
        public static int Played, Missing;
        public static string Error;

        public class Entry { public List<string> fire = new List<string>(); public string reload; }

        public static Dictionary<string, Entry> Index()
        {
            try
            {
                var p = Path.Combine(CsDefLoader.ResourcesDir ?? "", "Sounds", "sounds.json");
                return File.Exists(p) ? JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(p)) : null;
            }
            catch (Exception e) { Error = e.Message; return null; }
        }

        // main thread, once (ModBehaviour.Start): ~60 small wav files
        public static void Load(GameObject host)
        {
            if (clips != null) return;
            clips = new Dictionary<string, List<AudioClip>>();
            var idx = Index(); if (idx == null) return;
            string dir = Path.Combine(CsDefLoader.ResourcesDir, "Sounds");
            AudioClip Clip(string f)
            {
                var c = AssetsLoader.loadAudioClipInRequest("file:///" + Path.Combine(dir, f).Replace('\\', '/'));
                if (c == null) Missing++;
                return c;
            }
            foreach (var kv in idx)
            {
                var fire = new List<AudioClip>(); foreach (var f in kv.Value.fire) { var c = Clip(f); if (c != null) fire.Add(c); }
                if (fire.Count > 0) clips[Prefix + kv.Key + "_fire"] = fire;
                if (kv.Value.reload != null) { var c = Clip(kv.Value.reload); if (c != null) clips[Prefix + kv.Key + "_reload"] = new List<AudioClip> { c }; }
            }
            pool = new AudioSource[16];
            for (int i = 0; i < pool.Length; i++) { pool[i] = host.AddComponent<AudioSource>(); pool[i].playOnAwake = false; pool[i].spatialBlend = 0f; }
            Debug.Log("[CsArsenal] sounds: " + clips.Count + " ids, missing files " + Missing);
        }

        public static bool Has(string id) => id != null && id.StartsWith(Prefix);

        public static void Play(string id, Vector3 mapPos)
        {
            if (MainThread.onMain) PlayNow(id, mapPos);
            else MainThread.post(() => PlayNow(id, mapPos));
        }

        static float gameVolume = -1f; static float volumeAt;
        static float GameVolume()
        {
            if (Time.realtimeSinceStartup - volumeAt > 2f || gameVolume < 0)
            {
                volumeAt = Time.realtimeSinceStartup;
                // the player's own settings (设置 → 声音): master and sound switches, master x sound volume. AudioManager's
                // private soundVolume stayed 1 with both switched off, so the CS shots kept playing in a muted game.
                try { gameVolume = UserSaver.masterEnable && UserSaver.soundEnable ? Mathf.Clamp01(UserSaver.masterVolume) * Mathf.Clamp01(UserSaver.soundVolume) : 0f; }
                catch { gameVolume = 1f; }
            }
            return gameVolume;
        }

        static void PlayNow(string id, Vector3 mapPos)
        {
            try
            {
                if (pool == null || clips == null || !clips.TryGetValue(id, out var list) || list.Count == 0) return;
                if (ScriptInterface.isStopSound) return;
                // distance from the middle of the screen, in screens: full volume on screen, silent 1.5 screens away
                float att = 1f, pan = 0f;
                try
                {
                    Vector2 sp = CamearControl.getInstance().mapPoint2ScreenPoint(new Vector2(mapPos.x, mapPos.y));
                    float dx = (sp.x - Screen.width * 0.5f) / Screen.width, dy = (sp.y - Screen.height * 0.5f) / Screen.height;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    att = d <= 0.6f ? 1f : Mathf.Clamp01(1f - (d - 0.6f) / 0.9f);
                    pan = Mathf.Clamp(dx * 1.2f, -0.6f, 0.6f);
                }
                catch { }
                float vol = GameVolume() * att * 0.55f;
                if (vol <= 0.01f) return;
                var src = pool[next = (next + 1) % pool.Length];
                src.panStereo = pan; src.pitch = 0.97f + (float)rnd.NextDouble() * 0.06f;
                src.PlayOneShot(list[rnd.Next(list.Count)], vol);
                Played++;
            }
            catch (Exception e) { Error = e.Message; }
        }
    }

    [HarmonyPatch(typeof(SoundStruct), nameof(SoundStruct.playAtk))]
    static class FireSoundPatch
    {
        static bool Prefix(SoundStruct __instance, Vector3 localPosition)
        {
            if (!CsSound.Has(__instance.ATKMusic)) return true;
            var pm = CsPerf.Start(); try { CsSound.Play(__instance.ATKMusic, localPosition); } finally { CsPerf.End(CsPerf.H.Sound, pm); }
            return false;
        }
    }

    [HarmonyPatch(typeof(SoundStruct), nameof(SoundStruct.playReload))]
    static class ReloadSoundPatch
    {
        static bool Prefix(SoundStruct __instance, Vector3 localPosition)
        {
            if (!CsSound.Has(__instance.ReloadMusic)) return true;
            var pm = CsPerf.Start(); try { CsSound.Play(__instance.ReloadMusic, localPosition); } finally { CsPerf.End(CsPerf.H.Sound, pm); }
            return false;
        }
    }
}
