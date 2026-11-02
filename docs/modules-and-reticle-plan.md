# L2A7V：模块化损伤 + 装甲联动 + 程序化分划（实现计划）

本文件是下一轮实施用的交接文档。写于 2026-11-02，当时会话上下文耗尽，未开始编码。

## 0. 现状回顾

| 层 | 现在 | 目标 |
| --- | --- | --- |
| 装甲 | ✅ 26 块 xray 网格 × 3 层，挂碰撞体根，区值查表 | 保持 |
| 模块损伤 | ⚠️ 只搬了 3 个供体命中区位置（`LpModules.cs`） | 自建模块 + 接线（阶段 1） |
| 装甲-模块联动 | ❌ 无 | 穿透后按命中点施伤（阶段 2） |
| 炮手分划 | ⚠️ 战雷分划烘成 54 段固定网格 | 参数化生成 + 测距梯（阶段 3） |
| 车长分划 | ✅ 代码绘制（十字/密位尺/短划） | 保持 |

参考实现：`_reference/T90A/T90A.Common/ModuleBuilder.cs`（25 KB）与 `ReticleSpec.cs`（47 KB）。

## 阶段 1：模块化损伤

### 1.1 参考做法（T90A）

```csharp
// ModuleBuilder.Build(vehicle, spec, label, problems)
GameObject go = new GameObject(module.Name);
go.transform.SetParent(bone, false);
BoxCollider box = go.AddComponent<BoxCollider>();     // module.Boxes 每个一个盒子
DestructibleComponent dc = go.AddComponent<DestructibleComponent>();
DcHealth.SetValue(dc, module.Health);                  // 私有字段，反射
DcThreshold.SetValue(dc, module.Threshold);
DcPressure.SetValue(dc, module.Pressure);
DcChildren.SetValue(dc, new List<DestructibleComponent>());
// 接线
chassis.EngineHitZone = dc;   // 或 Transmission/Radiator/LeftTrack/RightTrack
```

私有字段（`DestructibleComponent`）：`_health`、`_damageThreshold`、`_pressureTolerance`、`_children`。

### 1.2 2A7V 模块清单（坐标来自 xray 模型，单位米）

| 模块 | 中心 | 尺寸 | Health | Threshold | 接线目标 |
| --- | --- | --- | --- | --- | --- |
| engine | (−2.331, 1.093, −0.014) | 2.838 × 1.036 × 2.079 | 100 | 30 | `ChassisDamageManager.EngineHitZone` |
| transmission | (−3.099, 0.949, −0.027) | 0.894 × 0.738 × 1.551 | 80 | 25 | `.TransmissionHitZone` |
| radiator | (−3.171, 1.506, 0.000) | 1.014 × 0.309 × 2.027 | 60 | 20 | `.RadiatorHitZone` |
| ammo_hull | (1.835, 1.089, −0.352) | 0.848 × 0.706 × 1.122 | 50 | 20 | 自有事件 → 殉爆 |
| ammo_turret | (−1.373, 2.151, −0.644) | 0.560 × 0.523 × 0.798 | 50 | 20 | 自有事件 → 殉爆 |
| fcs_computer | 由 `FireControlSystem.ComputerComponent` 的节点取 | 0.30³ | 40 | 15 | 替换 `FCS.ComputerComponent` |
| laser | 由 `FCS.LaserComponent` 取 | 0.20³ | 30 | 10 | 替换 `FCS.LaserComponent` |
| barrel | 炮管中段（`WeaponSystem` 节点的子级中点） | 0.15 × 0.15 × 4.0 | 60 | 20 | 替换 `WeaponSystem.BarrelHitZone` |
| breech | 炮尾（`cannon_breech_xray` 中心 (0.205, 1.779, −0.027)） | 0.67 × 0.60 × 0.60 | 50 | 20 | 替换 `WeaponSystem.BreechHitZone` |

