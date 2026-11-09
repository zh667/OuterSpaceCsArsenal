using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using CsArsenal.Kit;

namespace CsArsenal
{
    // Checks in the isolated game (python tools/run_isolated.py --tests <name>).
    //   agents (default): five colonists in a row, left to right: unchanged | SAS route 1 (clothes) + Glock |
    //                     SAS route 2 (frames) | Phoenix route 1 + AK-47 | Phoenix route 2; screenshots paused and walking.
    //   guns:             every round-0 gun in a colonist's hands, one screenshot per gun.
    public class SelfTest : SelfTestBase
    {
        protected override string ModName => "CsArsenal";
        protected override string ModVersion => ModBehaviour.Version;

        readonly List<BaseHero> heroes = new List<BaseHero>();
        readonly List<AgentOverlay> overlays = new List<AgentOverlay>();

        protected override IEnumerator RunTests()
        {
            var c0 = new Check { name = "rows added", passed = true }; result.checks.Add(c0);
            foreach (var g in CsData.Guns) if (ImplDef<T_WeaponDef>.getDataID(g.Id) == null) { c0.passed = false; c0.details.Add("missing gun " + g.Id); }
            foreach (var c in CsData.Clothes) if (ImplDef<T_ClothDef>.getDataID(c.Id) == null) { c0.passed = false; c0.details.Add("missing cloth " + c.Id); }
            c0.summary = string.Join("; ", CsDefLoader.Report);

            SetClock(0f);
            var info = new Check { name = "colonists", info = true, passed = true }; result.checks.Add(info);
            for (int attempt = 0; attempt < 10 && heroes.Count == 0; attempt++)
            {
                Locked(() =>
                {
                    var hm = MapManager.getInstance()?.heroManager;
                    if (hm == null) { info.details.Add("no heroManager"); return; }
                    for (sbyte al = 0; al <= 3; al++)
                    {
                        var f = hm.getAllianceFaction(al);
                        int all = 0, dead = 0;
                        if (f != null) foreach (var h in f.getRoles())
                            {
                                if (h == null || h.heroBean == null) continue;
                                all++;
                                if (h.heroBean.isDead()) { dead++; continue; }
                                // only colonists drawn on the map being shown (others may be aboard the ship or away)
                                if (al == 1 && h.gameObject.activeInHierarchy && MapTools.isCellInRealMapRect(h.cellX, h.cellY)) heroes.Add(h);
                            }
                        info.details.Add("attempt " + attempt + " alliance " + al + ": " + (f == null ? "none" : all + " heroes, " + dead + " dead"));
                    }
                });
                if (heroes.Count == 0) yield return new WaitForSecondsRealtime(2f);
            }
            info.summary = heroes.Count + " alive";
            if (heroes.Count < 2) { info.passed = false; yield break; }

            // the save opens with the offline report over everything, at night: close it and move the clock to midday
            CloseDialogs();
            Locked(() => { try { UserBean.getInstance().curStarMap.addTimeMM(8 * 60000); } catch (Exception e) { info.details.Add("time: " + e.Message); } });
            SetClock(1f); yield return new WaitForSecondsRealtime(1.5f); SetClock(0f);
            CloseDialogs();

            if (req.tests == "guns") { yield return Guard(Guns(), "guns"); yield break; }
            if (req.tests == "gunsizes") { yield return Guard(GunSizes(), "gun sizes"); yield break; }

            int n = Math.Min(5, heroes.Count);
            short x0 = 0, y0 = 0; bool ok = false;
            Locked(() => ok = Lineup(n, out x0, out y0));
            var line = new Check { name = "line up", passed = ok, summary = ok ? "row at " + x0 + "," + y0 : "no free row" }; result.checks.Add(line);
            for (int i = 0; i < n; i++) { var h = heroes[i]; line.details.Add(h.heroBean.Name + " cell " + h.cellX + "," + h.cellY + " local " + h.transform.localPosition + " world " + h.transform.position + " active " + h.gameObject.activeInHierarchy); }

            var eq = new Check { name = "route 1 clothes and guns put on", passed = true }; result.checks.Add(eq);
            Locked(() =>
            {
                if (n > 1) Dress(heroes[1], "sas", "881010", eq);
                if (n > 3) Dress(heroes[3], "phoenix", "883010", eq);
            });
            yield return new WaitForSecondsRealtime(1f);
            var ov = new Check { name = "route 2 overlays", passed = true }; result.checks.Add(ov);
            foreach (var (i, agent) in new[] { (2, "sas"), (4, "phoenix") })
            {
                if (i >= n) continue;
                var o = AgentOverlay.Attach(heroes[i], agent);
                if (o == null) { ov.passed = false; ov.details.Add(agent + ": not attached"); } else { overlays.Add(o); ov.details.Add(o.Note); }
            }
            float s0 = 1f; Locked(() => s0 = CamearControl.getInstance().getScale());
            // let the game move the drawn figures to their new cells, then aim at the middle one
            SetClock(1f); yield return new WaitForSecondsRealtime(0.4f); SetClock(0f);
            var mid = heroes[n / 2];
            foreach (var z in new[] { 0.2f, 0.26f })
            {
                Aim(mid, s0 * z, line);
                yield return new WaitForSecondsRealtime(1.5f);
                Locked(() => line.details.Add("after aim: scale " + CamearControl.getInstance().getScale() + " screen pos of middle colonist " + CamearControl.getInstance().mapPoint2ScreenPoint(mid.transform.localPosition) + " of " + Screen.width + "x" + Screen.height));
                yield return Shot("agents-still-" + Mathf.RoundToInt(z * 100));
            }
            // close-ups of the two route-1 colonists (clothes and the gun in hand)
            foreach (var (i, tag) in new[] { (1, "sas"), (3, "phoenix"), (2, "sas-route2"), (4, "phoenix-route2") })
            {
                if (i >= n) continue;
                Aim(heroes[i], s0 * 0.2f, null);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("close-" + tag);
            }
            SetClock(1f);
            for (int k = 0; k < 3; k++)
            {
                yield return new WaitForSecondsRealtime(2.5f);
                Aim(mid, s0 * 0.26f, null);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("agents-moving-" + k);
            }
            SetClock(0f);
        }

