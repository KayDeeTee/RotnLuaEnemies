using System.IO;
using System.Linq;
using HarmonyLib;
using RhythmRift;
using Shared.SceneLoading.Payloads;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

namespace EnemyMod;

internal static class RRStageControllerPatch
{
    public static string LuaPath = "";
    public static RRStageController instance;

    [HarmonyPatch(typeof(RRStageController), "UnpackScenePayload")]
    [HarmonyPostfix]
    public static void UnpackScene(RRStageController __instance,  ScenePayload currentScenePayload)
    {
        LuaManager.Reset();
        RREnemyControllerPatch.Reset();

        if (currentScenePayload is not RRDynamicScenePayload payload)
        {
            return;
        }

        LuaPath = Path.Combine(Path.GetDirectoryName(payload.GetBeatmapFileName()), "Lua");
        instance = __instance;

        if (!Directory.Exists(LuaPath))
        {
            EnemyPlugin.Logger.LogInfo("No lua folder found.");
            return;
        }

        var scripts = Directory.EnumerateFiles(LuaPath, "*.lua", SearchOption.AllDirectories);
        var to_load = scripts.ToArray();
        LuaManager.Load(to_load);
    }
    
}