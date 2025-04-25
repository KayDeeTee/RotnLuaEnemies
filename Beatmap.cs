using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using Shared.RhythmEngine;
using System.Linq;
using Newtonsoft.Json.Linq;
using EnemyMod;
using UnityEngine;
using RhythmRift;

namespace EnemyMod;

internal static class RRBeatmapPatch
{
    public static bool ShouldOverride = false;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RRStageController), "ParseBeatmapsForPreloadData")]
    public static bool disable_override()
    {
        ShouldOverride = false;
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RRStageController), "ParseBeatmapsForPreloadData")]
    public static void enable_override()
    {
        ShouldOverride = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BeatmapEvent), "GetFirstEventDataAsInt")]
    public static void GetFirstEventDataAsInt(string dataKey, BeatmapEvent __instance, ref int? __result)
    {
        if (ShouldOverride && dataKey == "EnemyId")
        {
            __result = __instance.GetFirstEventDataAsInt("CustomEnemyId") ?? __result;
        }
    }
}