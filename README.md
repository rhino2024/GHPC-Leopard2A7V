# 豹 2A7V

Gunner, HEAT, PC! 的 MelonLoader 模组。以本体里的 `LEO2A3` 为供体，新增一辆豹 2A7V：外观换成 2A7V 的车体与炮塔，挂到供体那套 `leo2_rig` 骨骼上，供体原车的模型整个藏起来，履带仍用供体那套蒙皮履带按实测节距滚动。弹药、主炮行程、车顶机枪、昼间分划板都按这辆车重新标定。

模型、材质和 37 张贴图都是本模组自己的东西，随仓库（`assets/Leopard2A7V/`）和发布包一起提供，不用另找。

## 你会看到什么

- 载具列表里多一辆 **Leopard 2A7V**（内部 key `LEO2A7V`）。它是从 `LEO2A3` 克隆出来的，所以装甲布局、悬挂、乘员位、瞄具机位都沿用供体，换掉的只是外观和武器参数。
- 主炮改称 **Rh-120 L/55**，弹种是 **DM53 APFSDS-T**：以游戏自己的 AP 弹为模板在运行时复制一份，换上 L/55 的炮口初速与 653 mm 穿深，弹道仍走游戏原本那套飞行模型。
- 行动前的弹药下拉里，这辆车**不会出现**。游戏那个面板是按供体的弹药清单建行的，认不出运行时造出来的弹种，只会把供体那两种旧弹摆在本车名字下面；模组把这几行整个拿掉，车就按自己那份清单出生——默认装 DM53。任务里想换弹，用炮塔里的弹架转移界面。
- 主炮行程 **−9 / +20**，车顶机枪 **−10 / +75**。数字按这辆车自己的页面写，不是照供体的 −10 / +20 抄，所以炮管不会比真车多压一度。
- 车顶那挺 MG3 变成真武器：能切进它的镜，能开火，「下一件武器」里轮得到，射速和声音沿用供体那挺 7.62（1200 rpm），弹链箱在模型上。
- 炮手昼间镜里画的是 2A7V 的分划板，倍率档与分划抬落仍由游戏自己的火控驱动。

## 装

1. 先彻底退出游戏。游戏开着的时候 `Bin\Mods\*.dll` 是锁住的，覆盖不了。
2. 把 zip 解压到游戏根目录，也就是放着 `GHPC.exe` 的那一层。解出来应该是：

   ```
   Bin\Mods\Leopard2A7V.dll
   Bin\Mods\Leopard2A7V\Leopard2A7V.obj
   Bin\Mods\Leopard2A7V\Leopard2A7V.mtl
   Bin\Mods\Leopard2A7V\textures\        （37 张图）
   ```

3. 开一次游戏。MelonLoader 会在 `Bin\UserData\MelonPreferences.cfg` 里写出 `[Leopard2A7V]` 这一段。

需要 MelonLoader 0.7.x。本体里必须有 `LEO2A3`：注册、克隆、弹药模板全靠它。进地图编辑器，在载具列表里按显示名「Leopard 2A7V」找。删掉 dll 和 `Bin\Mods\Leopard2A7V\` 这个目录就算卸载。

## 素材

发布包里那套模型是自制资产，不依赖任何外部下载：

```
assets/Leopard2A7V/          # 仓库里的原件
    Leopard2A7V.obj          # 单个 OBJ，顶点权重可选
    Leopard2A7V.mtl          # 每个贴图通道一条材质
    textures/                # 37 张 PNG
```

装好之后它们应该在 `Bin\Mods\Leopard2A7V\`，目录名和这三个文件名都是写死的，不能改。贴图走 Unity 的 `ImageConversion.LoadImage`，PNG / JPG 都读得了，DDS 读不了。`MaxTextureSize` 默认 2048，显存紧就降到 1024。

改过素材的话，`.mtl` 里的路径按相对 `Leopard2A7V\` 解析，贴图可以整批替换。

## 能改的东西

全在 `Bin\UserData\MelonPreferences.cfg` 的 `[Leopard2A7V]` 段。这个文件是所有模组共用的一个，游戏退出时才写盘。

| 名字 | 默认 | 作用 |
| --- | --- | --- |
| Enabled | true | 总开关 |
| SourceUnitKey | LEO2A3 | 拿来克隆的供体单位 |
| FriendlyName | Leopard 2A7V | 界面上显示的名字 |
| ConvertSourceUnit | false | 连本体原本的 LEO2A3 一起换皮 |
| UseDonorRunningGear | false | 用供体自己的负重轮，不建模型里的那些 |
| UseDonorTrackArt | true | 用供体那套蒙皮履带（唯一能正常滚动的选项） |
| ApplyArmor / LogArmor | false / true | 是否改写装甲数值／只把每层打到日志里 |
| MaxTextureSize | 2048 | 导入贴图的最大边长，显存紧就降到 1024 |
| AlbedoLuminance | 0 | 把导入的反照率往灰色带拉，0 = 原样 |
| NeutralMaps | true | 用中性色替换供体的涂装遮罩与光泽图 |
| UseDetailMaps | true | 绑模型自带的 AO 烘焙 |
| ReplaceDayReticle | true | 炮手昼间镜换 2A7V 分划板 |
| LimitMainGunElevation | true | 是否限制主炮行程 |
| GunElevationDegrees / GunDepressionDegrees | 20 / −9 | 上面那个行程的两头 |
| AddRoofGun | true | 把车顶那挺做成可用武器 |
| RoofGunSightFov | 20 | 车顶机枪镜的视场（水平度） |
| DumpMaterials | true | 建完外观后把材质与渲染器状态打一遍日志 |

还有一组模型空间锚点（`VisualOffset`、`ModelTurretAxis`、`ModelGunAxis`、`CommanderViewOffset`、`RoofGunYawPoint`、`RoofGunTrunnion`、`RoofGunMuzzle`、`RoofGunSight`、`RoofGunRack`），换一套模型时才需要动，每一项在代码里都写了是怎么量出来的。

**已经存档的值优先级高于代码里的新默认值。** 换过 DLL 之后要回头核对 `GunElevationDegrees = 20.0` / `GunDepressionDegrees = -9.0` 这两行还是不是你想要的。

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

A Leopard 2A7V for GHPC, cloned from the vanilla `LEO2A3` so that armor, suspension, crew stations and optic slots stay the donor's own while the art, the gun codex, the round, the elevation arc, the roof machine gun and the gunner's daylight reticle become this vehicle's. The main gun is renamed `Rh-120 L/55` and fires `DM53 APFSDS-T`, built at runtime from the donor's AP round so that it carries the L/55's muzzle velocity and 653 mm of penetration through the game's own ballistic model; the mission customiser's ammunition row is removed for this vehicle, which therefore spawns on its own list. Elevation is limited to −9/+20 for the gun and −10/+75 for the roof MG, and that MG is a real weapon with its own sight, fire control and 1200 rpm sound. The vehicle model, its material and the 37 textures are this mod's own art and ship with both the repository (`assets/Leopard2A7V/`) and the release archive, so unzipping to the game root is all the setup there is. The arcs, the tread pitch and the sight magnification were measured offline and still want in-game confirmation. MIT, unrelated to Radian Simulations.

## 授权

代码、模型、材质与贴图都是本模组自己的作品，一起按 MIT 授权，见 `LICENSE`。本模组与 Radian Simulations 无关。在当前 Steam 版上实测过，游戏更新有可能让它失效。
