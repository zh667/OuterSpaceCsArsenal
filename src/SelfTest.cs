using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using CsArsenal.Kit;

namespace CsArsenal
{
    // Checks in the isolated game (python tools/run_isolated.py --tests <name>):
    //   defs   rows, recipes, bench, tech node and the Harmony patches
    //   fight  one drafted colonist, each gun in turn against three spawned monsters: hits, headshots, Zeus executions,
    //          kills and the credits gained (kill rewards)
    //   hand   each gun in a drafted colonist's hands (screenshots)
    //   look   the CS tech nodes in the game's tech tree: CS2 icon on every node (screenshots selftest-tech-<id>.png)
    //   perf   load test for the perf log (CsPerf): 12 drafted colonists with 12 different CS guns against monsters that
    //          do not die, at 1x and 3x speed, then the same colonists with the game's own template guns as the control;
    //          one CsArsenal-perf line per phase (not part of "all")
    //   all    defs + fight + hand + look
    //   debug  serve the debug channel (Kit/DebugChannel.cs; tools/dbg.py) until "quit" or 30 idle minutes
    public class SelfTest : SelfTestBase
    {
        protected override string ModName => "CsArsenal";
        protected override string ModVersion => ModBehaviour.Version;

        readonly List<BaseHero> heroes = new List<BaseHero>();
        int lastHits = -1;   // hits by the gun in the last fight

        // the camera scale when the tests started: every close-up is set from it (scaling the current value compounded
        // over 21 fights and ended up a few pixels wide)
        float camScale0 = -1f;
        float BaseScale()
        {
            // the game starts at 1; a debug session may have zoomed in before the test ran
            if (camScale0 <= 0f) Locked(() => camScale0 = Mathf.Max(0.8f, CamearControl.getInstance().getScale()));
            return camScale0;
        }
        string monsterId;

        protected override IEnumerator RunTests()
        {
            Run(CheckDefs);
            if (req.tests == "defs") yield break;

            SetClock(0f);
            var info = new Check { name = "colonists", info = true, passed = true }; result.checks.Add(info);
            for (int attempt = 0; attempt < 10 && heroes.Count == 0; attempt++)
            {
                Locked(() =>
                {
                    var f = MapManager.getInstance()?.heroManager?.getAllianceFaction(1);
                    if (f != null) foreach (var h in f.getRoles())
                            if (h != null && h.heroBean != null && !h.heroBean.isDead() && h.gameObject.activeInHierarchy && MapTools.isCellInRealMapRect(h.cellX, h.cellY)) heroes.Add(h);
                });
                if (heroes.Count == 0) yield return new WaitForSecondsRealtime(2f);
            }
            info.summary = heroes.Count + " on this map";
            if (heroes.Count == 0) { info.passed = false; yield break; }
            CloseDialogs();
            Locked(() => { try { UserBean.getInstance().curStarMap.addTimeMM(8 * 60000); } catch { } });   // midday for the shots
            SetClock(1f); yield return new WaitForSecondsRealtime(1.5f); SetClock(0f);
            CloseDialogs();

            if (req.tests == "debug")
            {
                var host = new DebugChannel.Host
                {
                    Locked = Locked, Shot = Shot, SetClock = SetClock, GameDir = () => GameDir, Log = Log, Checks = () => result.checks,
                    StartTest = name =>
                    {
                        IEnumerator e = name.StartsWith("fight") ? FightAll() : name == "hand" ? Hands() : name == "look" ? TechLook() : null;
                        if (e == null) return "unknown test " + name;
                        req.tests = name; StartCoroutine(Guard(e, name.Split(':')[0])); return null;
                    }
                };
                yield return DebugChannel.Serve(host);
                yield break;
            }
            if (req.tests == "perf") { yield return Guard(PerfRun(), "performance"); yield break; }
            if (req.tests.StartsWith("fight") || req.tests == "all") yield return Guard(FightAll(), "fight");
            if (req.tests == "hand" || req.tests == "all") yield return Guard(Hands(), "hand");
            if (req.tests == "look" || req.tests == "all") yield return Guard(TechLook(), "look");
        }

