# 迫击炮弹幕指令 · Barrage Orders

适用于《环世界》1.7。启用模组列表中的 **Barrage Orders · 迫击炮弹幕指令**，进入地图后选中一门或多门可对地指定目标的曲射炮塔。原有的单点强制射击仍可使用。

| 指令 | 操作 |
| --- | --- |
| 徐进弹幕 | 在地图上按住左键，从起始边拖出矩形。松开后，所选炮组沿矩形长轴，从按下的一侧逐批推进。 |
| 压制弹幕 | 在圆心按住左键，向外拖到所需半径。松开后，所选炮组共同执行一次覆盖实心圆的任务。 |
| 取消弹幕 | 让选中的炮退出任务。仍在同一任务里的炮会接手剩余区域。绘制期间可用右键或 Esc 取消。 |

下令时，游戏会显示预计用弹数、参与炮数和计划覆盖的格数。选中参与任务的炮可查看其待射位置、已发射数量和预计余量。弹量按每门炮当前弹种在游戏地图上的作用范围估算。射程、屋顶、装填、操作员和游戏散布都会影响执行。游戏仍会按原有规则结算实际命中位置。

## 弹药与兼容性

- 原版使用已装填炮弹或固定投射物。装备不同弹种的炮可以参加同一任务。
- Combat Extended 启用弹药系统时，空弹仓按所选装填弹种预估。装弹后按实际膛内弹种安排，发射和扣弹由 CE 处理。
- 模组依照炮塔、射击动作和投射物识别可用武器。沿用原版或 CE 炮塔机制的其他模组可以自动获得指令。单门炮改为原版强制目标时，它会退出弹幕。进行中的任务会随存档保存。

## 本机验证

在 RimWorld 1.7.5014 的原版测试中，两门炮用灭火泡沫弹覆盖 23 × 11 格的矩形，各发射 6 发，任务中途存档读回后正常结束。在本机两份 CE 16.7.4.0 构建中，13 × 10 格的测试矩形均由两门炮分别发射 3 发和 2 发完成。PLA-Armory 的工坊迫击炮在原版机制和 CE 机制下均完成了射击。四组游戏进程的检查结果保存在[验收记录](Tests/acceptance.txt)中。

## 编译与隔离测试

正式模组位于 `BarrageOrders/`，包 ID 为 `local.barrageorders`。从仓库安装时，先按下段编译，再将 `About`、生成的 `Assemblies`、`Defs` 和 `Languages` 放入游戏的 `Mods/BarrageOrders/`。

本项目使用 .NET 10。对 `BarrageOrders/` 调用 rimworld-mod-mcp 的 `build_mod`，即可针对本机游戏编译并生成 `Assemblies/BarrageOrders.dll`。也可将 `RIMWORLD_MANAGED_DIR` 指向含有 `Assembly-CSharp.dll` 的游戏目录，然后构建 `BarrageOrders/Source/local.barrageorders.csproj`。

回归模组在 `Tests/BarrageOrders.GameTests/`。先编译正式模组和回归模组，将正式模组安装到测试游戏，然后对回归模组调用 `run_test_cycle`，设置 `quicktest=true`，并在 `companion_mods` 里加入 `local.barrageorders`。验证 CE 时另加 `brrainz.harmony` 和 `ceteam.combatextended`，验证工坊武器时再加 `issaczhuang.pla.armory`。在测试会话的 `save_data` 目录查看 `BarrageOrders-tests.txt` 或 `BarrageOrders-ce-tests.txt`，最后一行 `COMPLETE` 表示该配置的检查全部通过。
