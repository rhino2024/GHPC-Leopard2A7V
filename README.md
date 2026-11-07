# 豹 2A7V

Gunner, HEAT, PC! 的 MelonLoader 模组。以本体里的 `LEO2A3` 为供体，新增一辆豹 2A7V：外观换成 2A7V 的车体与炮塔，挂到供体那套 `leo2_rig` 骨骼上，供体原车的模型整个藏起来，履带仍用供体那套蒙皮履带按实测节距滚动。弹药、主炮行程、车顶机枪、昼间分划板都按这辆车重新标定。

模型、材质和 37 张贴图都是本模组自己的东西，随仓库（`assets/Leopard2A7V/`）和发布包一起提供，不用另找。

## 你会看到什么

- 载具列表里多一辆 **Leopard 2A7V**（内部 key `LEO2A7V`）。它是从 `LEO2A3` 克隆出来的，悬挂、乘员位、瞄具机位沿用供体；外观、装甲、内构、弹药与武器参数全部换成这辆车自己的。
- 主炮改称 **Rh-120 L/55**，默认弹种 **DM73 APFSDS-T**（1780 m/s），配置里可换 **DM53**（1750 m/s）：以游戏自己的 AP 弹为模板在运行时复制，两者都是炮口 800 mm 穿深（真车数据，游戏自己按弹速衰减，2 km 处约 665 mm），弹道仍走游戏原本那套飞行模型。
- 行动前的弹药下拉里，这辆车**不会出现**。游戏那个面板是按供体的弹药清单建行的，认不出运行时造出来的弹种，只会把供体那两种旧弹摆在本车名字下面；模组把这几行整个拿掉，车就按自己那份清单出生——默认装 DM73。任务里想换弹，用炮塔里的弹架转移界面。
- 主炮行程 **−9 / +20**，车顶机枪 **−10 / +75**。数字按这辆车自己的页面写，不是照供体的 −10 / +20 抄，所以炮管不会比真车多压一度。
- 车顶那挺 MG3 变成真武器：能切进它的镜，能开火，「下一件武器」里轮得到，射速和声音沿用供体那挺 7.62（1200 rpm），弹链箱在模型上。
- 炮手昼间镜里画的是 2A7V 的分划板，倍率档与分划抬落仍由游戏自己的火控驱动。按 `I` 可以把刻线点亮（荧光），再按一次熄灭；入夜时游戏自己会点亮，不受影响。
- 装甲是**完全独立的 2A7V 防护**：`armour_l2a7v.tsv` 153 行（uniform 53 / drawn 17 / composite 5 / retire 49 / slab 22 / keep 7）。供体 107 个外部装甲节点里 71 个被删除（49 直接退役 + 22 块自有等效板盖住后退役），只剩 36 个行走机构/炮管钢件保留原值——轮子履带的被击伤手感不变。数值是法向 RHAe，命中按 1/cos 换算，再乘配置里的 `ArmorCoefficient`。每块板一颗**凸包** MeshCollider、挂 rig 骨骼、注册进原生碰撞组；每辆车严格车体作用域，多辆同场不会互相认走装甲件。退役有闸门：任何一块该建的没建成，供体一件不拆（不留防护洞），审计日志 `UNCLAIMED=0` 自证。
- 内构**整体是战雷的**：供体 L2A3 内构（`LEO2_aar_*`、`LEO2_int`、混带的 M1 件）的渲染、碰撞体、装甲组件全部拆除，只留乘员命中区、弹药架和损伤组件这些逻辑。战雷 `Interior.obj` 86 组零件精确三分：26 组是装甲、17 组是模块、43 组纯 x-ray 显示（37 发炮弹、4 个乘员人形、耳轴、炮管）。`modules_l2a7v.json` 定义 22 个模块，每个自带 ke/ce 阻力（法向 RHAe mm）——内构阻力只来自这些模块区，不再和供体重复计费；10 个原生 hook 接进游戏的伤害与状态系统，毁伤会显示在状态文本里。供体的蓝色士兵人形被隐藏，x-ray 里看到的是战雷乘员。
- 车长独立周视镜（CITV）：车长位按 `T` 进镜，滚轮循环 15° / 10° / 4° 视场，镜内按 `T` 开关热成像、`Ctrl` 切极性；镜体美术随观瞄一起转。
- 车长舱口的镜窗是真玻璃：战雷导出的镜片是不透明灰，看着像贴了图的板，这里换成供体自己的 `Standard (FLIR)` 玻璃着色。
- 车会在地形上压出履带印。供体的压印刷（`grass cutter`，layer 24）被保留下来，TraxManager 才能把车体深度写进地形。

## 装