        // ---- look: the CS tech nodes as the player sees them ----
        IEnumerator TechLook()
        {
            var c = new Check { name = "look", passed = true }; result.checks.Add(c);
            var field = AccessTools.Field(typeof(TechonologyItem), "iconPlayer");
            foreach (var id in CsTechIcons.TechIds)
            {
                FairyUIDispatcher.showTecholongy(id);
                TechonologyItem item = null;
                for (float t0 = Time.realtimeSinceStartup; item == null && Time.realtimeSinceStartup - t0 < 15;)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    item = TechonologyView.getInstance()?.getTechnologyItem(id);
                }
                yield return new WaitForSecondsRealtime(1.5f);
                bool ok = item != null && CsTechIcons.Applied(field.GetValue(item) as UISpriteCatPlayer);
                if (!ok) c.passed = false;
                c.details.Add(id + ": node shown " + (item != null) + ", CS2 icon " + ok);
                yield return Shot("tech-" + id);
            }
            // the "tech researched" popup: its unlock text must end above the row of item icons (a long list of guns ran
            // into them, user 11-10). Checked on a game tech too, and once more with the old long text, which must be
            // caught as overlapping, so the measurement itself is proven.
            foreach (var id in CsTechIcons.TechIds.Concat(new[] { "ranged-3" }))
            {
                var dlg = FairyUIDispatcher.showTecholongyComplete(id);
                yield return new WaitForSecondsRealtime(1.5f);
                float gap = PopupGap(dlg, null, out int lines, out int icons);
                bool clear = gap >= 0 && lines > 0 && icons > 0;
                if (!clear && id != "ranged-3") c.passed = false;
                c.details.Add("popup " + id + ": " + lines + " lines, " + icons + " icons, text ends " + gap.ToString("0") + " px above the icons" + (clear ? "" : " OVERLAP"));
                if (id == CsData.Tech3Id)
                {
                    yield return Shot("popup-" + id);
                    string old = "解锁配方（枪械组装流水线、枪械组装中心制造）：" + string.Join("、", CsData.Guns.Where(g => g.Tier == 3).Select(g => g.Name)) + "。";
                    float oldGap = PopupGap(dlg, old, out int oldLines, out _);
                    bool caught = oldGap < 0;
                    if (!caught) c.passed = false;
                    c.details.Add("old long text: " + oldLines + " lines, gap " + oldGap.ToString("0") + " px, caught as overlap " + caught);
                }
                try { dlg?.closeDialog(); } catch { }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            // every item, recipe and recipe-unlock row draws our CS2 picture, not the icon of the game row it was cloned from
            int withIcon = 0, total = 0;
            void Count(BaseDef d) { if (d == null) return; total++; if (d.RawIcon != null && d.Sprite_RawIcon != null) withIcon++; else c.details.Add("no CS2 picture on " + d.GetType().Name + " " + d.ID); }
            foreach (var g in CsData.Guns)
            {
                Count(ImplDef<T_WeaponDef>.getDataID(g.Row(0))); Count(ImplDef<F_ProductListDef>.getDataID(g.RecipeId));
                if (g.RecipeUnlockId != null) Count(ImplDef<TechnologyUnlockItemDef>.getDataID(g.RecipeUnlockId));
            }
            foreach (var a in CsData.Armor)
            {
                Count(ImplDef<T_ClothDef>.getDataID(a.Row(0))); Count(ImplDef<F_ProductListDef>.getDataID(a.RecipeId));
                if (a.RecipeUnlockId != null) Count(ImplDef<TechnologyUnlockItemDef>.getDataID(a.RecipeUnlockId));
            }
            c.details.Add("rows with our CS2 picture " + withIcon + "/" + total);
            if (withIcon != total || total == 0) c.passed = false;
            c.summary = string.Join("; ", c.details.Take(6));
        }

        // pixels between the bottom of the popup's unlock text and the top of its first item icon (negative = overlap)
        static float PopupGap(TechonologyComplete dlg, string text, out int lines, out int icons)
        {
            lines = 0; icons = 0;
            var info = dlg?.transform.Find("Info");
            var label = info?.Find("Unlock_Content/Label")?.GetComponent<TMPro.TextMeshProUGUI>();
            var grid = info?.Find("Scroll View_Item/Grid");
            if (label == null || grid == null) return -9999;
            if (text != null) label.text = text;
            label.ForceMeshUpdate();
            lines = label.textInfo.lineCount;
            float bottom = float.MaxValue;
            for (int i = 0; i < label.textInfo.characterCount; i++)
            {
                var ch = label.textInfo.characterInfo[i];
                if (ch.isVisible) bottom = Mathf.Min(bottom, label.rectTransform.TransformPoint(new Vector3(0, ch.descender, 0)).y);
            }
            float top = float.MinValue; var corners = new Vector3[4];
            foreach (Transform t in grid)
                if (t.gameObject.activeSelf && t is RectTransform rt) { icons++; rt.GetWorldCorners(corners); top = Mathf.Max(top, corners[1].y); }
            if (bottom == float.MaxValue || icons == 0) return -9999;
            return bottom - top;
        }

