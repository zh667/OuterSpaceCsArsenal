using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CsArsenal.Kit
{
    // Debug channel for the isolated test copy only (python tools/run_isolated.py --tests debug): after the save has
    // loaded, commands are read from <game>/csdebug/in/<id>.json and answered in <game>/csdebug/out/<id>.json
    // (tools/dbg.py sends them). Commands (field "cmd"):
    //   tree   {root?: GameObject name/path, depth=3, components=true, filter?: substring}   scene hierarchy
    //   get    {target, path?, depth=2}            read a value (fields/properties/indexers along a dotted path)
    //   set    {target, path, value}               write a field/property (value converted to its type)
    //   call   {target, path?, method, args?: []}  call a method on the object at target.path
    //   perf   {seconds=5}                         frame times, GC collections, Mono/Unity memory
    //   shot   {label, aim?: target of a role, scale?}  screenshot, optionally centred on a colonist/monster
    //                                              (scale = camera scale; default keeps the current one)
    //   clock  {speed}                             game speed (0 pauses)
    //   load   {dll}                               load a compiled snippet: Harmony patches in it are applied,
    //                                              a static Snippet.Run() is called and its result returned
    //   test   {name}                              start a self-test (fight, fight:<gun>, hand) in the background,
    //                                              so it can be watched with get/perf/shot while it runs
    //   checks {}                                  the test checks recorded so far
    //   wait   {seconds}                           real-time wait
    //   quit                                       stop serving and let the test end
    // Targets: game (FWGameData), map (MapManager), user (UserBean), hero:<name|index>, monster:<index>,
    //   monsters, heroes, static:<Type>, def:<DefType>:<ID>, go:<GameObject path or name>, last (previous result).
    // A path segment can be a member name, an index [3], a dictionary key ["key"], or, on a GameObject, a component
    // type in angle brackets: <HeroAnim>.playIndex
    public static class DebugChannel
    {
        public class Host
        {
            public Action<Action> Locked;               // run under the game's locks
            public Func<string, IEnumerator> Shot;      // screenshot with a label
            public Action<float> SetClock;
            public Func<string> GameDir;
            public Action<string> Log;
            public Func<string, string> StartTest;      // start a named test in the background (null = started)
            public Func<object> Checks;                 // test results so far
        }

        static object last;

        public static IEnumerator Serve(Host host, float idleLimit = 1800f)
        {
            string root = Path.Combine(host.GameDir(), "csdebug"), inDir = Path.Combine(root, "in"), outDir = Path.Combine(root, "out");
            Directory.CreateDirectory(inDir); Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(inDir).Concat(Directory.GetFiles(outDir))) try { File.Delete(f); } catch { }   // from an earlier run
            File.WriteAllText(Path.Combine(root, "ready.txt"), DateTime.Now.ToString("s"));
            host.Log("debug channel ready at " + root);
            float idle = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - idle < idleLimit)
            {
                var files = Directory.GetFiles(inDir, "*.json").OrderBy(f => File.GetCreationTimeUtc(f)).ToList();
                if (files.Count == 0) { yield return new WaitForSecondsRealtime(0.2f); continue; }
                idle = Time.realtimeSinceStartup;
                foreach (var f in files)
                {
                    string id = Path.GetFileNameWithoutExtension(f);
                    JObject cmd = null; var res = new Dictionary<string, object> { ["id"] = id };
                    try { cmd = JObject.Parse(File.ReadAllText(f)); } catch (Exception e) { res["error"] = "bad json: " + e.Message; }
                    try { File.Delete(f); } catch { }
                    bool quit = false;
                    if (cmd != null)
                    {
                        string c = (string)cmd["cmd"] ?? "";
                        res["cmd"] = c;
                        float t0 = Time.realtimeSinceStartup;
                        if (c == "quit") quit = true;
                        else if (c == "wait") yield return new WaitForSecondsRealtime((float?)cmd["seconds"] ?? 1f);
                        else if (c == "perf") yield return Perf(cmd, res);
                        else if (c == "shot")
                        {
                            string label = "dbg-" + ((string)cmd["label"] ?? id);
                            if (cmd["aim"] != null)
                            {
                                host.Locked(() =>
                                {
                                    try
                                    {
                                        var o = Resolve((string)cmd["aim"], null) as SpriteObject;
                                        var cam = CamearControl.getInstance(); cam.enableMove(); cam.endFollow();
                                        if (o != null) cam.setTo(o.transform.localPosition.x, o.transform.localPosition.y, (float?)cmd["scale"] ?? cam.getScale());
                                    }
                                    catch (Exception e) { res["aimError"] = e.Message; }
                                });
                                yield return new WaitForSecondsRealtime(0.6f);
                            }
                            yield return host.Shot(label);
                            res["file"] = Path.Combine(host.GameDir(), "selftest-" + label + ".png");
                        }
                        else if (c == "test") { var err = host.StartTest((string)cmd["name"] ?? ""); if (err != null) res["error"] = err; else res["ok"] = true; }
                        else if (c == "checks") { try { res["value"] = host.Checks(); } catch (Exception e) { res["error"] = e.Message; } }
                        else if (c == "clock") { host.SetClock((float?)cmd["speed"] ?? 1f); res["ok"] = true; }
                        else host.Locked(() => Run(c, cmd, res));
                        res["ms"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000, 1);
                    }
                    try { File.WriteAllText(Path.Combine(outDir, id + ".json"), JsonConvert.SerializeObject(res, Formatting.Indented)); } catch { }
                    if (quit) { host.Log("debug channel: quit"); try { File.Delete(Path.Combine(root, "ready.txt")); } catch { } yield break; }
                }
            }
            host.Log("debug channel: idle " + idleLimit + " s, stopping");
            try { File.Delete(Path.Combine(root, "ready.txt")); } catch { }
        }

        static void Run(string c, JObject cmd, Dictionary<string, object> res)
        {
            try
            {
                switch (c)
                {
                    case "tree": res["tree"] = Tree(cmd); break;
                    case "get":
                        {
                            var o = Resolve((string)cmd["target"], (string)cmd["path"]);
                            last = o; res["type"] = o?.GetType().FullName; res["value"] = Describe(o, (int?)cmd["depth"] ?? 2);
                            break;
                        }
                    case "set":
                        {
                            string path = (string)cmd["path"];
                            int dot = path.LastIndexOf('.');
                            var owner = Resolve((string)cmd["target"], dot < 0 ? null : path.Substring(0, dot));
                            string member = dot < 0 ? path : path.Substring(dot + 1);
                            res["before"] = Describe(Member(owner, member), 0);
                            SetMember(owner, member, cmd["value"]);
                            res["after"] = Describe(Member(owner, member), 0);
                            break;
                        }
                    case "call":
                        {
                            var o = Resolve((string)cmd["target"], (string)cmd["path"]);
                            var args = (cmd["args"] as JArray)?.ToList() ?? new List<JToken>();
                            object r = Call(o, (string)cmd["method"], args);
                            last = r; res["type"] = r?.GetType().FullName; res["value"] = Describe(r, (int?)cmd["depth"] ?? 2);
                            break;
                        }
                    case "load": res["value"] = Load((string)cmd["dll"]); break;
                    default: res["error"] = "unknown cmd " + c; break;
                }
            }
            catch (Exception e) { res["error"] = (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e).ToString(); }
        }

        // ---- targets and paths ----
        public static object Resolve(string target, string path)
        {
            object o = Root(target);
            if (string.IsNullOrEmpty(path)) return o;
            foreach (var seg in Split(path)) o = Step(o, seg);
            return o;
        }

        static object Root(string t)
        {
            if (string.IsNullOrEmpty(t) || t == "last") return last;
            var mm = MapManager.getInstance();
            if (t == "game") return UserBean.getInstance().gameData;
            if (t == "user") return UserBean.getInstance();
            if (t == "map") return mm;
            if (t == "heroes") return mm.heroManager.getAllianceFaction(1).getRoles();
            if (t == "monsters") return mm.monsterManager.getMonsters();
            if (t.StartsWith("hero:"))
            {
                var key = t.Substring(5); var list = mm.heroManager.getAllianceFaction(1).getRoles();
                if (int.TryParse(key, out int i)) return list[i];
                return list.FirstOrDefault(h => h?.heroBean?.Name == key) ?? throw new Exception("no colonist " + key);
            }
            if (t.StartsWith("monster:")) return mm.monsterManager.getMonsters()[int.Parse(t.Substring(8))];
            if (t.StartsWith("static:")) return new StaticRef(FindType(t.Substring(7)));
            if (t.StartsWith("def:"))
            {
                var p = t.Split(new[] { ':' }, 3); var type = FindType(p[1]);
                var impl = typeof(ImplDef<>).MakeGenericType(type);
                return impl.GetMethod("getDataID", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { p[2] });
            }
            if (t.StartsWith("go:"))
            {
                var name = t.Substring(3);
                var go = GameObject.Find(name) ?? Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g.name == name && g.scene.IsValid());
                return go ?? throw new Exception("no GameObject " + name);
            }
            throw new Exception("unknown target " + t);
        }

        class StaticRef { public Type T; public StaticRef(Type t) { T = t; } public override string ToString() => "static " + T.FullName; }

        public static Type FindType(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType(name) ?? a.GetTypes().FirstOrDefault(x => x.Name == name); } catch { }
                if (t != null) return t;
            }
            throw new Exception("no type " + name);
        }

        static IEnumerable<string> Split(string path)
        {
            int i = 0; var cur = new System.Text.StringBuilder();
            while (i < path.Length)
            {
                char ch = path[i];
                if (ch == '.') { if (cur.Length > 0) { yield return cur.ToString(); cur.Clear(); } i++; continue; }
                if (ch == '[' || ch == '<')
                {
                    if (cur.Length > 0) { yield return cur.ToString(); cur.Clear(); }
                    char close = ch == '[' ? ']' : '>'; int j = path.IndexOf(close, i);
                    yield return path.Substring(i, j - i + 1); i = j + 1; continue;
                }
                cur.Append(ch); i++;
            }
            if (cur.Length > 0) yield return cur.ToString();
        }

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        static BindingFlags FlagsFor(object o) => o is StaticRef ? Statics : All;

        static object Step(object o, string seg)
        {
            if (o == null) throw new Exception("null before " + seg);
            if (seg.StartsWith("<"))
            {
                var tn = seg.Substring(1, seg.Length - 2); var go = o as GameObject ?? (o as Component)?.gameObject;
                var comp = go.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && (c.GetType().Name == tn || c.GetType().FullName == tn));
                return comp ?? throw new Exception("no component " + tn + " on " + go.name);
            }
            if (seg.StartsWith("["))
            {
                var key = seg.Substring(1, seg.Length - 2).Trim();
                if (o is IList l) return l[int.Parse(key)];
                if (o is IDictionary d) { var k = key.Trim('"'); foreach (DictionaryEntry e in d) if (e.Key?.ToString() == k) return e.Value; throw new Exception("no key " + k); }
                if (o is IEnumerable en) return en.Cast<object>().ElementAt(int.Parse(key));
                throw new Exception(o.GetType().Name + " is not indexable");
            }
            return Member(o, seg);
        }

        static object Member(object o, string name)
        {
            Type t = o is StaticRef s ? s.T : o.GetType(); object inst = o is StaticRef ? null : o;
            for (var tt = t; tt != null; tt = tt.BaseType)
            {
                var f = tt.GetField(name, FlagsFor(o)); if (f != null) return f.GetValue(inst);
                var p = tt.GetProperty(name, FlagsFor(o)); if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(inst);
            }
            var m = t.GetMethod(name, FlagsFor(o), null, Type.EmptyTypes, null);   // a parameterless method as a path step
            if (m != null) return m.Invoke(inst, null);
            throw new Exception("no member " + name + " on " + t.Name);
        }

        static void SetMember(object o, string name, JToken value)
        {
            Type t = o is StaticRef s ? s.T : o.GetType(); object inst = o is StaticRef ? null : o;
            for (var tt = t; tt != null; tt = tt.BaseType)
            {
                var f = tt.GetField(name, FlagsFor(o)); if (f != null) { f.SetValue(inst, value.ToObject(f.FieldType)); return; }
                var p = tt.GetProperty(name, FlagsFor(o)); if (p != null && p.CanWrite) { p.SetValue(inst, value.ToObject(p.PropertyType)); return; }
            }
            throw new Exception("no writable member " + name + " on " + t.Name);
        }

        static object Call(object o, string name, List<JToken> args)
        {
            Type t = o is StaticRef s ? s.T : o.GetType(); object inst = o is StaticRef ? null : o;
            foreach (var m in t.GetMethods(FlagsFor(o)).Where(m => m.Name == name && m.GetParameters().Length == args.Count))
            {
                var ps = m.GetParameters(); var vals = new object[ps.Length]; bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    try { vals[i] = args[i].Type == JTokenType.String && ((string)args[i]).StartsWith("@") ? Resolve(((string)args[i]).Substring(1), null) : args[i].ToObject(ps[i].ParameterType); }
                    catch { ok = false; }
                }
                if (ok) return m.Invoke(inst, vals);
            }
            throw new Exception("no method " + name + " with " + args.Count + " matching arguments on " + t.Name);
        }

        // ---- output ----
        public static object Describe(object o, int depth)
        {
            if (o == null) return null;
            var t = o.GetType();
            if (o is string || t.IsPrimitive || o is decimal) return o;
            if (t.IsEnum) return o.ToString();
            if (o is Vector2 || o is Vector3 || o is Color || o is Rect || o is Quaternion) return o.ToString();
            if (o is UnityEngine.Object uo && uo == null) return "(destroyed " + t.Name + ")";
            if (o is GameObject g) { var j = new Dictionary<string, object> { ["name"] = g.name, ["active"] = g.activeInHierarchy, ["components"] = g.GetComponents<Component>().Where(c => c != null).Select(c => (object)c.GetType().Name).ToList() }; return j; }
            if (o is IDictionary d)
            {
                var j = new Dictionary<string, object> { ["$count"] = d.Count }; int n = 0;
                foreach (DictionaryEntry e in d) { if (n++ >= 30) break; j[e.Key?.ToString() ?? "null"] = depth > 0 ? Describe(e.Value, depth - 1) : (object)(e.Value?.ToString()); }
                return j;
            }
            if (o is IEnumerable en && !(o is UnityEngine.Object))
            {
                var a = new List<object>(); int n = 0, total = 0;
                foreach (var x in en) { total++; if (n < 30) { a.Add(depth > 0 ? Describe(x, depth - 1) : (object)(x?.ToString())); n++; } }
                return new Dictionary<string, object> { ["$count"] = total, ["items"] = a };
            }
            if (depth <= 0) return o.ToString() + " (" + t.Name + ")";
            var obj = new Dictionary<string, object> { ["$type"] = t.Name };
            int k = 0;
            for (var tt = t; tt != null && tt != typeof(object) && tt != typeof(MonoBehaviour) && tt != typeof(Component); tt = tt.BaseType)
                foreach (var f in tt.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (k++ >= 80 || f.Name.Contains("<")) continue;
                    try { obj[f.Name] = Describe(f.GetValue(o), depth - 1); } catch (Exception e) { obj[f.Name] = "!" + e.GetType().Name; }
                }
            return obj;
        }

        static object Tree(JObject cmd)
        {
            int depth = (int?)cmd["depth"] ?? 3; bool comps = (bool?)cmd["components"] ?? true; string filter = (string)cmd["filter"];
            string rootName = (string)cmd["root"];
            IEnumerable<GameObject> roots;
            if (rootName != null) roots = new[] { (GameObject)Root("go:" + rootName) };
            else
            {
                var l = new List<GameObject>(); for (int i = 0; i < SceneManager.sceneCount; i++) l.AddRange(SceneManager.GetSceneAt(i).GetRootGameObjects());
                // objects kept with DontDestroyOnLoad (mods' own objects among them) are not in the loaded scenes
                l.AddRange(Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.transform.parent == null && g.scene.IsValid() && g.scene.name == "DontDestroyOnLoad"));
                roots = l.Distinct();
            }
            var a = new List<object>(); int budget = 4000;
            foreach (var r in roots) { var n = Node(r.transform, depth, comps, filter, ref budget); if (n != null) a.Add(n); }
            return a;
        }

        static Dictionary<string, object> Node(Transform t, int depth, bool comps, string filter, ref int budget)
        {
            if (budget-- <= 0) return null;
            var kids = new List<object>();
            if (depth > 0) foreach (Transform c in t) { var n = Node(c, depth - 1, comps, filter, ref budget); if (n != null) kids.Add(n); }
            bool match = filter == null || t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!match && kids.Count == 0) return null;
            var j = new Dictionary<string, object> { ["n"] = t.name + (t.gameObject.activeInHierarchy ? "" : " (inactive)"), ["kids"] = t.childCount };
            if (comps) j["c"] = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            if (kids.Count > 0) j["k"] = kids;
            return j;
        }

        static IEnumerator Perf(JObject cmd, Dictionary<string, object> res)
        {
            float secs = (float?)cmd["seconds"] ?? 5f; var ft = new List<float>();
            int gc0 = GC.CollectionCount(0); long mono0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < secs) { yield return null; ft.Add(Time.unscaledDeltaTime * 1000f); }
            ft.Sort();
            res["frames"] = ft.Count; res["fps"] = Math.Round(ft.Count / secs, 1);
            res["ms_avg"] = Math.Round(ft.Average(), 2); res["ms_p95"] = Math.Round(ft[(int)(ft.Count * 0.95)], 2); res["ms_max"] = Math.Round(ft.Last(), 2);
            res["gc_gen0"] = GC.CollectionCount(0) - gc0;
            res["mono_used_mb"] = Math.Round(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0, 1);
            res["mono_growth_mb"] = Math.Round((UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - mono0) / 1048576.0, 2);
            res["unity_allocated_mb"] = Math.Round(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0, 1);
        }

        // a snippet DLL (tools/dbg.py snippet): Harmony patches in it are applied, Snippet.Run() is called
        static bool resolverOn;
        static object Load(string path)
        {
            if (!resolverOn)
            {   // the mod DLLs were loaded from bytes, so a snippet referencing them by name needs this to find them
                resolverOn = true;
                AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
                {
                    var n = new AssemblyName(e.Name).Name;
                    return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == n);
                };
            }
            var asm = Assembly.Load(File.ReadAllBytes(path));
            var res = new Dictionary<string, object> { ["assembly"] = asm.FullName };
            var patchTypes = asm.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length > 0).ToList();
            if (patchTypes.Count > 0)
            {
                var h = new HarmonyLib.Harmony("dbg." + asm.GetName().Name);
                h.PatchAll(asm); res["patched"] = h.GetPatchedMethods().Select(m => (object)(m.DeclaringType?.Name + "." + m.Name)).ToList();
            }
            var run = asm.GetTypes().FirstOrDefault(t => t.Name == "Snippet")?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            if (run != null) { var r = run.Invoke(null, null); last = r; res["result"] = Describe(r, 3); }
            return res;
        }
    }
}