        // The camera may be locked or following something after the save loads.
        void Aim(BaseHero h, float scale, Check c)
        {
            Locked(() =>
            {
                try
                {
                    var cam = CamearControl.getInstance();
                    cam.enableMove(); cam.endFollow();
                    var p = h.transform.localPosition;
                    cam.setTo(p.x, p.y, scale);
                }
                catch (Exception e) { c?.details.Add("aim: " + e.Message); }
            });
        }

        void CloseDialogs()
        {
            try { foreach (var d in UnityEngine.Object.FindObjectsOfType<OfflineDialog>()) d.closeDialog(); }
            catch (Exception e) { Log("close dialogs: " + e.Message); }
        }

        // Moves the first n colonists onto one row, two cells apart, near the first one.
        bool Lineup(int n, out short x0, out short y0)
        {
            var first = heroes[0];
            for (int r = 0; r < 30; r++)
                for (int dy = -r; dy <= r; dy++)
                {
                    short sx = (short)(first.cellX - n), sy = (short)(first.cellY + dy);
                    bool free = true;
                    for (int i = 0; i < n && free; i++)
                    {
                        short x = (short)(sx + 2 * i);
                        free = MapTools.isCellInRealMapRect(x, sy) && PassableHandler.isPersonPassable(x, sy, heroes[i]);
                    }
                    if (!free) continue;
                    for (int i = 0; i < n; i++) Place(heroes[i], (short)(sx + 2 * i), sy);
                    x0 = sx; y0 = sy; return true;
                }
            x0 = y0 = 0; return false;
        }

        static void Place(BaseHero r, short x, short y)
        {
            r.doInterruptAction(); r.mod_Move?.stopMove();
            MapGrid.getInstance().releasePlance(r, r.cellX, r.cellY);
            r.setCell(x, y); r.roleBean?.setCell(x, y); MapGrid.getInstance().tryPlace_Role(r, x, y);
        }

        void Dress(BaseHero h, string agent, string gunId, Check c)
        {
            var hb = h.heroBean; var gd = Game;
            // the save's colonists wear sealed suits (armour + leg armour slots) that cover clothes and hats
            foreach (var e in hb.cloths)
                if (e != null && (e.getEquipType() == 2 || e.getEquipType() == 7) && e.hasEquip()) { hb.takeOffEquip(e); c.details.Add(hb.Name + " took off slot " + e.getEquipType()); }
            foreach (var cs in CsData.Clothes)
            {
                if (cs.Agent != agent) continue;
                FWRoleEquip slot = null;
                foreach (var e in hb.cloths) if (e != null && e.getEquipType() == cs.Slot) { slot = e; break; }
                if (slot == null) { c.passed = false; c.details.Add(agent + " " + cs.Id + ": no slot " + cs.Slot); continue; }
                var thing = FWThingUtils.createThing(gd.nextUid(), 17, cs.Id, 1);
                thing.hp = (float)ImplDef<T_ClothDef>.getDataID(cs.Id).HP;
                hb.takeOn(slot, thing, false);
                c.details.Add(hb.Name + " wears " + cs.Name);
            }
            Arm(h, gunId, c);
            Refresh(h, c);
        }