        Check CheckDefs()
        {
            var c = new Check { name = "rows, bench, tech, patches", passed = true };
            int w = 0;
            foreach (var g in CsData.Guns)
                for (int q = 0; q < 4; q++)
                {
                    var r = ImplDef<T_WeaponDef>.getDataID(g.Row(q));
                    if (r == null) { c.passed = false; c.details.Add("missing weapon " + g.Row(q)); continue; }
                    w++;
                    if (q == 0) c.details.Add(g.Name + " q1: ATK " + r.ATK + " x" + r.Attack_Bullet + ", mag " + r.Max_Bullet + ", reload " + r.Reload + ", range " + r.Range + ", armour pierce " + r.AntiArmor + ", icon " + (r.Sprite_RawIcon != null));
                }
            foreach (var g in CsData.Guns)
            {
                var p = ImplDef<F_ProductListDef>.getDataID(g.RecipeId);
                if (p == null) { c.passed = false; c.details.Add("missing recipe " + g.RecipeId); }
                else if (p.OutMaterial != "18_" + g.Row(0) + "_1") { c.passed = false; c.details.Add("recipe " + p.ID + " makes " + p.OutMaterial); }
            }
            foreach (var a in CsData.Armor)
                for (int q = 0; q < 4; q++)
                {
                    var r = ImplDef<T_ClothDef>.getDataID(a.Row(q));
                    if (r == null) { c.passed = false; c.details.Add("missing armour " + a.Row(q)); continue; }
                    if (q == 0) c.details.Add(a.Name + " q1: defend " + r.Defend + ", head protect " + r.HeadProtect + ", slot " + r.getSubType() + ", skin " + r.Skin);
                }
            foreach (var a in CsData.Armor)
            {
                var p = ImplDef<F_ProductListDef>.getDataID(a.RecipeId);
                if (p == null || p.OutMaterial != "17_" + a.Row(0) + "_1") { c.passed = false; c.details.Add("armour recipe " + a.RecipeId + " " + p?.OutMaterial); }
            }
            // armour is discontinued (11-10): merchants neither sell nor buy it (its rows sit in no group)
            int expectRows = CsData.Guns.Count * 4, expectBuy = CsData.Guns.Count * 4, expectSoldiers = CsData.Guns.Count(g => g.Category != "zeus");
            int armourGoods = ImplDef<MapTraderBuyDef>.beans.Count(r => r.ID.Length == 6 && r.ID.StartsWith("886") && r.ItemGroup == -1 && r.itemReward?.type == 17);
            int armourBuys = ImplDef<MapTraderSellDef>.beans.Count(r => r.ID.Length == 6 && r.ID.StartsWith("887") && r.ItemGroup == -1 && r.ItemId != null && r.ItemId.StartsWith("17_"));
            c.details.Add("discontinued armour rows out of every merchant group: goods " + armourGoods + ", purchases " + armourBuys + " (of " + CsData.Armor.Count * 4 + " each)");
            if (armourGoods != CsData.Armor.Count * 4 || armourBuys != CsData.Armor.Count * 4) c.passed = false;
            int goods = ImplDef<MapTraderBuyDef>.beans.Count(r => r.ID.Length == 6 && r.ID.StartsWith("886") && r.ItemGroup == 1 && r.itemReward != null && r.ItemId == r.itemReward.type + "_" + r.itemReward.id);
            int purchases = ImplDef<MapTraderSellDef>.beans.Count(r => r.ID.Length == 6 && r.ID.StartsWith("887") && r.ItemGroup == 1);
            var soldierIds = new HashSet<string>(Enumerable.Range(0, CsData.Guns.Count).Select(CsData.SoldierId));
            int soldiers = ImplDef<ESoliderConfigDef>.beans.Count(r => soldierIds.Contains(r.ID));
            int squads = ImplDef<E_SquadConfig_Def>.beans.Count(r => r.SoliderSelected != null && r.SoliderSelected.Split('|').Any(s => soldierIds.Contains(s.Split('_')[0])));
            c.details.Add("weapon merchants sell " + goods + " and buy " + purchases + " rows (expected " + expectRows + " and " + expectBuy + "), soldiers " + soldiers + " in " + squads + " squads");
            var sd = ImplDef<ESoliderConfigDef>.getDataID(CsData.SoldierBase + "0");
            if (sd != null) c.details.Add("soldier " + sd.ID + " " + sd.Name + " equip " + sd.Equip);
            if (goods != expectRows || purchases != expectBuy || soldiers != expectSoldiers || squads == 0) c.passed = false;
            c.details.Add("config killRewardMultiplier " + CsConfig.Get().killRewardMultiplier);
            int withSound = CsData.Guns.Count(g => ImplDef<T_WeaponDef>.getDataID(g.Row(0))?.ATKMusic?.StartsWith(CsSound.Prefix) == true);
            c.details.Add("CS2 sounds: " + withSound + "/" + CsData.Guns.Count + " guns, files missing " + CsSound.Missing + (CsSound.Error != null ? ", error " + CsSound.Error : ""));
            if (withSound != CsData.Guns.Count || CsSound.Missing > 0) c.passed = false;
            // recipes on the game's own benches, locked behind the CS techs
            int placed = 0, wanted = 0;
            void OnBench(string bench, string recipe)
            {
                wanted++;
                var bd = ImplDef<F_ManufactureDef>.getDataID(bench);
                if (bd != null && (bd.ProductID ?? "").Split('|').Contains(recipe)) placed++; else c.details.Add("recipe " + recipe + " missing on bench " + bench);
            }
            foreach (var g in CsData.Guns) foreach (var id in CsData.GunBenches[g.Tier]) OnBench(id, g.RecipeId);
            c.details.Add("recipes on the game's benches " + placed + "/" + wanted);
            if (placed != wanted) c.passed = false;
            // discontinued armour: on no bench, unlocked by no tech
            int armourLeft = 0;
            foreach (var a in CsData.Armor)
            {
                foreach (var id in CsData.ArmorBenches) if ((ImplDef<F_ManufactureDef>.getDataID(id)?.ProductID ?? "").Split('|').Contains(a.RecipeId)) armourLeft++;
                foreach (var t in ImplDef<TechnologyDef>.beans) if (t?.NonBuilding_unlock != null && t.NonBuilding_unlock.Split('|').Contains(a.RecipeUnlockId)) armourLeft++;
            }
            c.details.Add("armour still on a bench or a tech " + armourLeft);
            if (armourLeft != 0) c.passed = false;
            // the in-game texts: no gun still promises a dropped effect (capture, piercing, door breaching)
            foreach (var g in CsData.Guns)
            {
                var wr = ImplDef<T_WeaponDef>.getDataID(g.Row(0));
                foreach (var bad in new[] { "制服", "俘虏", "贯穿", "穿墙", "破门" })
                    if ((wr?.Desc ?? "").Contains(bad) || (ImplDef<TechnologyUnlockItemDef>.getDataID(g.RecipeUnlockId ?? "")?.Desc ?? "").Contains(bad))
                    { c.passed = false; c.details.Add(g.Name + " text still says " + bad); }
                if (wr != null && !wr.Desc.StartsWith(g.Desc)) { c.passed = false; c.details.Add(g.Name + " row text is not its CsData text"); }
            }
            foreach (var k in new[] { "awp", "revolver", "ump45", "nova", "mag7" }) { var gg = CsData.Guns.First(x => x.Key == k); c.details.Add(gg.Name + ": " + ImplDef<T_WeaponDef>.getDataID(gg.Row(0))?.Desc?.Split('\n')[0]); }
            int locks = 0, lockable = 0;
            foreach (var (rid, uid, tech) in CsData.Guns.Select(g => (g.RecipeId, g.RecipeUnlockId, "cs-" + g.Tier)))
            {
                lockable++;
                var td = ImplDef<TechnologyDef>.getDataID(tech);
                var ru = uid == null ? null : ImplDef<TechnologyUnlockItemDef>.getDataID(uid);
                var pr = ImplDef<F_ProductListDef>.getDataID(rid);
                if (td != null && ru != null && ru.AimItem == "1002_" + rid && td.NonBuilding_unlock.Split('|').Contains(uid) && pr?.getTechDef()?.ID == tech) locks++;
                else c.details.Add("recipe " + rid + " not locked behind " + tech);
            }
            c.details.Add("recipes locked behind their CS tech " + locks + "/" + lockable);
            if (locks != lockable) c.passed = false;
            foreach (var tid in new[] { CsData.TechId, CsData.Tech2Id, CsData.Tech3Id, CsData.Tech4Id })
            {
                var td = ImplDef<TechnologyDef>.getDataID(tid);
                if (td == null) { c.passed = false; c.details.Add("missing tech " + tid); }
                else
                {
                    c.details.Add("tech " + td.Name + " after " + td.Pre_technology + " at " + td.HexGridX + "," + td.HexGridY + ", researched in this save " + UserBean.getInstance().techonology_isUnLock(tid));
                    // the line leaves the parent on the side facing the node: "t:" from the top edge for a node above
                    // (HexGridY grows upwards), "b:" from the bottom edge for one below; the other way it crosses the
                    // parent's icon (user 11-10)
                    var par = ImplDef<TechnologyDef>.getDataID(td.Pre_technology);
                    string kind = td.PathNode != null && td.PathNode.Contains(",") ? td.PathNode.Split(',')[1].Substring(0, 1) : "?";
                    string want = par == null ? "?" : td.HexGridY > par.HexGridY ? "t" : "b";
                    if (kind != want) { c.passed = false; c.details.Add("tech " + tid + " line leaves its parent by '" + kind + "', should be '" + want + "'"); }
                    var icon = CsTechIcons.PathOf(tid);
                    if (icon == null || !System.IO.File.Exists(icon)) { c.passed = false; c.details.Add("tech icon missing: " + icon); }
                }
            }
            if (ImplDef<F_ManufactureDef>.getDataID("884") != null) { c.passed = false; c.details.Add("the old CS bench 884 still exists"); }
            if (CsPatches.H == null) { c.passed = false; c.details.Add("patches not applied: " + CsPatches.Error); }
            else foreach (var m in CsPatches.H.GetPatchedMethods()) c.details.Add("patched " + m.DeclaringType?.Name + "." + m.Name);
            c.summary = w + "/" + CsData.Guns.Count * 4 + " weapon rows; " + string.Join("; ", CsDefLoader.Report);
            return c;
        }

