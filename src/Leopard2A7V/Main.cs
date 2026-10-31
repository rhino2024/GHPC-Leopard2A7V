using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Leopard2A7V.LpMod), "Leopard2A7V", "0.1.7", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace Leopard2A7V
{
    public class LpMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LpConfig.Create();

            if (!System.IO.Directory.Exists(LpConfig.AssetDir))
            {
                MelonLogger.Error("[L2A7V] asset folder missing: " + LpConfig.AssetDir);
                return;
            }

            LpAssets.SetMaxTextureSize(LpConfig.MaxTextureSize);
            MelonLogger.Msg("[L2A7V] texture kit: " + LpConfig.ResolutionName + ", cap " +
                            LpConfig.MaxTextureSize + " px, skins stay at their baked size");
            ObjModel.Preload(LpConfig.AssetDir);
            LpInterior.Preload(LpConfig.AssetDir);
            var harmony = new HarmonyLib.Harmony("local.leopard2a7v.modelswap");
            LpPatches.Install(harmony);
            LpDebug.Install();
            MelonLogger.Msg("[L2A7V] loaded, parsing model in the background");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            LpUnitRegistry.RegisterEverywhere();
        }
    }
}