1. 先彻底退出游戏。游戏开着的时候 `Bin\Mods\*.dll` 是锁住的，覆盖不了。
2. 把 zip 解压到游戏根目录，也就是放着 `GHPC.exe` 的那一层。解出来应该是：

   ```
   Bin\Mods\Leopard2A7V.dll
   Bin\Mods\Leopard2A7V\Leopard2A7V.obj
   Bin\Mods\Leopard2A7V\Leopard2A7V.mtl
   Bin\Mods\Leopard2A7V\Interior.obj     （内构 22 个模块的真实网格，硬依赖）
   Bin\Mods\Leopard2A7V\CitvAssets\citv_monitor  （车长镜显示器）
   Bin\Mods\Leopard2A7V\L2A7V_data.txt   （可选：装甲/模块覆盖表）
   Bin\Mods\Leopard2A7V\textures\        （2K 图）
   Bin\Mods\Leopard2A7V\textures_uhq\    （4K 图，TextureResolution=4k 时用）
   Bin\Mods\Leopard2A7V\skins\           （nato / desert / winter / parade，2K 涂装）
   Bin\Mods\Leopard2A7V\skins_uhq\       （同四套的 4K 涂装，TextureResolution=4k 时用）
   ```

3. 开一次游戏。MelonLoader 会在 `Bin\UserData\MelonPreferences.cfg` 里写出 `[Leopard2A7V]` 这一段。

需要 MelonLoader 0.7.x。本体里必须有 `LEO2A3`：注册、克隆、弹药模板全靠它。进地图编辑器，在载具列表里按显示名「Leopard 2A7V」找。删掉 dll 和 `Bin\Mods\Leopard2A7V\` 这个目录就算卸载。

## 素材

模型重拓扑自《战争雷霆》里的豹 2A7V，作者在此基础上做了大量优化：减面、重新展 UV、贴图按 GHPC 的光照重做，所以这套网格和贴图尺寸是按本作的预算重新配过的，不是原封搬过来的原始资产。原始模型与贴图的版权归 Gaijin Entertainment，本次重拓扑、优化与贴图处理的部分归本模组。

发布包自带这套素材，不依赖任何外部下载：

```
assets/Leopard2A7V/          # 仓库里的原件
    Leopard2A7V.obj          # 单个 OBJ，顶点权重可选
    Leopard2A7V.mtl          # 每个贴图通道一条材质
    textures/                # 37 张 PNG
    textures_uhq/            # 可选：同名的 4K 重导出版本，TextureResolution=4k 时优先读这里
    skins/                   # 四套 2K 涂装
    skins_uhq/               # 四套 4K 涂装的同名重导出