        // ---- fight ----
        IEnumerator FightAll()
        {
            var list = ImplDef<MonsterDef>.beans;
            monsterId = list.Count > 0 ? list[0].ID : null;
            result.checks.Add(new Check { name = "test monster", info = true, passed = monsterId != null, summary = monsterId + " " + (list.Count > 0 ? list[0].Name : "") });
            if (monsterId == null) yield break;
            var h = heroes[0];
            // alone, far from the other colonists (they would shoot the monsters too)
            var spot = new Check { name = "quiet spots", info = true, passed = true }; result.checks.Add(spot);
            Locked(() => h.setRunAI(AI_Base.AI_TYPE.战斗));
            Freeze(h, true);
            // fight:taser = one gun, fight:mp9,nova = some, fight:tier2 = one tier
            string only = req.tests.StartsWith("fight:") ? req.tests.Substring(6) : null;
            var pick = only == null ? null : new HashSet<string>(only.Split(','));
            foreach (var g in CsData.Guns)
            {
                if (pick != null && !pick.Contains(g.Key) && !pick.Contains("tier" + g.Tier)) continue;
                bool down = false; Locked(() => down = Down(h));
                if (down) { result.checks.Add(new Check { name = "shooter", passed = false, summary = h.heroBean?.Name + " is down; the remaining fights were skipped" }); break; }
                // a fresh spot for every gun: colonists who came running to help stay around the last one
                bool ok = false; Locked(() => ok = MoveAway(h, 30, spot));
                if (!ok) { spot.passed = false; break; }
                spot.details.Add(g.Key + ": " + spot.summary);
                SetClock(1f); yield return new WaitForSecondsRealtime(0.5f); SetClock(0f);
                yield return Guard(Fight(h, g), "fight " + g.Key);
                if (lastHits == 0)
                {   // the other colonists came running and killed everything before this gun hit once: once more elsewhere
                    var first = result.checks.LastOrDefault(x => x.name == "fight " + g.Name);
                    if (first != null) { first.info = true; first.summary = "(retried) " + first.summary; }
                    Locked(() => ok = MoveAway(h, 40, spot));
                    if (ok) { SetClock(1f); yield return new WaitForSecondsRealtime(0.5f); SetClock(0f); yield return Guard(Fight(h, g), "fight " + g.Key); }
                }
            }
            Locked(() => h.backBaseAI());
            Freeze(h, false);
        }

        // the other colonists on the map stand still while the tests run (TestFreezePatch skips their logic), and they and
        // the shooter cannot be hurt: with nobody helping, the shooter once died to the test monsters by the 12th gun
        // and every later fight ran with a dead man (11-09)
        readonly Dictionary<long, bool> wasInvincible = new Dictionary<long, bool>();
        void Freeze(BaseHero keep, bool on)
        {
            lock (TestFreezePatch.Frozen)
            {
                TestFreezePatch.Frozen.Clear();
                Locked(() =>
                {
                    foreach (var o in MapManager.getInstance().heroManager.getAllianceFaction(1).getRoles())
                    {
                        if (o?.roleBean == null) continue;
                        long uid = o.getUID();
                        if (on)
                        {
                            if (!wasInvincible.ContainsKey(uid)) wasInvincible[uid] = o.roleBean.isInvincible;
                            o.roleBean.isInvincible = true;
                            if (o != keep) TestFreezePatch.Frozen.Add(uid);
                        }
                        else if (wasInvincible.TryGetValue(uid, out bool was)) o.roleBean.isInvincible = was;
                    }
                    if (!on) wasInvincible.Clear();
                });
            }
        }