注意：
- 侧裙/履带命中区（`LeftTrackHitZone`/`RightTrackHitZone`）**保留供体的**，我们不动
- 所有盒子都要 `isTrigger = false`、layer 与供体命中区一致（读 `ChassisDamageManager.EngineHitZone.gameObject.layer`）

### 1.3 新文件与接线

- 新文件 `LpModulesOwn.cs`：
  - `Build(GameObject unitRoot)`：按表建模块、接线、日志每个模块的结果
  - `static bool Wire(ChassisDamageManager, FireControlSystem, WeaponSystem)`：把组件塞进对应字段
  - 私有字段用 `AccessTools.Field` 反射（Harmony 已引用）
- `LpPatches.cs`：在 `LpXrayArmor.Build` 之后调用 `LpModulesOwn.Build(root)`，并删除对旧 `LpModules.Apply` 的调用（旧文件保留但不调用）

### 1.4 阶段 1 自检清单

1. `dotnet build` 0 error / 0 warning 新增
2. 生成后日志：9 个模块逐个打印 `built module <name> at <pos> health=<h>`
3. 编译器层面确认反射字段名与 `DestructibleComponent` 实际字段一致（用 ilspy 或 grep 反编译源确认 `_health` 等存在）
4. 静态检查：`ChassisDamageManager` 的五个字段是否都是 public（是，见反编译）
5. 与旧 `LpModules.cs` 不冲突：确认旧调用已移除

## 阶段 2：装甲与模块联动

### 2.1 设计

游戏原版靠 spall（破片）打内部件；我们的装甲是自建 `UniformArmor`，破片方向由 `MaxSpallAngleKe` 决定，材质从供体复制。

两种做法：

**A. 复用原版 spall**：把自建装甲件的 `MaxSpallAngleKe/Ce` 设成较大值（如 90°），让破片向车内扩散。零代码逻辑，只改参数。

**B. 主动施伤**：监听 `IArmor.Punctured` 事件，穿透时按命中点向内 raycast 0.5–2 m，命中的 `DestructibleComponent` 直接扣血。

推荐先做 **A**（一行参数），不够再做 B。

### 2.2 阶段 2 自检

1. 打穿炮塔侧面 → 车内乘员/弹药架掉血（AAR 里可见）
2. 打穿首上 → 驾驶员或弹药架受损
3. 不出现“穿透但车内毫发无损”

## 阶段 3：程序化分划

### 3.1 现状

`LpReticle.cs` 从 WT 分划数据读 54 个固定段（`reticle plate: 54 fixed and 0 moving segment(s)`），一次性建 `ReticleSO`。

### 3.2 目标

- 主十字、密位刻度：保持固定（不变形）
- **测距梯**：按当前弹种弹道重新计算（DM53 vs DM73 初速不同）
- 换弹种时重建 plate（监听 `LoadoutManager` 当前弹种变化）

### 3.3 实现要点

```
每 segment 记录：类型（十字/刻度/测距线）、mrad 坐标、是否随弹种变化
对“随弹种变化”的段：
    drop_mrad(d) = (g * t^2 / 2) / d * 1000      // t = d / v0（一级近似）
    用 v0 = 当前弹种 MuzzleVelocity，g = 9.81
重建时机：进入炮手镜时检查当前弹种，变化则重建
```

### 3.4 阶段 3 自检

1. 炮手镜默认（DM73）分划与之前一致（视觉对比）
2. 切到 DM53 后，测距梯刻度应略微下移（初速低 → 弹道更弯）
3. 分划不再“烘焙死”，换弹种有可见变化

## 实施顺序与验证

| 步骤 | 产出 | 验证 |
| --- | --- | --- |
| 1 | `LpModulesOwn.cs` + 接线 | 编译 + 生成日志 + 打发动机舱熄火 |
| 2 | 装甲 spall 参数 | 打穿后车内受损 |
| 3 | 分划重建 | 换弹种刻度变化 |

每步完成后先本地编译、读日志，再交给你实测；三步全部通过后再发布 0.2.0。