```

装好之后它们应该在 `Bin\Mods\Leopard2A7V\`，目录名和这三个文件名都是写死的，不能改。贴图走 Unity 的 `ImageConversion.LoadImage`，PNG / JPG 都读得了，DDS 读不了。导入贴图的最大边长跟着精度走：2k 是 2048，4k 是 4096。

`textures_uhq\` 只放 4K 里真有提升的那几张（车体/炮塔/炮管的反照率、法线与光泽）；AO 烤图两套一样大，机枪座 4K 里没有，这几项自动回落到 2K。涂装同理：`skins_uhq\` 里是四套涂装的车体、炮塔、炮管反照率 4K 版，机枪座那张两套都没有，用的是 2K 涂装图而不是未涂装的出厂贴图。4K 一张 4096² 解码后是 64 MB，车体和炮塔各三张，显存吃紧就留在 2k。

改过素材的话，`.mtl` 里的路径按相对 `Leopard2A7V\` 解析，贴图可以整批替换。

## 能改的东西

全在 `Bin\UserData\MelonPreferences.cfg` 的 `[Leopard2A7V]` 段。这个文件是所有模组共用的一个，游戏退出时才写盘。

| 名字 | 默认 | 作用 |
| --- | --- | --- |
| Skin | factory | 涂装方案：factory / nato / desert / winter / parade |
| TextureResolution | 2k | 贴图精度：2k / 4k；4k 读 `textures_uhq\` 里那套 4096 的图 |
| ArmorCoefficient | 1 | 全部装甲区的强度倍率，1 = 上面那套公开数据，范围 0.3–3 |
| Ammo | dm73 | 主弹种：`dm53`（1750 m/s）或 `dm73`（1780 m/s），两者都是 800 mm 穿深 |

前两项在模组管理器里是**下拉选择**，不用手打——管理器从目录条目的 `singlechoice` 里读可选项，玩家看到的只有实际存在的值。cfg 里 `factory` 与留空等价（出厂 bronzegruen），两种写法都认。`ArmorCoefficient` 是数值项，填超范围会夹到 0.3–3，填错不会让车变无敌或没装甲；`Ammo` 填不认识的值回落到 `dm73`。

其余的都编进代码了，不再是可以改的配置项：供体单位（`LEO2A3`）、模型空间锚点（`VisualOffset`、炮塔回转轴、炮耳轴、车长视角偏移、车顶机枪的各标点）、装甲分区与数值（`LpArmorOverlay.Zones` / `TryXrayZone`），以及几条实现开关（`UseTemplate`、`ShowInterior`、`UseDonorRunningGear`、`UseDonorTrackArt`、`OverlayArmor`、`ReplaceDayReticle`）都是 `LpConfig.cs` 里的常量，改它们要重编译。cfg 里 0.1.x 时代的旧行（`ArmorLayers`、`RealAxis`）已不再被读取。

**已经存档的值优先级高于代码里的新默认值。** 换过 DLL 之后要回头核对 `Skin`、`TextureResolution`、`ArmorCoefficient`、`Ammo` 这四行还是不是你想要的。

## 先说清楚

- 战役模式的 sortie 不读任务自定义器的覆盖，所以这辆车在战役里用的是它自己那份弹药清单，行动前的弹种选择本来也没有。
- 供体身上那三张伪装网剥不掉（隐藏白名单里的瞄具豁免会吞到炮塔骨），进场景能直接看到。
- 主炮 −9 / +20、车顶机枪 −10 / +75、供体履带节距、炮手镜倍率都是离线量出来或读表的，还没逐条进游戏核对。有偏差就改上面那几个数字，不用改代码。
- 同一会话里第二次生成整车，早先会变品红（任务结束卸载 bundle 后材质 shader 指针悬空）。已按 shader 名重抓模板并原地修缓存，但仍需复验。
- 只换这一辆车的皮，不改别的载具，也不碰游戏的存档和资源文件。

## 想自己编译

需要 .NET SDK（目标框架 net472，本机用的是 .NET 10 的 SDK）：

```bash
dotnet build -c Release -p:GameDir="E:/SteamLibrary/steamapps/common/Gunner HEAT PC/Bin"
```

`GameDir` 指到游戏的 `Bin` 目录，不是游戏根目录：MelonLoader、0Harmony 和 `GHPC_Data\Managed\*.dll` 这十一项引用都从那里解析，所以编译机上也得有一份装好 MelonLoader 的 GHPC。不给 `-p:GameDir=` 就用 `Leopard2A7V.csproj` 里写的默认值。产物在 `src/Leopard2A7V/bin/Release/Leopard2A7V.dll`，替换 `Bin\Mods\Leopard2A7V.dll` 之前先确认游戏进程数为 0。

`LpSightData.cs` 是昼间分划的线段表（mrad 端点），`LpConfig.cs` 是所有可调项的注册处。

## In English

A Leopard 2A7V for GHPC, cloned from the vanilla `LEO2A3` so that suspension, crew stations and optic slots stay the donor's own while the art, armor, interior, gun codex, rounds, elevation arc, roof machine gun and reticles become this vehicle's. The donor's exterior armor is removed (71 nodes retired or replaced by own slabs; 36 running-gear/gun-steel nodes kept) and its interior renderers, colliders and armor are stripped entirely — the War Thunder Interior.obj takes over the x-ray, and 22 own module zones carry the interior resistance. The main gun is renamed `Rh-120 L/55` and fires `DM73 APFSDS-T` by default (`DM53` selectable), built at runtime from the donor's AP round so that it carries the L/55's muzzle velocity and 800 mm of penetration at the muzzle (the game scales it down with the round's own velocity decay, to about 665 mm at 2 km) through the game's own ballistic model; the mission customiser's ammunition row is removed for this vehicle, which therefore spawns on its own list. Elevation is limited to −9/+20 for the gun and −10/+75 for the roof MG, and that MG is a real weapon with its own sight, fire control and 1200 rpm sound. The vehicle model is retopologised from the Leopard 2A7V in War Thunder and then heavily reworked here (decimated, re-UV'd, textures rebuilt for this game's lighting), with the original model and textures remaining Gaijin Entertainment's. That set, its material and the 37 textures ship with both the repository (`assets/Leopard2A7V/`) and the release archive, so unzipping to the game root is all the setup there is. The arcs, the tread pitch and the sight magnification were measured offline and still want in-game confirmation. MIT, unrelated to Radian Simulations.

## 授权

代码按 MIT 授权，见 `LICENSE`。模型重拓扑自《战争雷霆》的豹 2A7V，原始模型与贴图版权归 Gaijin Entertainment；重拓扑、减面、UV 重展与贴图重制是本模组做的。本模组与 Gaijin Entertainment、Radian Simulations 均无关联。在当前 Steam 版上实测过，游戏更新有可能让它失效。