        static bool Down(BaseHero h) => h.heroBean == null || h.heroBean.isDead() || h.heroBean.isFallDown();

        bool MoveAway(BaseHero h, int clear, Check c)
        {
            var others = heroes.Where(o => o != h).Select(o => new Vector2(o.cellX, o.cellY)).ToList();   // where they are now
            int rows = MapGlobal.CellRow, lines = MapGlobal.CellLine;
            for (int r = 0; r < Math.Max(rows, lines); r += 3)
                for (int a = 0; a < Math.Max(8, r); a++)
                {
                    double ang = a * 2 * Math.PI / Math.Max(8, r);
                    int x = h.cellX + (int)(r * Math.Cos(ang)), y = h.cellY + (int)(r * Math.Sin(ang));
                    if (others.Any(o => Mathf.Abs(o.x - x) < clear && Mathf.Abs(o.y - y) < clear)) continue;
                    bool free = true;
                    for (int i = x - 8; i <= x + 8 && free; i++)
                        for (int j = y - 8; j <= y + 8 && free; j++)
                            free = MapTools.isCellInRealMapRect(i, j) && PassableHandler.isPersonPassable((short)i, (short)j, h);
                    if (!free) continue;
                    Place(h, (short)x, (short)y);
                    c.summary = "colonist " + h.heroBean.Name + " at " + x + "," + y;
                    return true;
                }
            c.summary = "no open area " + clear + " cells from everyone";
            return false;
        }

        static void Place(BaseHero r, short x, short y)
        {
            r.doInterruptAction(); r.mod_Move?.stopMove();
            MapGrid.getInstance().releasePlance(r, r.cellX, r.cellY);
            r.setCell(x, y); r.roleBean?.setCell(x, y); MapGrid.getInstance().tryPlace_Role(r, x, y);
        }