        // takeOn changes the data; the drawing is rebuilt by HeroAnim.refreshEquip
        static void Refresh(BaseHero h, Check c)
        {
            var anim = h.GetComponentInChildren<HeroAnim>(true);
            if (anim == null) { c?.details.Add(h.heroBean.Name + ": no HeroAnim"); return; }
            anim.refreshEquip();
        }

        void Arm(BaseHero h, string gunId, Check c)
        {
            var hb = h.heroBean;
            if (hb.weapons == null || hb.weapons.Count == 0) { c.passed = false; c.details.Add(hb.Name + ": no weapon slot"); return; }
            var thing = FWThingUtils.createThing(Game.nextUid(), 18, gunId, 1);
            thing.hp = ImplDef<T_WeaponDef>.getDataID(gunId).HP;
            hb.takeOn(hb.weapons[0], thing, false);
            c.details.Add(hb.Name + " holds " + ImplDef<T_WeaponDef>.getDataID(gunId).Name);
        }

        // Eight colonists in a row, each with one gun: the game's own rifle, AK-47 A / A2 / A3, the game's own pistol,
        // Glock A / A2 / A3. Paused shots at two zoom levels plus the screen position of every colonist (for cropping).
        static readonly string[] SizeRow = { "10310", "883010", "883012", "883013", "10110", "881010", "881012", "881013" };

        IEnumerator GunSizes()
        {
            var c = new Check { name = "gun sizes", passed = true }; result.checks.Add(c);
            var h = heroes[0];
            float s0 = 1f; Locked(() => s0 = CamearControl.getInstance().getScale());
            // drafted (fight AI): the game itself picks the weapon stance for the gun type (HeroAnim.indexStandRow)
            Locked(() => h.setRunAI(AI_Base.AI_TYPE.战斗));
            SetClock(1f); yield return new WaitForSecondsRealtime(2f);   // the switch to the weapon stance takes a moment
            foreach (var id in SizeRow)
            {
                Locked(() => { Arm(h, id, c); Refresh(h, c); });
                yield return new WaitForSecondsRealtime(1f);
                for (int k = 0; k < 3; k++)
                {
                    AimAt(h.transform.localPosition, s0 * 0.2f, c);
                    yield return new WaitForSecondsRealtime(0.7f);
                    Locked(() =>
                    {
                        var anim = h.GetComponentInChildren<HeroAnim>(true);
                        var p = CamearControl.getInstance().mapPoint2ScreenPoint(h.transform.localPosition);
                        c.details.Add(id + " shot " + k + " screen " + p + " weaponPose " + (anim != null && anim.isWeaponAction()));
                    });
                    yield return Shot("size-" + id + "-" + k);
                }
            }
            SetClock(0f);
            Locked(() => h.backBaseAI());
        }

        void AimAt(Vector3 p, float scale, Check c)
        {
            Locked(() =>
            {
                try { var cam = CamearControl.getInstance(); cam.enableMove(); cam.endFollow(); cam.setTo(p.x, p.y, scale); }
                catch (Exception e) { c?.details.Add("aim: " + e.Message); }
            });
        }

        // Each gun in the hands of the first colonist, camera close, paused, then a few seconds walking.
        IEnumerator Guns()
        {
            var c = new Check { name = "guns in hand", passed = true }; result.checks.Add(c);
            var h = heroes[0];
            float s0 = 1f; Locked(() => s0 = CamearControl.getInstance().getScale());
            foreach (var g in CsData.Guns)
            {
                Locked(() => { Arm(h, g.Id, c); Refresh(h, c); });
                Locked(() => CamearControl.getInstance().moveFollow((SpriteObject)h));
                Locked(() => CamearControl.getInstance().setTo(h.transform.localPosition.x, h.transform.localPosition.y, s0 * 0.18f));
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("gun-" + g.Id);
            }
        }
    }
}
