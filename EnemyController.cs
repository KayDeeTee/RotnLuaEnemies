using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using HarmonyLib;
using MoonSharp.Interpreter;
using RhythmRift;
using RhythmRift.Enemies;
using RhythmRift.Traps;
using Shared.Audio;
using Shared.PlayerData;
using Shared.RhythmEngine;
using Shared.SceneLoading.Payloads;
using TicToc.ObjectPooling.Runtime;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using static RhythmRift.RREnemyController;

namespace EnemyMod;

internal static class RREnemyControllerPatch
{
    public class EnemyReference
    {
        public RREnemy enemy;
        public string current_anim;
        public float anim_length;
        public float anim_progress;
        public bool anim_loops;
    }

    public static Dictionary<string, DynValue> get_enemy_state(string guid)
    {
        if (modded_enemies.ContainsKey(guid))
        {
            EnemyReference er = modded_enemies[guid];
            RREnemy enemy = er.enemy;
            Dictionary<string, DynValue> ret = new Dictionary<string, DynValue>();
            ret["GUID"] = DynValue.NewString(guid);
            //state
            ret["CurrentHealth"] = DynValue.NewNumber(enemy.CurrentHealthValue);
            ret["CurrentGridPosition"] = DynValue.NewTuple([DynValue.NewNumber(enemy.CurrentGridPosition.x), DynValue.NewNumber(enemy.CurrentGridPosition.y)]);
            ret["IsFacingLeft"] = DynValue.NewBoolean(enemy.IsFacingLeft);
            ret["CollisionCount"] = DynValue.NewNumber(enemy.NumTimesDesiredPositionWasUnavailable);
            //flags
            ret["IsPerformingHitMovement"] = DynValue.NewBoolean(enemy.IsPerformingHitMovement);
            ret["IsBeingPushed"] = DynValue.NewBoolean(enemy.IsBeingPushed);
            ret["IsDying"] = DynValue.NewBoolean(enemy.IsDying);

            ret["IsBeingPushed"] = DynValue.NewBoolean(enemy.IsBeingPushed);
            ret["PushDirection"] = DynValue.NewTuple([DynValue.NewNumber(enemy._pushDirectionOverride.x), DynValue.NewNumber(enemy._pushDirectionOverride.y)]);

            ret["IsBeingTeleported"] = DynValue.NewBoolean(enemy.IsBeingTeleported);
            ret["TeleportDestination"] = DynValue.NewTuple([DynValue.NewNumber(enemy._portalOutGridPosition.x), DynValue.NewNumber(enemy._portalOutGridPosition.y)]);

            ret["IsSnappingToActionRow"] = DynValue.NewBoolean(enemy.IsSnappingToActionRow);
            ret["ActionRowTargetGridPosition"] = DynValue.NewTuple([DynValue.NewNumber(enemy._actionRowMoveTargetGridPosition.x), DynValue.NewNumber(enemy._actionRowMoveTargetGridPosition.y)]);

            ret["OnFire"] = DynValue.NewBoolean(enemy.HasStatusEffectActive(RREnemyStatusEffect.Burning));
            ret["BurningSpeed"] = DynValue.NewNumber(enemy._burningSpeedUpdateRateOverride);

            //animation
            ret["CurrentAnimation"] = DynValue.NewString(er.current_anim);
            ret["AnimProgress"] = DynValue.NewNumber(er.anim_progress);
            return ret;
        }
        return null;
    }

    public static EnemyReference get_enemy_ref(string guid)
    {
        return modded_enemies[guid];
    }
    public static RREnemy get_enemy(string guid)
    {
        if (modded_enemies.ContainsKey(guid))
        {
            return modded_enemies[guid].enemy;
        }
        return null;
    }

    public static void add_enemy(RREnemy instance)
    {
        EnemyReference er = new EnemyReference();
        er.enemy = instance;
        er.current_anim = "Idle";
        er.anim_progress = 0.0f;
        er.anim_loops = true;
        modded_enemies[instance.GroupId.ToString()] = er;
    }

    public static void remove_enemy(RREnemy instance)
    {
        modded_enemies.Remove(instance.GroupId.ToString());
    }

    public static Dictionary<string, EnemyReference> modded_enemies = new Dictionary<string, EnemyReference>();
    public static Dictionary<Guid, int> original_ids = new Dictionary<Guid, int>();
    public static bool NextEnemyIsLua = false;
    public static int NextEnemyTrueId = -1;
    public static void Reset()
    {
        original_ids = new Dictionary<Guid, int>();
        modded_enemies = new Dictionary<string, EnemyReference>();
        NextEnemyIsLua = false;
    }