        IEnumerator Fight(BaseHero h, GunSpec g)
        {
            var c = new Check { name = "fight " + g.Name }; result.checks.Add(c);
            var def = ImplDef<T_WeaponDef>.getDataID(g.Row(0));
            int rangeCells = (def.Range - 16) / 32;           // the table's Range is in cells, the loaded row in pixels
            bool zeus = g.Category == "zeus";
            int dist = zeus ? 4 : g.Key == "ump45" ? 4 : Math.Max(2, Math.Min(rangeCells - 2, 7));   // closer than ~3 cells the colonist punches instead
            int want = zeus ? 1 : 3;
            var spawned = new List<FWMonsterBean>();
            long cp0 = 0;
            int hits0 = CsStats.Get(CsStats.Hits, g.Key), hs0 = CsStats.Get(CsStats.Headshots, g.Key), ex0 = CsStats.Get(CsStats.Executes, g.Key), k0 = CsStats.Get(CsStats.Kills, g.Key);
            int rt0 = CsStats.Get(CsStats.Retargets, g.Key), sk0 = CsStats.Get(CsStats.Skipped, g.Key), sh0 = CsStats.Get(CsStats.Shots, g.Key);
            var fx0 = Effects(g.Key);
            Locked(() =>
            {
                try { h.heroBean.addHp(100000); } catch { }   // a hurt colonist keeps away from fights: start each one healthy
                Arm(h, g.Row(0), c); Refresh(h, c);
                cp0 = Game.getCreditPoint();
                for (int a = 0; a < 360 && spawned.Count < want; a += 20)
                {
                    double ang = a * Math.PI / 180;
                    short mx = (short)(h.cellX + (int)Math.Round(dist * Math.Cos(ang))), my = (short)(h.cellY + (int)Math.Round(dist * Math.Sin(ang)));
                    if (!MapTools.isCellInRealMapRect(mx, my) || !PassableHandler.isPersonPassable(mx, my, h)) continue;
                    var m = GMCommand.getInstance().Monster_AddMoster(monsterId, 2, mx, my);
                    // the USP's "unhurt target" check needs the monster's own full health; the others get more to chew on
                    // kill-triggered effects need this gun to land the last hit before the other colonists do
                    if (m != null) { if (g.Key != "usp_silencer") m.setHp(zeus ? 1200 : KillEffect.Contains(g.Key) ? 200 : 1500); spawned.Add(m); a += 40; }
                }
            });
            if (spawned.Count == 0) { c.summary = "no free cell for monsters"; yield break; }
            lock (CsStats.Sources) { CsStats.Watched.Clear(); CsStats.Sources.Clear(); foreach (var m in spawned) CsStats.Watched.Add(m.getUID()); }
            AimAt(h.transform.localPosition, BaseScale() * 0.5f, null);
            SetClock(1f);
            float t0 = Time.realtimeSinceStartup; bool shot = false;
            while (Time.realtimeSinceStartup - t0 < 40 && spawned.Any(m => !m.isDead()))
            {
                yield return new WaitForSecondsRealtime(0.5f);
                if (!shot && CsStats.Get(CsStats.Hits, g.Key) > hits0) { shot = true; yield return Shot("fight-" + g.Key); }
                if (zeus && Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 2) % 10 == 0)
                    Locked(() =>
                    {
                        var m0 = spawned[0]; var mr = m0.getRole();
                        c.details.Add($"t={Time.realtimeSinceStartup - t0:0} AI {h.getRunAI()?.GetType().Name}, weapon {h.heroBean.CurWeapon?.getWeaponDef()?.ID}, monster hp {m0.getHp():0} at {(mr != null ? MathTool.getDistanceI(h.localPos.x, h.localPos.y, mr.localPos.x, mr.localPos.y) : -1)}px, hated {h.roleBean.fightHatreds?.hatreds?.Count}");
                    });
            }
            SetClock(0f);
            long cp1 = 0; Locked(() => cp1 = Game.getCreditPoint());
            int hits = CsStats.Get(CsStats.Hits, g.Key) - hits0, hs = CsStats.Get(CsStats.Headshots, g.Key) - hs0, ex = CsStats.Get(CsStats.Executes, g.Key) - ex0, kills = CsStats.Get(CsStats.Kills, g.Key) - k0;
            int dead = spawned.Count(m => m.isDead());
            lock (CsStats.Sources) { foreach (var kv in CsStats.Sources) c.details.Add("hit by " + kv.Key + " x" + kv.Value); CsStats.Watched.Clear(); }
            Locked(() => c.details.Add("shooter weapon " + h.heroBean.CurWeapon?.getWeaponDef()?.ID + ", AI " + h.getRunAI()?.GetType().Name + ", hp " + h.heroBean.getHp()));
            Locked(() => { foreach (var m in spawned) if (!m.isDead() && m.getRole() is BaseMonster bm) try { GMCommand.getInstance().Monster_RemoveMoster(bm); } catch { } });
            c.summary = $"{dead}/{spawned.Count} dead in {Time.realtimeSinceStartup - t0:0.0}s at {dist} cells; hits {hits}, headshots {hs}, executions {ex}, kills {kills}, credits {cp1 - cp0:+#;-#;0}";
            // the save's other colonists join in (they come running), so judge only this gun: it hit, every kill it made
            // paid its reward exactly, Zeus executed, and the USP's first shot on an unhurt monster was a headshot
            int rt = CsStats.Get(CsStats.Retargets, g.Key) - rt0, sk = CsStats.Get(CsStats.Skipped, g.Key) - sk0;
            c.details.Add("shots fired " + (CsStats.Get(CsStats.Shots, g.Key) - sh0));
            if (g.Key == "elite") c.details.Add("second shots sent to another enemy: " + rt);
            if (g.Key == "glock18") c.details.Add("burst shots skipped while moving: " + sk);
            if (g.Key == "usp_silencer") c.details.Add("headshots " + hs + " of " + hits + " hits on " + spawned.Count + " monsters fresh monsters");
            long reward = (long)Math.Round(kills * g.KillReward * CsConfig.Get().killRewardMultiplier);
            // CS-2 signatures: what fired during this fight; the ones a plain fight always triggers are required
            var fx = Effects(g.Key).Where(kv => kv.Value != (fx0.TryGetValue(kv.Key, out int v) ? v : 0))
                .ToDictionary(kv => kv.Key, kv => kv.Value - (fx0.TryGetValue(kv.Key, out int v) ? v : 0));
            if (fx.Count > 0) c.details.Add("effects: " + string.Join(", ", fx.Select(kv => kv.Key + " x" + kv.Value)));
            string need = RequiredEffect.TryGetValue(g.Key, out var e) ? e : null;
            bool hasNeed = need == null || fx.Keys.Any(k => k.StartsWith(need));
            if (!hasNeed) c.details.Add("MISSING effect " + need);
            c.passed = hits > 0 && cp1 - cp0 == reward && (!zeus || ex > 0) && (g.Key != "usp_silencer" || hs > 0) && (g.Key != "elite" || rt > 0)
                && hasNeed;
            lastHits = hits;
        }

        // effects every fight of that gun shows (kill-triggered ones depend on who lands the last hit, so they are reported only)
        static readonly Dictionary<string, string> RequiredEffect = new Dictionary<string, string>
        {
            ["fiveseven"] = "fiveseven stack", ["cz75a"] = "cz75a fast step", ["mp9"] = "mp9 slow", ["ump45"] = "ump45 close",
            ["sawedoff"] = "sawedoff ", ["m4a4"] = "m4a4 stack", ["p90"] = "p90 fast step", ["ssg08"] = "ssg08 burst", ["g3sg1"] = "g3sg1 suppress", ["negev"] = "negev steady",   // close or far: the monsters may die before they come within 3 cells ["mac10"] = "mac10 burst", ["mp5sd"] = "mp5sd burst", ["bizon"] = "bizon rounds back",
        };
        static readonly HashSet<string> KillEffect = new HashSet<string> { "mac10", "mp5sd", "bizon", "deagle", "ssg08" };

