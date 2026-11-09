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
    //   all    defs + fight + hand
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
                        IEnumerator e = name.StartsWith("fight") ? FightAll() : name == "hand" ? Hands() : null;
                        if (e == null) return "unknown test " + name;
                        req.tests = name; StartCoroutine(Guard(e, name.Split(':')[0])); return null;
                    }
                };
                yield return DebugChannel.Serve(host);
                yield break;
            }
            if (req.tests.StartsWith("fight") || req.tests == "all") yield return Guard(FightAll(), "fight");
            if (req.tests == "hand" || req.tests == "all") yield return Guard(Hands(), "hand");
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
            int expectRows = (CsData.Guns.Count + CsData.Armor.Count) * 4, expectSoldiers = CsData.Guns.Count(g => g.Category != "zeus");
            int goods = ImplDef<MapTraderBuyDef>.beans.Count(r => r.ID.Length == 5 && r.ID.StartsWith("886") && r.ItemGroup == 1 && r.itemReward != null && r.ItemId == r.itemReward.type + "_" + r.itemReward.id);
            int purchases = ImplDef<MapTraderSellDef>.beans.Count(r => r.ID.Length == 5 && r.ID.StartsWith("887") && r.ItemGroup == 1);
            var soldierIds = new HashSet<string>(Enumerable.Range(0, CsData.Guns.Count).Select(CsData.SoldierId));
            int soldiers = ImplDef<ESoliderConfigDef>.beans.Count(r => soldierIds.Contains(r.ID));
            int squads = ImplDef<E_SquadConfig_Def>.beans.Count(r => r.SoliderSelected != null && r.SoliderSelected.Split('|').Any(s => soldierIds.Contains(s.Split('_')[0])));
            c.details.Add("weapon merchants sell " + goods + " and buy " + purchases + " rows (expected " + expectRows + "), soldiers " + soldiers + " in " + squads + " squads");
            var sd = ImplDef<ESoliderConfigDef>.getDataID(CsData.SoldierBase + "0");
            if (sd != null) c.details.Add("soldier " + sd.ID + " " + sd.Name + " equip " + sd.Equip);
            if (goods != expectRows || purchases != expectRows || soldiers != expectSoldiers || squads == 0) c.passed = false;
            c.details.Add("config killRewardMultiplier " + CsConfig.Get().killRewardMultiplier);
            int withSound = CsData.Guns.Count(g => ImplDef<T_WeaponDef>.getDataID(g.Row(0))?.ATKMusic?.StartsWith(CsSound.Prefix) == true);
            c.details.Add("CS2 sounds: " + withSound + "/" + CsData.Guns.Count + " guns, files missing " + CsSound.Missing + (CsSound.Error != null ? ", error " + CsSound.Error : ""));
            if (withSound != CsData.Guns.Count || CsSound.Missing > 0) c.passed = false;
            var b = ImplDef<F_ManufactureDef>.getDataID(CsData.BenchId);
            if (b == null) { c.passed = false; c.details.Add("missing bench"); } else c.details.Add("bench " + b.Name + " products " + b.ProductID);
            var t = ImplDef<TechnologyDef>.getDataID(CsData.TechId);
            if (t == null) { c.passed = false; c.details.Add("missing tech"); }
            else c.details.Add("tech " + t.Name + " after " + t.Pre_technology + " unlocks " + t.Building_unlock + " at " + t.HexGridX + "," + t.HexGridY + " researched " + t.isUnlock());
            var u = ImplDef<TechnologyUnlockItemDef>.getDataID(CsData.BenchUnlockId);
            if (u == null) { c.passed = false; c.details.Add("missing unlock item"); } else c.details.Add("unlock item aims at " + u.AimItem);
            var t2 = ImplDef<TechnologyDef>.getDataID(CsData.Tech2Id);
            int tier2 = CsData.Guns.Count(g => g.Tier == 2), locks = 0;
            foreach (var g in CsData.Guns.Where(g => g.RecipeUnlockId != null))
            {
                var ru = ImplDef<TechnologyUnlockItemDef>.getDataID(g.RecipeUnlockId);
                if (ru != null && ru.AimItem == "1002_" + g.RecipeId && t2 != null && t2.NonBuilding_unlock.Split('|').Contains(g.RecipeUnlockId)) locks++;
                else c.details.Add("recipe unlock missing for " + g.Key);
            }
            if (t2 == null) { c.passed = false; c.details.Add("missing tech " + CsData.Tech2Id); }
            else c.details.Add("tech " + t2.Name + " after " + t2.Pre_technology + " at " + t2.HexGridX + "," + t2.HexGridY + ", recipe unlocks " + locks + "/" + tier2 + ", researched " + t2.isUnlock());
            if (locks != tier2) c.passed = false;
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
            // fight:taser = one gun, fight:mp9,nova = some, fight:tier2 = one tier
            string only = req.tests.StartsWith("fight:") ? req.tests.Substring(6) : null;
            var pick = only == null ? null : new HashSet<string>(only.Split(','));
            foreach (var g in CsData.Guns)
            {
                if (pick != null && !pick.Contains(g.Key) && !pick.Contains("tier" + g.Tier)) continue;
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
        }

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
            ["sawedoff"] = "sawedoff ",   // close or far: the monsters may die before they come within 3 cells ["mac10"] = "mac10 burst", ["mp5sd"] = "mp5sd burst", ["bizon"] = "bizon rounds back",
        };
        static readonly HashSet<string> KillEffect = new HashSet<string> { "mac10", "mp5sd", "bizon", "deagle" };

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
