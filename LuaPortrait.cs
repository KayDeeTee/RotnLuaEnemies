using EnemyMod;
using HarmonyLib;
using MoonSharp.Interpreter;
using RhythmRift;
using Shared.RhythmEngine;
using UnityEngine;

internal static class RRPortraitViewPatch
{
    [HarmonyPatch(typeof(RRPortraitView), "ApplyCustomPortrait")]
    [HarmonyPostfix]
    public static void ApplyCustomPortrait(ref RRPortraitView __instance)
    {
        bool isHero = __instance == RRStageControllerPatch.instance._portraitUiController._heroPortraitViewInstance;
        RRPortraitView orig = __instance.gameObject.GetComponent<RRPortraitView>();
        if (isHero)
        {
            if (LuaManager.PlayerPortraitLua is Script portrait_lua)
            {
                RRPortraitViewLua lua_portait = __instance.gameObject.AddComponent<RRPortraitViewLua>();
                lua_portait.Copy(orig);
                lua_portait.lua = LuaManager.PlayerPortraitLua;
                Object.Destroy(orig);
            }
            return;
        }
        else
        {
            if (LuaManager.CounterpartPortraitLua is Script portrait_lua)
            {
                RRPortraitViewLua lua_portait = __instance.gameObject.AddComponent<RRPortraitViewLua>();
                lua_portait.Copy(orig);
                lua_portait.lua = LuaManager.CounterpartPortraitLua;
                Object.Destroy(orig);
            }
            return;
        }
    }

    [HarmonyPatch(typeof(RRPortraitView), "PerformanceLevelChange")]
    [HarmonyPostfix]
    public static void PerformanceLevelChange(ref RRPortraitView __instance, RRPerformanceLevel performanceLevel)
    {
        if (__instance.GetType() == typeof(RRPortraitViewLua))
        {
            RRPortraitViewLua lua_portait = __instance as RRPortraitViewLua;
            lua_portait.lua.Globals["performance_level"] = PerformanceToString(performanceLevel);
        }
    }
    public static string PerformanceToString(RRPerformanceLevel performanceLevel) {
        switch (performanceLevel)
        {
            case RRPerformanceLevel.Normal:     return "Normal";
            case RRPerformanceLevel.GameOver:   return "GameOver";
            case RRPerformanceLevel.Terrible:   return "Terrible";
            case RRPerformanceLevel.Poor:       return "Poor";
            case RRPerformanceLevel.Ok:         return "Ok";
            case RRPerformanceLevel.Well:       return "Well";
            case RRPerformanceLevel.Awesome:    return "Awesome";
            case RRPerformanceLevel.Amazing:    return "Amazing";
            case RRPerformanceLevel.VibePower:  return "VibePower";
        }
        return "";
    }

    [HarmonyPatch(typeof(RRPortraitView), "MissRecorded")]
    [HarmonyPostfix]
    public static void MissRecorded(ref RRPortraitView __instance)
    {
        if (__instance.GetType() == typeof(RRPortraitViewLua))
        {
            RRPortraitViewLua lua_portait = __instance as RRPortraitViewLua;
            DynValue dv = lua_portait.lua.Globals.Get("on_miss");
            if (dv == DynValue.Nil) return;
            if (dv.Type != DataType.Function) return;
            dv.Function.Call();
        }
    }

    [HarmonyPatch(typeof(RRPortraitView), "ActivateHealVfx")]
    [HarmonyPostfix]
    public static void ActivateHealVfx(ref RRPortraitView __instance)
    {
        if (__instance.GetType() == typeof(RRPortraitViewLua))
        {
            RRPortraitViewLua lua_portait = __instance as RRPortraitViewLua;
            DynValue dv = lua_portait.lua.Globals.Get("on_heal");
            if (dv == DynValue.Nil) return;
            if (dv.Type != DataType.Function) return;
            dv.Function.Call();
        }
    }

    [HarmonyPatch(typeof(RRPortraitView), "HandleMusicAmplitudeUpdate")]
    [HarmonyPostfix]
    public static void HandleMusicAmplitudeUpdate(ref RRPortraitView __instance, float amplitude)
    {
        if (__instance.GetType() == typeof(RRPortraitViewLua))
        {
            RRPortraitViewLua lua_portait = __instance as RRPortraitViewLua;
            lua_portait.lua.Globals["music_amplitude"] = amplitude;
        }
    }
    
    [HarmonyPatch(typeof(RRPortraitView), "UpdateSystem")]
    [HarmonyPostfix]
    public static void UpdateSystem(ref RRPortraitView __instance, ref FmodTimeCapsule fmodTimeCapsule)
    {
        if (__instance.GetType() == typeof(RRPortraitViewLua))
        {
            RRPortraitViewLua lua_portait = __instance as RRPortraitViewLua;
            lua_portait.lua.Globals["real_time"] = fmodTimeCapsule.Time;
            lua_portait.lua.Globals["true_beat"] = fmodTimeCapsule.TrueBeatNumber;

            DynValue dv = lua_portait.lua.Globals.Get("on_update");
            if (dv == DynValue.Nil) return;
            if (dv.Type != DataType.Function) return;
            dv.Function.Call();
        }
    }
}

public class RRPortraitViewLua : RRPortraitView
{
    public Script lua;
    public void Copy(RRPortraitView other)
    {
        _portraitAnimator = other._portraitAnimator;
        _beatmapAnimatorController = other._beatmapAnimatorController;
        _healVfxParticles = other._healVfxParticles;
        _hasVibePowerAnimation = other._hasVibePowerAnimation;
        _shouldSingAlong = other._shouldSingAlong;
        _singingThreshold = other._singingThreshold;
        _dataDrivenAnimator = other._dataDrivenAnimator;
        _characterTransform = other._characterTransform;
        _characterMaskImage = other._characterMaskImage;
        _characterMask = other._characterMask;
        _doingWellReactionEventRef = other._doingWellReactionEventRef;
        _normalReactionEventRef = other._normalReactionEventRef;
        _doingPoorlyReactionEventRef = other._doingPoorlyReactionEventRef;
        _gameOverReactionEventRef = other._gameOverReactionEventRef;
        _currentAnimStateTrigger = other._currentAnimStateTrigger;
        _activeVOInstance = other._activeVOInstance;
        _audioManager = other._audioManager;
        _shouldInvertReactions = other._shouldInvertReactions;
    }
}