        static Dictionary<string, int> Effects(string key)
        {
            lock (CsStats.Effects) return CsStats.Effects.Where(kv => kv.Key.StartsWith(key + " ")).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // ---- hand ----
        IEnumerator Hands()
        {
            var c = new Check { name = "guns in hand", info = true, passed = true }; result.checks.Add(c);
            var h = heroes[0];
            float s0 = BaseScale();
            // a tough monster 6 cells to the right: the colonist aims sideways at it, and only the aiming and firing poses
            // draw the whole gun (standing drafted shows the back or a sliver of the front view)
            if (monsterId == null && ImplDef<MonsterDef>.beans.Count > 0) monsterId = ImplDef<MonsterDef>.beans[0].ID;
            Freeze(h, true);
            FWMonsterBean dummy = null;
            bool spot = false; Locked(() => spot = MoveAway(h, 30, c));
            // a fresh target for every gun: the old one walks up and the colonist turns to face it
            void NewTarget()
            {
                if (dummy != null && dummy.getRole() is BaseMonster old) try { GMCommand.getInstance().Monster_RemoveMoster(old); } catch { }
                dummy = null;
                for (int dx = 6; dx <= 9 && dummy == null && spot; dx++)
                {
                    short mx = (short)(h.cellX + dx), my = h.cellY;
                    if (MapTools.isCellInRealMapRect(mx, my) && PassableHandler.isPersonPassable(mx, my, h))
                        dummy = GMCommand.getInstance().Monster_AddMoster(monsterId, 2, mx, my);
                }
                dummy?.setHp(1000000);
            }
            // the game's own pistol and rifle first: same colonist, same pose, as the reference for "is a gun drawn here"
            var shots = new List<(string key, string row)> { ("vanilla_pistol", "10110"), ("vanilla_rifle", "10310") };
            foreach (var g in CsData.Guns) shots.Add((g.Key, g.Row(0)));
            foreach (var (key, row) in shots)
            {
                Locked(() => { try { h.heroBean.addHp(100000); } catch { } NewTarget(); Arm(h, row, c); Refresh(h, c); h.setRunAI(AI_Base.AI_TYPE.战斗); });
                SetClock(1f); yield return new WaitForSecondsRealtime(1.2f); SetClock(0f);
                for (int pass = 0; pass < 2; pass++) { AimAt(h.transform.localPosition, s0 * 0.2f, c); yield return new WaitForSecondsRealtime(0.6f); }
                Locked(() => c.details.Add(key + " AI " + h.getRunAI()?.GetType().Name + ", aiming " + (h.roleBean?.CurWeapon?.getWeaponDef()?.ID)));
                yield return Shot("hand-" + key);
            }
            Locked(() =>
            {
                if (dummy != null && dummy.getRole() is BaseMonster bm) try { GMCommand.getInstance().Monster_RemoveMoster(bm); } catch { }
                h.backBaseAI();
            });
            Freeze(h, false);
        }

        // ---- perf: load test for the perf log ----
        static readonly string[] PerfGuns = { "glock18", "deagle", "p90", "mp9", "nova", "xm1014", "ak47", "m4a4", "awp", "g3sg1", "m249", "negev" };

        IEnumerator PerfRun()
        {
            var c = new Check { name = "performance", passed = true }; result.checks.Add(c);
            CsPerf.Window = 100000f; CsPerf.EchoToGameLog = true;   // the phases flush by hand
            var list = ImplDef<MonsterDef>.beans; monsterId = list.Count > 0 ? list[0].ID : null;
            if (monsterId == null) { c.passed = false; c.summary = "no monster kind"; yield break; }
            var shooters = heroes.Take(PerfGuns.Length).ToList();
            const float phase = 25f;
            SetClock(1f);
            yield return PerfPhase(c, "1 空场（模组已加载，没人开枪）", phase, null);

            // the shooters together in an open area, everybody else standing still, nobody can be hurt
            bool ok = false; Locked(() => ok = MoveAway(shooters[0], 30, c));
            if (!ok) { c.passed = false; c.summary = "no open area"; yield break; }
            var spawned = new List<FWMonsterBean>(); Vector3 centre = Vector3.zero;
            Locked(() =>
            {
                var h0 = shooters[0]; int k = 0;
                for (int i = 1; i < shooters.Count; i++)
                    for (; k < 64; k++)
                    {
                        short x = (short)(h0.cellX + (k % 8) - 3), y = (short)(h0.cellY + (k / 8) - 3);
                        if (x == h0.cellX && y == h0.cellY) continue;
                        if (!MapTools.isCellInRealMapRect(x, y) || !PassableHandler.isPersonPassable(x, y, shooters[i])) continue;
                        Place(shooters[i], x, y); k++; break;
                    }
                lock (TestFreezePatch.Frozen)
                {
                    TestFreezePatch.Frozen.Clear();
                    foreach (var o in MapManager.getInstance().heroManager.getAllianceFaction(1).getRoles())
                    {
                        if (o?.roleBean == null) continue;
                        if (!wasInvincible.ContainsKey(o.getUID())) wasInvincible[o.getUID()] = o.roleBean.isInvincible;
                        o.roleBean.isInvincible = true;
                        if (!shooters.Contains(o)) TestFreezePatch.Frozen.Add(o.getUID());
                    }
                }
                for (int i = 0; i < shooters.Count; i++) { try { shooters[i].heroBean.addHp(100000); } catch { } Arm(shooters[i], CsData.Guns.First(g => g.Key == PerfGuns[i]).Row(0), c); Refresh(shooters[i], c); Draft(shooters[i]); }
                centre = h0.transform.localPosition;
                for (int a = 0; a < 360 && spawned.Count < 24; a += 15)
                {
                    double ang = a * Math.PI / 180;
                    short mx = (short)(h0.cellX + (int)Math.Round(8 * Math.Cos(ang))), my = (short)(h0.cellY + (int)Math.Round(8 * Math.Sin(ang)));
                    if (!MapTools.isCellInRealMapRect(mx, my) || !PassableHandler.isPersonPassable(mx, my, h0)) continue;
                    var m = GMCommand.getInstance().Monster_AddMoster(monsterId, 2, mx, my);
                    if (m != null) { m.setHp(1000000); spawned.Add(m); }
                }
            });
            c.details.Add("shooters " + shooters.Count + " (" + string.Join(", ", PerfGuns.Take(shooters.Count)) + "), monsters " + spawned.Count);
            AimAt(centre, BaseScale() * 0.6f, null);
            int hits0 = CsStats.Hits.Values.Sum();
            yield return PerfPhase(c, "2 CS 枪交战 1 倍速", phase, spawned, shooters);
            SetClock(3f);
            yield return PerfPhase(c, "3 CS 枪交战 3 倍速", phase, spawned, shooters);
            int csHits = CsStats.Hits.Values.Sum() - hits0;
            yield return Shot("perf-cs");
            // control: the same people, each with the game's own gun the CS one was copied from (no CS effect, tracer or
            // sound; our hooks still run and leave at once)
            Locked(() => { for (int i = 0; i < shooters.Count; i++) { var g = CsData.Guns.First(x => x.Key == PerfGuns[i]); Arm(shooters[i], g.Template + "0", c); Refresh(shooters[i], c); Draft(shooters[i]); } });
            int hitsCtl0 = CsStats.Hits.Values.Sum();
            yield return PerfPhase(c, "4 对照：原版同款枪交战 3 倍速", phase, spawned, shooters);
            SetClock(0f);
            c.details.Add("CS hits in phases 2-3: " + csHits + ", CS hits in the control phase (should be 0): " + (CsStats.Hits.Values.Sum() - hitsCtl0));
            Locked(() =>
            {
                foreach (var r in MapManager.getInstance().monsterManager.getMonsters().ToList()) if (spawned.Contains(r.monsterBean)) try { GMCommand.getInstance().Monster_RemoveMoster(r); } catch { }
                foreach (var h in shooters) try { h.backBaseAI(); } catch { }
            });
            lock (TestFreezePatch.Frozen) TestFreezePatch.Frozen.Clear();
            Locked(() => { foreach (var o in MapManager.getInstance().heroManager.getAllianceFaction(1).getRoles()) if (o?.roleBean != null && wasInvincible.TryGetValue(o.getUID(), out bool was)) o.roleBean.isInvincible = was; wasInvincible.Clear(); });
            CsPerf.Window = 30f; CsPerf.EchoToGameLog = false;
            c.passed = csHits > 0;
            c.summary = shooters.Count + " shooters, " + spawned.Count + " monsters, " + csHits + " CS hits; one CsArsenal-perf line per phase in details, Player.log and " + CsPerf.FilePath;
        }

        IEnumerator PerfPhase(Check c, string label, float seconds, List<FWMonsterBean> hold, List<BaseHero> draft = null)
        {
            CsPerf.Flush();   // closes whatever ran before; the phase starts clean
            float t0 = Time.realtimeSinceStartup, nextHold = 0f, nextDraft = t0 + 10f; int gc = GC.CollectionCount(0);
            var dts = new List<float>();
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return null;
                dts.Add(Time.unscaledDeltaTime * 1000f);
                if (hold != null && Time.realtimeSinceStartup > nextHold)
                {
                    nextHold = Time.realtimeSinceStartup + 0.5f;   // the monsters stand (a steady load)
                    Locked(() => { foreach (var r in MapManager.getInstance().monsterManager.getMonsters()) if (hold.Contains(r.monsterBean)) { r.mod_Move?.stopMove(); if (r.monsterBean.getHp() < 500000) r.monsterBean.setHp(1000000); } });
                }
                if (draft != null && Time.realtimeSinceStartup > nextDraft)
                {
                    nextDraft = Time.realtimeSinceStartup + 10f;   // the manual fight mode lapses after 30 s
                    Locked(() => { foreach (var h in draft) try { Draft(h); } catch { } });
                }
            }
            CsPerf.Write("---- 阶段 " + label + " ----");
            CsPerf.Flush();
            dts.Sort();
            float p50 = dts.Count > 0 ? dts[dts.Count / 2] : 0, p99 = dts.Count > 0 ? dts[Math.Min(dts.Count - 1, dts.Count * 99 / 100)] : 0;
            c.details.Add("阶段 " + label + ": 帧 " + dts.Count + "，中位 " + p50.ToString("0.0") + "ms，99% " + p99.ToString("0.0") + "ms，最慢 " + (dts.Count > 0 ? dts[dts.Count - 1] : 0).ToString("0")
                + "ms，回收 " + (GC.CollectionCount(0) - gc) + " 次 || " + CsPerf.LastLine);
        }