    [HarmonyPatch(typeof(RREnemyController), "SpawnEnemy", new Type[] { typeof(SpawnEnemyData), typeof(Guid), typeof(FmodTimeCapsule), typeof(int2) })]
    [HarmonyPrefix]
    public static bool SpawnEnemy(ref SpawnEnemyData spawnEnemyData, Guid groupId)
    {
        NextEnemyIsLua = false;
        if (LuaManager.LuaEnemyRemaps.ContainsKey(spawnEnemyData.EnemyId))
        {
            var orig = spawnEnemyData.EnemyId;
            NextEnemyTrueId = orig;
            spawnEnemyData.EnemyId = LuaManager.LuaEnemyRemaps[spawnEnemyData.EnemyId];
            //original_ids[groupId] = orig;
            NextEnemyIsLua = true;
        }
        return true;
    }

    [HarmonyPatch(typeof(RREnemyController), "SpawnEnemy", new Type[] { typeof(SpawnEnemyData), typeof(Guid), typeof(FmodTimeCapsule), typeof(int2) })]
    [HarmonyPostfix]
    public static void ReplaceWithLua(ref RREnemyController __instance, ref RREnemy __result, ref SpawnEnemyData spawnEnemyData, ref Guid groupId, ref FmodTimeCapsule fmodTimeCapsule, ref int2 spawnGridPosition)
    {
        if (NextEnemyIsLua)
        {
            EnemyPlugin.Logger.LogInfo("Replacing enemy.");
            RREnemy orig = __result;
            RRLuaEnemy lua_enemy = orig.gameObject.AddComponent<RRLuaEnemy>();
            lua_enemy.cached_functions = false;
            //Need to steal stuff from original
            EnemyPlugin.Logger.LogInfo("Copying enemy.");
            CopyEnemy(lua_enemy, orig);

            float update_tempo = 1.0f;
            RREnemyDefinition? green_slime = __instance._enemyDatabase.GetEnemyDefinition(1722);
            RREnemyDefinition enemy_def = green_slime.Value;

            Vector3 adjustedTileWorldPosition = __instance.GetAdjustedTileWorldPosition(__result, spawnGridPosition.x, spawnGridPosition.y);
            float spawnTrueBeatNumber = spawnEnemyData.SpawnTrueBeatNumber - update_tempo; // - updateInTempo
            RREnemyInitializationData enemyInitializationData = __instance._enemyInitializationData;
            enemyInitializationData.SetData(enemy_def, spawnEnemyData.ShouldStartFacingRight, spawnTrueBeatNumber, spawnGridPosition, adjustedTileWorldPosition, spawnEnemyData.EnemyLength, groupId, spawnEnemyData.ItemToDropOnDeathId, spawnEnemyData.ShouldIgnoreForTutorialSuccess, spawnEnemyData.ShouldClampToSubdivisions);
            EnemyPlugin.Logger.LogInfo("Initializing enemy.");
            lua_enemy.Initialize(enemyInitializationData, __instance._defaultEnemyMovementCurve, fmodTimeCapsule, __instance._shouldDisableEnemyMovementAnimations);

            EnemyPlugin.Logger.LogInfo("Removing original hooks");
            orig.OnRequestDeathAndRemoval -= __instance.HandleEnemyDeathAndRemovalRequest;
            orig.OnRequestActionRowSoundQueueing -= __instance.HandleEnemyActionRowSoundQueueingRequest;
            orig.OnRequestHoldEnemyPerBeatHeldBonusRecorded -= __instance.HandleHoldEnemyPerBeatHeldBonusRecordedRequest;
            orig.OnHeldEnemyMiss -= __instance.HandleHeldEnemyMiss;
            orig.OnStatusEffectApplied -= __instance.HandleEnemyStatusEffectApplied;
            orig.OnStatusEffectRemoved -= __instance.HandleEnemyStatusEffectRemoved;
            orig.OnRequestSetWorldPosition -= __instance.HandleEnemyRequestSetWorldPosition;
            orig.OnRequestCleanSpecialAudioData -= __instance.HandleCleanSpecialAudioData;
            orig.OnRequestAddToKillCount -= __instance.HandleEnemyAddToKillCount;

            EnemyPlugin.Logger.LogInfo("Adding lua version hooks");
            lua_enemy.OnRequestDeathAndRemoval += __instance.HandleEnemyDeathAndRemovalRequest;
            lua_enemy.OnRequestActionRowSoundQueueing += __instance.HandleEnemyActionRowSoundQueueingRequest;
            lua_enemy.OnRequestHoldEnemyPerBeatHeldBonusRecorded += __instance.HandleHoldEnemyPerBeatHeldBonusRecordedRequest;
            lua_enemy.OnHeldEnemyMiss += __instance.HandleHeldEnemyMiss;
            lua_enemy.OnStatusEffectApplied += __instance.HandleEnemyStatusEffectApplied;
            lua_enemy.OnStatusEffectRemoved += __instance.HandleEnemyStatusEffectRemoved;
            lua_enemy.OnRequestSetWorldPosition += __instance.HandleEnemyRequestSetWorldPosition;
            lua_enemy.OnRequestCleanSpecialAudioData += __instance.HandleCleanSpecialAudioData;
            lua_enemy.OnRequestAddToKillCount += __instance.HandleEnemyAddToKillCount;

            EnemyPlugin.Logger.LogInfo("Removing original from active enemies and adding lua version");
            __instance._activeEnemies.Remove(orig);
            __instance._activeEnemies.Add(lua_enemy);

            //
            //if (spawnEnemyData.ShouldSpawnWithPoofStatusEffect && !instance.IsHealthItem)
            //{
            //    instance.ApplyStatusEffect(RREnemyStatusEffect.Poofing, fmodTimeCapsule, RRUtils.DefaultPoofingDuration);
            //}

            //if (PinsController.IsPinActive("Enigma") && spawnGridPosition.y < RRUtils.EnigmaRowIndex)
            //{
            //    instance.ApplyStatusEffect(RREnemyStatusEffect.Mysterious, fmodTimeCapsule);
            //}

            //if (spawnEnemyData.ShouldSpawnAsVibeChain && !_isVibeChainBroken)
            //{
            //    instance.ApplyStatusEffect(RREnemyStatusEffect.Vibing, fmodTimeCapsule);
            //}
            //else
            //{
            //    instance.SetVibeChainStatus(isActive: false);
            //}

            //instance.SetVibePowerStatus(_isVibePowerActive);
            //if (_shouldSkeletonsBeInvisible && instance is RRSkeletonEnemy && (bool)instance.SpriteRenderer)
            //{
            //    instance.SpriteRenderer.enabled = false;
            //}

            Script? script = LuaManager.GetScriptInstance(NextEnemyTrueId);
            if (script != null)
            {
                lua_enemy.script = script;
                lua_enemy.CacheFunctions();
                foreach (string func_name in lua_enemy.func_cache.Keys)
                {
                    EnemyPlugin.Logger.LogInfo(String.Format("Cached lua function: {0}", func_name));
                }
            }

            lua_enemy.PostInit();

            __result = lua_enemy;
            //UnityEngine.Object.Destroy(orig);
        }
    }

