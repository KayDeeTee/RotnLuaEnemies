using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using RhythmRift.Enemies;
using RhythmRift;
using System.Text;
using UnityEngine.UIElements.UIR;
using System.Linq;
using MoonSharp.Interpreter;
using System;
using Shared.RhythmEngine;
using Shared;

namespace EnemyMod;

[BepInPlugin("main.rotn.plugins.enemy_mod", "Enemy Mod", "1.0.0.0")]
public class EnemyPlugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;

        Logger.LogInfo(String.Format("BuildVer: {0}", BuildInfoHelper.Instance.BuildId));
        if (BuildInfoHelper.Instance.BuildId != "1.3.0-b20559")
        {
            Logger.LogInfo("Mod built for a previous version of the game, wait for an update or update this yourself.");
            return;
        }

        Harmony.CreateAndPatchAll(typeof(EnemyPlugin));
        Harmony.CreateAndPatchAll(typeof(RRStageControllerPatch));
        Harmony.CreateAndPatchAll(typeof(RREnemyControllerPatch));
        Harmony.CreateAndPatchAll(typeof(RREnemyPatch));
        Harmony.CreateAndPatchAll(typeof(RRBeatmapPatch));

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        Shared.BugSplatAccessor.Instance.BugSplat.ShouldPostException = ex => false;
    }

}
