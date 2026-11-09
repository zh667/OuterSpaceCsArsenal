using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CsArsenal
{
    // Player settings: <LocalLow>/CatStudio/Outer Space/CsArsenal/config.json (written with the defaults on first run).
    public class CsConfig
    {
        public float killRewardMultiplier = 1f;   // 0 turns kill rewards off
        public string note = "killRewardMultiplier: 击杀奖励倍率（1 = 默认，0 = 关闭）。改完重启游戏生效。";

        static CsConfig cur;
        public static CsConfig Get()
        {
            if (cur != null) return cur;
            cur = new CsConfig();
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "CsArsenal"); Directory.CreateDirectory(dir);
                var p = Path.Combine(dir, "config.json");
                if (File.Exists(p)) cur = JsonConvert.DeserializeObject<CsConfig>(File.ReadAllText(p)) ?? cur;
                File.WriteAllText(p, JsonConvert.SerializeObject(cur, Formatting.Indented));   // adds new keys with defaults
            }
            catch (Exception e) { Debug.LogWarning("[CsArsenal] config: " + e.Message); }
            cur.killRewardMultiplier = Mathf.Clamp(cur.killRewardMultiplier, 0f, 20f);
            return cur;
        }
    }
}
