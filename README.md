# CS2 军械 · CsArsenal

《Outer Space / 秩序：新曙光》（Steam 3080070）的模组：把 Counter-Strike 2 的 35 把枪做进游戏，接入游戏自己的科技树、枪械设备、武器商人和敌方小队，每把枪有自己的招牌特效。非官方模组，与两款游戏的开发商无关。代码以 GPL-3.0 开源；CS2 的模型渲染图、图标和声音的权利归 Valve 所有。

- 反馈和联系：QQ群 1131996767
- Steam 创意工坊：见 `tools/workshop/published_id.txt`
- 需要前置模组 Harmony（创意工坊 3803065949），需要游戏 0.9.0020 或更新

## 内容
| 科技 | 制造设备 | 枪 |
|---|---|---|
| CS-1 手枪训练 | 粗制枪械加工台、自动枪械组装台 | 格洛克 18、USP 消音版、P2000、P250、双持贝瑞塔、宙斯 x27 |
| CS-2 近距火力 | 自动枪械组装台、枪械组装流水线 | Tec-9、FN57、CZ75、沙漠之鹰、R8 左轮、MAC-10、MP9、MP7、MP5-SD、UMP-45、PP 野牛、新星、截短霰弹枪、MAG-7、XM1014 |
| CS-3 突击步枪 | 枪械组装流水线、枪械组装中心 | 加利尔 AR、法玛斯、AK-47、M4A4、M4A1 消音版、SG 553、AUG、P90、SSG 08 |
| CS-4 精确与压制 | 枪械组装中心 | AWP、G3SG1、SCAR-20、M249、内格夫 |

- 爆头（对人 ×2，对怪物 ×2.5，AK-47 ×3；头盔按头部防护挡掉一部分）、击杀奖励（按 CS 的分档发信用点）、每把枪一个招牌特效（`src/CsEffects.cs`、`src/CsPatches.cs`，说明在 `src/CsData.cs`）。
- CS2 原声经 Unity 音频播放（`src/CsSound.cs`），CS 式曳光、霰弹整轮齐射和散布（`src/CsTracer.cs`）。
- 科技节点和物品用 CS2 的图标（`src/CsTechIcons.cs`，`tools/assets/make_icons.py`）。
- 不新增设备：配方挂在原版枪械设备上，用 CS-1～CS-4 科技解锁。

## 素材
`res/Resources` 里的枪贴图、图标和声音由 `tools/assets` 的脚本从本地 CS2 安装和解包文件生成（Blender 渲染 CS2 模型、提取 HUD 图标和原声），路径写在各脚本开头。

## 构建与安装
Windows：`python tools/build.py`（加 `install` 复制到 `<游戏>/mods/CsArsenal`，只替换 `Manifest` 和 `v_common`，旧安装先备份到 `dist/replaced-*`）。目标 net472，直接引用游戏 `Outer Space_Data/Managed` 的 DLL 和 Harmony。装好后在游戏的模组菜单里启用 Harmony 和本模组。

## 创意工坊
`python tools/workshop.py check|upload [--visibility private|friends|unlisted|public] [--note 文字]`（Windows，Steam 要开着并已登录）：重新编译并上传 `dist/CsArsenal`，标题、简介、封面、前置物品取自 `tools/workshop/item.json` 和 `description.txt`。上传工具 `tools/workshop/Program.cs` 用游戏自带的 Steamworks.NET。

## 测试
`python tools/run_isolated.py --save <星球上的存档> --exact-save --tests <名字>`：在游戏的隔离副本里运行 `src/SelfTest.cs`（原版游戏和玩家存档只读）。`defs` 检查表格行、配方、科技；`fight` 每把枪各打一场（命中、爆头、击杀奖励、招牌特效）；`hand` 截图手持外观；`look` 检查科技树图标和解锁弹窗；`perf` 压测（12 人用 12 把 CS 枪交战，1/3 倍速，再换原版同款枪对照）；`all` = defs + fight + hand + look；`debug` 开调试通道（`tools/dbg.py`）。

## 性能日志
交战时每 30 秒在 `C:/Users/<用户>/AppData/LocalLow/CatStudio/Outer Space/CsArsenal/CsArsenal-perf.log` 写一行：平均帧和最慢帧，每个钩子每秒的调用次数、耗时和最慢一次；单次超过 2ms 的调用会立刻单独记一行。