    public static void CopyEnemy(RRLuaEnemy lua_enemy, RREnemy orig)
    {
        lua_enemy.original_enemy = orig;
        lua_enemy._animationComponent = orig._animationComponent;
        lua_enemy._scalePointTransform = orig._scalePointTransform;
        lua_enemy._spriteRenderer = orig._spriteRenderer;
        lua_enemy._statusFxParent = orig._statusFxParent;
        lua_enemy._heldItemParent = orig._heldItemParent;
        lua_enemy._monsterShadow = orig._monsterShadow;
        lua_enemy._onBeatShadowSprite = orig._onBeatShadowSprite;
        lua_enemy._halfBeatShadowSprite = orig._halfBeatShadowSprite;
        lua_enemy._otherBeatShadowSprite = orig._otherBeatShadowSprite;
        lua_enemy._defaultShadowColor = orig._defaultShadowColor;
        lua_enemy._vibePowerShadowColor = orig._vibePowerShadowColor;
        lua_enemy._defaultShadowShaderColor = orig._defaultShadowShaderColor;
        lua_enemy._vibePowerShadowShaderColor = orig._vibePowerShadowShaderColor;
        lua_enemy._resetAnimationClip = orig._resetAnimationClip;
        lua_enemy._numFramesToMoveTowardsActionRow = orig._numFramesToMoveTowardsActionRow;
        lua_enemy._shouldOverrideDefaultMoveCurve = orig._shouldOverrideDefaultMoveCurve;
        lua_enemy._enemyMovementCurve = orig._enemyMovementCurve;
        lua_enemy._movementAnimationData = orig._movementAnimationData;
        lua_enemy._shouldWrapAroundGrid = orig._shouldWrapAroundGrid;
        lua_enemy._percentageThroughMovementToWrapAroundGrid = orig._percentageThroughMovementToWrapAroundGrid;
        lua_enemy._percentageThroughMovementToPlayGridLeavingAnimation = orig._percentageThroughMovementToPlayGridLeavingAnimation;
        lua_enemy._leaveBoardOnLeftAnimationData = orig._leaveBoardOnLeftAnimationData;
        lua_enemy._leaveBoardOnRightAnimationData = orig._leaveBoardOnRightAnimationData;
        lua_enemy._enterBoardOnLeftAnimationData = orig._enterBoardOnLeftAnimationData;
        lua_enemy._enterBoardOnRightAnimationData = orig._enterBoardOnRightAnimationData;
        lua_enemy._simpleLeaveBoardOnLeftAnimationClip = orig._simpleLeaveBoardOnLeftAnimationClip;
        lua_enemy._simpleLeaveBoardOnRightAnimationClip = orig._simpleLeaveBoardOnRightAnimationClip;
        lua_enemy._simpleEnterBoardOnLeftAnimationClip = orig._simpleEnterBoardOnLeftAnimationClip;
        lua_enemy._simpleEnterBoardOnRightAnimationClip = orig._simpleEnterBoardOnRightAnimationClip;
        lua_enemy._shouldFlipEnemyOnBeat = orig._shouldFlipEnemyOnBeat;
        lua_enemy._rendererOffsetPairs = orig._rendererOffsetPairs;
        lua_enemy._basePositionOffset = orig._basePositionOffset;
        lua_enemy._zOffsetDistanceScaleCurve = orig._zOffsetDistanceScaleCurve;
        lua_enemy._deathAnimationData = orig._deathAnimationData;
        lua_enemy._attackAnimationData = orig._attackAnimationData;
        lua_enemy._deathDurationInBeats = orig._deathDurationInBeats;
        lua_enemy._hitMoveCurve = orig._hitMoveCurve;
        lua_enemy._hitMovementAnimationData = orig._hitMovementAnimationData;
        lua_enemy._healthIndicatorObjects = orig._healthIndicatorObjects;
        lua_enemy._attackSoundEventRef = orig._attackSoundEventRef;
        lua_enemy._hurtHitCryEventRef = orig._hurtHitCryEventRef;
        lua_enemy._deathHitCryEventRef = orig._deathHitCryEventRef;
        lua_enemy._defaultMatTintValue = orig._defaultMatTintValue;
        lua_enemy._defaultMatTintOverlayValue = orig._defaultMatTintOverlayValue;
        lua_enemy._burningFxPositionOffset = orig._burningFxPositionOffset;
        lua_enemy._burningFxScale = orig._burningFxScale;
        lua_enemy._portalInAnimationClip = orig._portalInAnimationClip;
        lua_enemy._portalOutAnimationClip = orig._portalOutAnimationClip;
        lua_enemy._portalInRightFacingAnimationClip = orig._portalInRightFacingAnimationClip;
        lua_enemy._portalOutRightFacingAnimationClip = orig._portalOutRightFacingAnimationClip;
        lua_enemy._statusFXDatas = orig._statusFXDatas;
        lua_enemy._enemiesToSpawnOnDeath = orig._enemiesToSpawnOnDeath;
        lua_enemy._enemyDefinition = new RREnemyDefinition(NextEnemyTrueId, orig._enemyDefinition);
        lua_enemy._enemyIdBacking = NextEnemyTrueId.ToString();
        lua_enemy._movementCurve = orig._movementCurve;
        lua_enemy._timeOfDeath = orig._timeOfDeath;
        lua_enemy._hasAnnouncedDeath = orig._hasAnnouncedDeath;
        lua_enemy._currentSpriteAnimData = orig._currentSpriteAnimData;
        lua_enemy._pushDirectionOverride = orig._pushDirectionOverride;
        lua_enemy._basePositionOffsetOverride = orig._basePositionOffsetOverride;
        lua_enemy._portalOutGridPosition = orig._portalOutGridPosition;
        lua_enemy._hasPerformedTeleportAnimation = orig._hasPerformedTeleportAnimation;
        lua_enemy._specialActionMoveCurve = orig._specialActionMoveCurve;
        lua_enemy._timeSpentInSpecialActionMove = orig._timeSpentInSpecialActionMove;
        lua_enemy._framesElapsedSnappingToActionRow = orig._framesElapsedSnappingToActionRow;
        lua_enemy._timeElapsedSnappingToActionRow = orig._timeElapsedSnappingToActionRow;
        lua_enemy._snapToActionRowMaxDuration = orig._snapToActionRowMaxDuration;
        lua_enemy._actionRowMoveTargetGridPosition = orig._actionRowMoveTargetGridPosition;
        lua_enemy._sortingOrderSlotIndex = orig._sortingOrderSlotIndex;
        lua_enemy._numUpdatesSinceSpawn = orig._numUpdatesSinceSpawn;
        lua_enemy._beatNumberOfLastBeatAction = orig._beatNumberOfLastBeatAction;
        lua_enemy._beatNumberOfNextBeatAction = orig._beatNumberOfNextBeatAction;
        lua_enemy._beatProgressOfLastBeatAction = orig._beatProgressOfLastBeatAction;
        lua_enemy._beatProgressOfNextBeatAction = orig._beatProgressOfNextBeatAction;
        lua_enemy._currentNumBeatSubdivisions = orig._currentNumBeatSubdivisions;
        lua_enemy._shouldResumeAnimationAfterTeleporting = orig._shouldResumeAnimationAfterTeleporting;
        lua_enemy._shouldResumeAnimationAfterWrappingAround = orig._shouldResumeAnimationAfterWrappingAround;
        lua_enemy._isHalfBeat = orig._isHalfBeat;
        lua_enemy._isOnBeat = orig._isOnBeat;
        lua_enemy._statusFXStartingAnimClipName = orig._statusFXStartingAnimClipName;
        lua_enemy._isPlayingStatusFxStartingAnimation = orig._isPlayingStatusFxStartingAnimation;
        lua_enemy._isColorBlindnessFilterActive = orig._isColorBlindnessFilterActive;
        lua_enemy._colorBlindnessFilterColor = orig._colorBlindnessFilterColor;
        lua_enemy._colorBlindnessFilterLevelsParam = orig._colorBlindnessFilterLevelsParam;
        lua_enemy._colorBlindnessFilterIgnoreShield = orig._colorBlindnessFilterIgnoreShield;
        lua_enemy._burningSpeedUpdateRateOverride = orig._burningSpeedUpdateRateOverride;
        lua_enemy._specialSoundEventRef = orig._specialSoundEventRef;
        lua_enemy._isWrappingAroundGrid = orig._isWrappingAroundGrid;
        lua_enemy._queuedTargetWorldPosition = orig._queuedTargetWorldPosition;
        lua_enemy._queuedOffGridWorldPosition = orig._queuedOffGridWorldPosition;
        lua_enemy._isFacingLeft = orig._isFacingLeft;
        lua_enemy._enemyMatPropBlock = orig._enemyMatPropBlock;
        lua_enemy._enemyShadowMatPropBlock = orig._enemyShadowMatPropBlock;
        lua_enemy._statusEffects = orig._statusEffects;
        lua_enemy._statusFxViews = orig._statusFxViews;
        lua_enemy._heldItemView = orig._heldItemView;
        lua_enemy._wrapAroundTeleportationPoint = orig._wrapAroundTeleportationPoint;
        lua_enemy._playGridLeavingAnimationPoint = orig._playGridLeavingAnimationPoint;
        lua_enemy._hasPerformedGridLeavingAnimation = orig._hasPerformedGridLeavingAnimation;
        lua_enemy._hasPerformedGridWrapAround = orig._hasPerformedGridWrapAround;
        lua_enemy._playedGridEnteringAnimationName = orig._playedGridEnteringAnimationName;
        lua_enemy._healthDuringLastBaneHit = orig._healthDuringLastBaneHit;
        lua_enemy._initialScaleValue = orig._initialScaleValue;
        lua_enemy._shouldDisableMovementAnimations = orig._shouldDisableMovementAnimations;

        lua_enemy._isVibePowerActive = orig._isVibePowerActive;
        lua_enemy._isPartOfVibeChain = orig._isPartOfVibeChain;
        lua_enemy._previouslyOnBeat = orig._previouslyOnBeat;
        lua_enemy._previouslyHalfBeat = orig._previouslyHalfBeat;
        lua_enemy._defaultSprite = orig._defaultSprite;
    }

}