        // ---- helpers ----
        // what the player's draft order does (RoleCommand): the fight AI in manual mode (it returns to automatic after
        // 30 s); setRunAI alone drops back to the daily AI within a second when no enemy is around
        static void Draft(BaseHero h)
        {
            h.setRunAI(AI_Base.AI_TYPE.战斗);
            (h.getRunAI() as AI_BaseFight)?.doFightMode(AI_BaseFight.FightMode.手动);
        }

        void Arm(BaseHero h, string gunId, Check c)
        {
            var hb = h.heroBean;
            if (hb.weapons == null || hb.weapons.Count == 0) { c.passed = false; c.details.Add(hb.Name + ": no weapon slot"); return; }
            var thing = FWThingUtils.createThing(Game.nextUid(), 18, gunId, 1);
            thing.hp = ImplDef<T_WeaponDef>.getDataID(gunId).HP;
            hb.takeOn(hb.weapons[0], thing, false);
        }

        static void Refresh(BaseHero h, Check c)
        {
            var anim = h.GetComponentInChildren<HeroAnim>(true);
            if (anim == null) { c?.details.Add(h.heroBean.Name + ": no HeroAnim"); return; }
            anim.refreshEquip();
        }

        void AimAt(Vector3 p, float scale, Check c)
        {
            Locked(() =>
            {
                try { var cam = CamearControl.getInstance(); cam.enableMove(); cam.endFollow(); cam.setTo(p.x, p.y, scale); }
                catch (Exception e) { c?.details.Add("aim: " + e.Message); }
            });
        }

        void CloseDialogs()
        {
            try { foreach (var d in UnityEngine.Object.FindObjectsOfType<OfflineDialog>()) d.closeDialog(); }
            catch (Exception e) { Log("close dialogs: " + e.Message); }
        }
    }
}
