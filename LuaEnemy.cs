using System;
using System.Collections.Generic;
using EnemyMod;
using FMODUnity;
using HarmonyLib;
using MoonSharp.Interpreter;
using RhythmRift;
using RhythmRift.Enemies;
using Shared.RhythmEngine;
using Unity.Mathematics;
using UnityEngine;

internal static class RRLuaEnemyPatches
{
    [HarmonyPatch(typeof(RREnemy), "UpdateAnimations")]
    [HarmonyPostfix]
    public static void UpdateAnimations(ref RREnemy __instance, FmodTimeCapsule fmodTimeCapsule)
    {
        if (__instance.GetType() == typeof(RRLuaEnemy))
        {
            (__instance as RRLuaEnemy).UpdateAnimationsLua(fmodTimeCapsule);
        }
    }

    [HarmonyPatch(typeof(RREnemy), "PlayAnimation")]
    [HarmonyPostfix]
    public static void PlayAnimation(ref RREnemy __instance, SpriteAnimationData spriteAnimationData, bool isMovementAnim, FmodTimeCapsule fmodTimeCapsule, float timeToStartAnim = 0f)
    {
        if (__instance.GetType() == typeof(RRLuaEnemy))
        {
            (__instance as RRLuaEnemy).PlayAnimationHook(spriteAnimationData, isMovementAnim, fmodTimeCapsule, timeToStartAnim);
        }
    }

    [HarmonyPatch(typeof(RREnemyController), "UpdateSystem")]
    [HarmonyPostfix]
    public static void UpdateSystem(FmodTimeCapsule fmodTimeCapsule)
    {
        for (int i = 0; i < RRStageControllerPatch.instance._obscureAnimators.Count; i++)
        {
            if (RRLuaEnemy.ObscureRowEnds[i] == -1) continue;
            if (fmodTimeCapsule.TrueBeatNumber > RRLuaEnemy.ObscureRowEnds[i])
            {
                EnemyPlugin.Logger.LogInfo(String.Format("Unobscuring row {0}", i));
                RRLuaEnemy.ObscureRowEnds[i] = -1;
                RRStageControllerPatch.instance._obscureAnimators[i].SetTrigger("GloomOff");
            }
        }
    }
}

class RRLuaEnemy : RREnemy
{
    public static void Log(string message)
    {
        EnemyPlugin.Logger.LogInfo(message);
    }

    public RREnemy original_enemy;
    public Script script;
    public string LuaCurrentAnimationName = "None";
    public float LuaCurrentAnimationProgress = 0.0f;
    public float LuaCurrentAnimationLength = 1.0f;
    public bool LuaCurrentAnimationLoops = true;
    public bool cached_functions = false;
    public Dictionary<string, DynValue> func_cache;
    public Dictionary<string, int2> state_int2_cache;
    public Dictionary<string, int> state_int_cache;
    public Dictionary<string, bool> state_bool_cache;
    public Dictionary<string, float> state_float_cache;
    public Dictionary<string, string> state_string_cache;
    public void CacheFunctions()
    {
        func_cache = new Dictionary<string, DynValue>();
        state_int2_cache = new Dictionary<string, int2>();
        state_int_cache = new Dictionary<string, int>();
        state_bool_cache = new Dictionary<string, bool>();
        state_float_cache = new Dictionary<string, float>();
        state_string_cache = new Dictionary<string, string>();
        Log(String.Format("Set initial state"));
        PlayLuaAnimation("NONE", 1.0f, true);
        //set constants
        set_state_int("home_row", RRGridView.HOME_ROW_COORD.y, true);
        set_state_int("STATUS_BURNING", (int)RREnemyStatusEffect.Burning, true);
        set_state_int("STATUS_MYSTERY", (int)RREnemyStatusEffect.Mysterious, true);

        //preset cache
        set_state_int2("current_grid_position", new int2(-1, -1), true);
        set_state_int2("target_grid_position", new int2(-1, -1), true);
        set_state_bool("is_being_pushed", false, true);
        set_state_int2("push_direction_override", new int2(-1, -1), true);

        set_state_bool("is_being_teleported", false, true);
        set_state_int2("portal_out_grid_position", new int2(-1, -1), true);

        set_state_int("num_updates_since_spawn", 0, true);

        set_state_int("current_health", -1, true);
        set_state_float("burning_speed", 0, true);

        set_state_bool("is_facing_left", false, true);

        set_state_string("current_anim_name", "none", true);
        set_state_float("current_anim_progress", -1, true);

        set_state_float("target_beat_number", -1, true);

        set_state_float("next_update_true_beat", -1, true);

        set_state_int2("left_arrow", new int2(-1, -1), true);
        set_state_int2("mid_arrow", new int2(-1, -1), true);
        set_state_int2("right_arrow", new int2(-1, -1), true);

        set_state_bool("is_performing_hit_movement", false, true);

        set_state_int("num_times_position_was_unavailable", -1, true);


        //add new functions
        Log(String.Format("Add c# callbacks"));
        script.Globals["log"] = (System.Object)Log;
        script.Globals["has_status_effect"] = (System.Object)HasStatusEffectActive;
        script.Globals["play_anim"] = (System.Object)PlayLuaAnimation;
        script.Globals["set_sprite"] = (System.Object)SetSprite;
        script.Globals["set_sprite_size"] = (System.Object)SetSpriteSize;
        script.Globals["set_sprite_position"] = (System.Object)SetSpritePosition;
        script.Globals["set_sprite_tint"] = (System.Object)SetSpriteTint;
        script.Globals["add_spawn"] = (System.Object)AddEnemySpawn;
        script.Globals["flip"] = (System.Object)FlipHorizontally;

        script.Globals["set_health"] = (System.Object)SetHealth;
        script.Globals["reset_status_effects"] = (System.Object)ResetStatusEffects;
        script.Globals["update_health_indicators"] = (System.Object)SetHealthIndicators;
        script.Globals["set_in_hit_window"] = (System.Object)SetInHitWindows;
        script.Globals["set_next_beat"] = (System.Object)SetNextBeat;
        script.Globals["set_target_grid_position"] = (System.Object)SetTargetGridPosition;
        script.Globals["arrive_at_target_position"] = (System.Object)ArriveAtTargetPositionLua;
        script.Globals["obscure_row"] = (System.Object)ObscureRow;
        script.Globals["camera_shake"] = (System.Object)CameraShake;
        script.Globals["rearrange_inputs"] = (System.Object)RearrangeInputs;

        script.Globals["update_def"] = (System.Object)UpdateDef;

        Log(String.Format("Cache user defined functions"));
        //cache functions
        CacheFunction("GetDesiredGridPosition");
        CacheFunction("PerformBeatActions");
        CacheFunction("ProcessIncomingAttack");
        CacheFunction("PerformTakeDamageBehaviour");
        CacheFunction("PerformDeathBehaviour");
        CacheFunction("CompleteHitMovement");
        CacheFunction("GetUpdateTempoInBeats");
        CacheFunction("PerformAttackPlayerBehaviour");
        CacheFunction("PerformCollisionResponse");
        CacheFunction("PerformCollisionResponse");
        CacheFunction("ShouldSpoofInputForEnemy");
        CacheFunction("UpdateAnimations");
        CacheFunction("ResumeAnimationAfterTeleporting");
        CacheFunction("ResumeAnimationAfterWrappingAroundGrid");
        CacheFunction("PlayDeathAnimation");
        CacheFunction("PlayAttackAnimation");
        CacheFunction("PlayAnimation");
        CacheFunction("NextActionRowTrueBeatNumber");
        CacheFunction("ExpectedFollowUpActionTrueBeatNumber");
        CacheFunction("MaxNumTimesToAttemptMovementResolution");
        CacheFunction("Init");
        cached_functions = true;
    }

    public void CacheFunction(string func_name)
    {
        DynValue dv = script.Globals.Get(func_name);
        if (dv.IsNil()) return;
        if (dv.Type != MoonSharp.Interpreter.DataType.Function) return;
        func_cache[func_name] = dv;
        Log(String.Format("Cached func: {0}", func_name));
    }

    public void set_state_int2(string key, int2 v, bool forced = false)
    {
        if (forced || !state_int2_cache[key].Equals(v))
        {
            state_int2_cache[key] = new int2(v);
            script.Globals[key] = int2_to_dynvalue(v);
        }
    }

    public void set_state_bool(string key, bool v, bool forced = false)
    {
        if (forced || !state_bool_cache[key].Equals(v))
        {
            state_bool_cache[key] = v;
            script.Globals[key] = DynValue.NewBoolean(v);
        }
    }

    public void set_state_int(string key, int v, bool forced = false)
    {
        if (forced || !state_int_cache[key].Equals(v))
        {
            state_int_cache[key] = v;
            script.Globals[key] = DynValue.NewNumber(v);
        }
    }
    public void set_state_string(string key, string v, bool forced = false)
    {
        if (forced || !state_string_cache[key].Equals(v))
        {
            state_string_cache[key] = v;
            script.Globals[key] = DynValue.NewString(v);
        }
    }
    public void set_state_float(string key, float v, bool forced = false)
    {
        if (forced || !state_float_cache[key].Equals(v))
        {
            state_float_cache[key] = v;
            script.Globals[key] = DynValue.NewNumber(v);
        }
    }

    public DynValue RunFunction(string func_name, object[] args)
    {
        if (!cached_functions) return DynValue.Nil;
        if (!func_cache.ContainsKey(func_name)) return DynValue.Nil;
        try
        {
            //Update state here
            set_state_int2("current_grid_position", CurrentGridPosition);
            set_state_bool("is_being_pushed", IsBeingPushed);
            set_state_int2("push_direction_override", _pushDirectionOverride);
            set_state_bool("is_being_teleported", IsBeingTeleported);
            set_state_int2("portal_out_grid_position", _portalOutGridPosition);
            set_state_int("num_updates_since_spawn", _numUpdatesSinceSpawn);
            set_state_int("current_health", CurrentHealthValue);
            set_state_float("burning_speed", _burningSpeedUpdateRateOverride);
            set_state_bool("is_facing_left", IsFacingLeft);
            set_state_string("current_anim_name", LuaCurrentAnimationName);
            set_state_float("current_anim_progress", LuaCurrentAnimationProgress);
            set_state_float("target_beat_number", TargetHitBeatNumber);
            set_state_int2("target_grid_position", TargetGridPosition);
            set_state_float("next_update_true_beat", NextUpdateTrueBeatNumber);
            set_state_int2("left_arrow", RRStageControllerPatch.instance._leftArrowGridPosition);
            set_state_int2("mid_arrow", RRStageControllerPatch.instance._midArrowGridPosition);
            set_state_int2("right_arrow", RRStageControllerPatch.instance._rightArrowGridPosition);
            set_state_bool("is_performing_hit_movement", IsPerformingHitMovement);
            set_state_int("num_times_position_was_unavailable", NumTimesDesiredPositionWasUnavailable);
            //Run function
            //Log(String.Format("Running function {0}", func_name));
            DynValue dv = script.Call(func_cache[func_name], args);
            return dv;
        }
        catch (MoonSharp.Interpreter.ScriptRuntimeException ex)
        {
            EnemyPlugin.Logger.LogError(String.Format("LUA ScriptRuntimeEx: {0}", ex.DecoratedMessage));
        }
        catch (MoonSharp.Interpreter.SyntaxErrorException ex)
        {
            EnemyPlugin.Logger.LogError(String.Format("LUA SyntaxErrorEx: {0}", ex.DecoratedMessage));
        }
        Log(String.Format("Attempted to run {0} but function wasn't cached", func_name));
        return DynValue.Nil;
    }

    //dynvalue helpers
    public DynValue int2_to_dynvalue(int2 v)
    {
        DynValue table = DynValue.NewTable(script);
        table.Table["x"] = v.x;
        table.Table["y"] = v.y;
        return table;
    }

    public int2? dynvalue_to_int2(DynValue dv)
    {
        if (dv.Type != DataType.Nil)
        {
            return new int2((int)dv.Tuple[0].Number, (int)(dv.Tuple[1].Number));
        }
        return null;
    }

    public bool? dynvalue_to_bool(DynValue dv)
    {
        if (dv.Type != DataType.Nil)
        {
            return dv.Boolean;
        }
        return null;
    }

    public float? dynvalue_to_float(DynValue dv)
    {
        if (dv.Type != DataType.Nil)
        {
            return (float)dv.Number;
        }
        return null;
    }

    public int? dynvalue_to_int(DynValue dv)
    {
        if (dv.Type != DataType.Nil)
        {
            return (int)dv.Number;
        }
        return null;
    }

    public DynValue fmodcapsule_to_table(FmodTimeCapsule fmodTimeCapsule)
    {
        DynValue table = DynValue.NewTable(script);
        table.Table["time"] = fmodTimeCapsule.Time;
        table.Table["delta_time"] = fmodTimeCapsule.DeltaTime;
        table.Table["true_beat_number"] = fmodTimeCapsule.TrueBeatNumber;
        table.Table["beat_length_seconds"] = fmodTimeCapsule.BeatLengthInSeconds;
        table.Table["current_beat_number"] = fmodTimeCapsule.CurrentBeatNumber;
        return table;
    }

    //hooks
    public override int CurrentHealthValue { get; set; }
    public override bool IsAllowedToIgnoreActionRowCollisions { get { return base.IsAllowedToIgnoreActionRowCollisions; } }
    public override bool ShouldBeIgnoredForActionRowCollisions { get { return base.ShouldBeIgnoredForActionRowCollisions; } }
    public override bool IsExpectingFollowUpAction { get { return base.IsExpectingFollowUpAction; } }
    public override bool ShouldPlayHitSoundInActionRow { get { return base.ShouldPlayHitSoundInActionRow; } }
    public override bool ShouldPlayAttackSoundInActionRow { get { return base.ShouldPlayAttackSoundInActionRow; } }
    public override int MaxHealthValue { get { return base.MaxHealthValue; } }
    public override int AttackDamage { get { return base.AttackDamage; } }
    public override int CollisionPriority { get { return base.CollisionPriority; } }
    public override float SpecialMoveActionDurationInBeats { get { return base.SpecialMoveActionDurationInBeats; } }
    public override bool CanBePushed { get { return base.CanBePushed; } }
    public override bool ShouldShowHitMovement { get { return base.ShouldShowHitMovement; } }
    public override bool ShouldSnapToActionRowOnDeath { get { return base.ShouldSnapToActionRowOnDeath; } }
    public override bool ShouldAddToKillCount { get { return base.ShouldAddToKillCount; } }
    public override void Awake()
    {
        base.Awake();
    }

    public override void UpdateState(FmodTimeCapsule fmodTimeCapsule)
    {
        base.UpdateState(fmodTimeCapsule);
    }

    public override void EvaluateBoard(HashSet<int2> enemyPositions, FmodTimeCapsule fmodTimeCapsule)
    {
        base.EvaluateBoard(enemyPositions, fmodTimeCapsule);
    }
    public override void UpdateMovement(FmodTimeCapsule fmodTimeCapsule)
    {
        base.UpdateMovement(fmodTimeCapsule);
    }
    public override void CheckBaneHitAttempt(FmodTimeCapsule fmodTimeCapsule)
    {
        base.CheckBaneHitAttempt(fmodTimeCapsule);
    }
    public override void ApplySpriteSortingOrder()
    {
        base.ApplySpriteSortingOrder();
    }
    public override void SetTargetPosition(int2 targetGridPosition, Vector3 targetWorldPosition)
    {
        base.SetTargetPosition(targetGridPosition, targetWorldPosition);
    }
    public override int2 GetTargetPositionWorldGridOffset()
    {
        return base.GetTargetPositionWorldGridOffset();
    }
    public override bool CanBeHit()
    {
        return base.CanBeHit();
    }
    public override bool HasReachedHitWindowRow()
    {
        return base.HasReachedHitWindowRow();
    }
    public override bool HasStatusEffectActive(RREnemyStatusEffect statusEffect)
    {
        return base.HasStatusEffectActive(statusEffect);
    }
    public override Vector3 GetTargetWorldPositionOffset(int2 targetGridPosition)
    {
        return base.GetTargetWorldPositionOffset(targetGridPosition);
    }
    public override EventReference GetEnemyHitCryEventRef(bool isRequestingSubsequentHitCry, float targetBeatNumber)
    {
        return base.GetEnemyHitCryEventRef(isRequestingSubsequentHitCry, targetBeatNumber);
    }
    public override void PerformReleaseInputAction(FmodTimeCapsule fmodTimeCapsule, string inputReleaseName)
    {
        base.PerformReleaseInputAction(fmodTimeCapsule, inputReleaseName);
    }
    public override bool NotifyInputWindowOpen(FmodTimeCapsule fmodTimeCapsule)
    {
        if (IsHitWindowActive)
        {
            return true;
        }
        TargetHitBeatNumber = NextActionRowTrueBeatNumber;
        IsHitWindowActive = true;
        return false;
    }
    public override bool NotifyInputWindowClosed(FmodTimeCapsule fmodTimeCapsule)
    {
        return base.NotifyInputWindowClosed(fmodTimeCapsule);
    }
    public override void PerformHoldMissBehaviour(FmodTimeCapsule fmodTimeCapsule)
    {
        base.PerformHoldMissBehaviour(fmodTimeCapsule);
    }
    public override void DestroySelf(FmodTimeCapsule fmodTimeCapsule, bool shouldContributeScoreForRemainingHealth = false)
    {
        base.DestroySelf(fmodTimeCapsule, shouldContributeScoreForRemainingHealth);
    }
    public override void HandleSnapToActionRow(FmodTimeCapsule fmodTimeCapsule)
    {
        base.HandleSnapToActionRow(fmodTimeCapsule);
    }
    public override void ApplyStatusEffect(RREnemyStatusEffect effectType, FmodTimeCapsule fmodTimeCapsule, int durationInUpdates = 999, float burningSpeedOverride = 0f)
    {
        base.ApplyStatusEffect(effectType, fmodTimeCapsule, durationInUpdates, burningSpeedOverride);
    }
    public override float GetLastHoldBeatNumber()
    {
        return base.GetLastHoldBeatNumber();
    }
    public override bool ShouldQueueSounds()
    {
        return base.ShouldQueueSounds();
    }
    public override void FreezeMovementAnimation()
    {
        base.FreezeMovementAnimation();
    }
    public override void ResumeMovementAnimation(FmodTimeCapsule fmodTimeCapsule)
    {
        base.PerformCollisionResponse(fmodTimeCapsule);
    }
    public override int2 GetDesiredGridPosition()
    {
        int2? ret = dynvalue_to_int2(RunFunction("GetDesiredGridPosition", []));
        return ret ?? base.GetDesiredGridPosition();
    }
    public override void PerformBeatActions(FmodTimeCapsule fmodTimeCapsule)
    {
        base.PerformBeatActions(fmodTimeCapsule);
        RunFunction("PerformBeatActions", [fmodcapsule_to_table(fmodTimeCapsule)]);
    }
    public override bool ProcessIncomingAttack(FmodTimeCapsule fmodTimeCapsule, int attackDamage)
    {
        bool? ret = dynvalue_to_bool(RunFunction("ProcessIncomingAttack", [fmodcapsule_to_table(fmodTimeCapsule), attackDamage]));
        bool should_run_orig = ret ?? true;
        if (should_run_orig)
            return base.ProcessIncomingAttack(fmodTimeCapsule, attackDamage);
        return false;
    }
    public override void PerformTakeDamageBehaviour(FmodTimeCapsule fmodTimeCapsule)
    {
        bool? ret = dynvalue_to_bool(RunFunction("PerformTakeDamageBehaviour", [fmodcapsule_to_table(fmodTimeCapsule)]));
        bool should_run_orig = ret ?? true;
        if (should_run_orig)
            base.PerformTakeDamageBehaviour(fmodTimeCapsule);
    }
    public override void PerformDeathBehaviour(FmodTimeCapsule fmodTimeCapsule, bool diedFromPlayerDamage = false)
    {
        bool? ret = dynvalue_to_bool(RunFunction("PerformDeathBehaviour", [fmodcapsule_to_table(fmodTimeCapsule), diedFromPlayerDamage]));
        bool should_run_orig = ret ?? true;
        if (should_run_orig)
            base.PerformDeathBehaviour(fmodTimeCapsule, diedFromPlayerDamage);
    }

    public override void CompleteHitMovement(FmodTimeCapsule fmodTimeCapsule)
    {
        base.CompleteHitMovement(fmodTimeCapsule);
        RunFunction("CompleteHitMovement", [fmodcapsule_to_table(fmodTimeCapsule)]);
    }
    public override float GetUpdateTempoInBeats(bool shouldApplyBurnSpeedUp = true)
    {
        float? ret = dynvalue_to_float(RunFunction("GetUpdateTempoInBeats", [shouldApplyBurnSpeedUp]));
        return ret ?? base.GetUpdateTempoInBeats(shouldApplyBurnSpeedUp);
    }
    public override void PerformAttackPlayerBehaviour(FmodTimeCapsule fmodTimeCapsule)
    {
        RunFunction("PerformAttackPlayerBehaviour", [fmodcapsule_to_table(fmodTimeCapsule)]);
        base.PerformAttackPlayerBehaviour(fmodTimeCapsule);
    }
    public override void PerformCollisionResponse(FmodTimeCapsule fmodTimeCapsule, bool shouldForceDestruction = false)
    {
        RunFunction("PerformCollisionResponse", [fmodcapsule_to_table(fmodTimeCapsule), shouldForceDestruction]);
        base.PerformCollisionResponse(fmodTimeCapsule, shouldForceDestruction);
    }
    public override bool ShouldSpoofInputForEnemy()
    {
        bool? ret = dynvalue_to_bool(RunFunction("ShouldSpoofInputForEnemy", []));
        return ret ?? base.ShouldSpoofInputForEnemy();
    }
    public override float NextActionRowTrueBeatNumber
    {
        get
        {
            float? ret = dynvalue_to_float(RunFunction("NextActionRowTrueBeatNumber", []));
            return ret ?? base.NextActionRowTrueBeatNumber;
        }
    }

    public override float ExpectedFollowUpActionTrueBeatNumber
    {
        get
        {
            float? ret = dynvalue_to_float(RunFunction("ExpectedFollowUpActionTrueBeatNumber", []));
            return ret ?? base.ExpectedFollowUpActionTrueBeatNumber;
        }
    }
    public override void Initialize(RREnemyInitializationData enemyInitializationData, AnimationCurve defaultMovementCurve, FmodTimeCapsule fmodTimeCapsule, bool shouldDisableMovementAnimations)
    {
        base.Initialize(enemyInitializationData, defaultMovementCurve, fmodTimeCapsule, shouldDisableMovementAnimations);
        _shouldFlipEnemyOnBeat = false;
        if (enemyInitializationData.ShouldStartFacingRight) //original enemies flips, then you flip again unflipping yourself, so you need to flip a third time
        {
            FlipHorizontally();
        }
    }


    //Animations hooks
    public override void PlayAttackAnimation(FmodTimeCapsule fmodTimeCapsule)
    {
        RunFunction("PlayAttackAnimation", []);
        base.PlayAttackAnimation(fmodTimeCapsule);
    }
    public override void PlayDeathAnimation(FmodTimeCapsule fmodTimeCapsule)
    {
        RunFunction("PlayDeathAnimation", []);
        base.PlayDeathAnimation(fmodTimeCapsule);
    }
    public override void ResumeAnimationAfterTeleporting(FmodTimeCapsule fmodTimeCapsule)
    {
        RunFunction("ResumeAnimationAfterTeleporting", []);
        base.ResumeAnimationAfterTeleporting(fmodTimeCapsule);
    }
    public override void ResumeAnimationAfterWrappingAroundGrid(FmodTimeCapsule fmodTimeCapsule)
    {
        RunFunction("ResumeAnimationAfterWrappingAroundGrid", []);
        base.ResumeAnimationAfterWrappingAroundGrid(fmodTimeCapsule);
    }
    public override int MaxNumTimesToAttemptMovementResolution
    {
        get
        {
            int? ret = dynvalue_to_int(RunFunction("MaxNumTimesToAttemptMovementResolution", []));
            return ret ?? base.MaxNumTimesToAttemptMovementResolution;
        }
    }

    //un vanilla hooks
    public void PlayAnimationHook(SpriteAnimationData spriteAnimationData, bool isMovementAnim, FmodTimeCapsule fmodTimeCapsule, float timeToStartAnim = 0f)
    {
        string anim_name = spriteAnimationData.AnimClipName;
        RunFunction("PlayAnimation", [anim_name, fmodcapsule_to_table(fmodTimeCapsule)]);
    }
    public void PostInit()
    {
        RunFunction("Init", []);
    }
    //Added functions
    public void UpdateDef(DynValue dv)
    {
        if (dv.Type != MoonSharp.Interpreter.DataType.Table) return;
        Table table = dv.Table;
        foreach (TablePair tablePair in table.Pairs)
        {
            switch (tablePair.Key.String)
            {
                case "player_damage":
                    _enemyDefinition._playerDamage = (int)tablePair.Value.Number;
                    break;
                case "is_health_item":
                    if ((bool)tablePair.Value.Boolean)
                    {
                        _enemyDefinition._specialProperties |= EnemySpecialProperties.IsHealthItem;
                    }
                    else
                    {
                        _enemyDefinition._specialProperties &= ~EnemySpecialProperties.IsHealthItem;
                    }
                    break;
                case "is_trap_immune":
                    if ((bool)tablePair.Value.Boolean)
                    {
                        _enemyDefinition._specialProperties |= EnemySpecialProperties.IsImmuneToTraps;
                    }
                    else
                    {
                        _enemyDefinition._specialProperties &= ~EnemySpecialProperties.IsImmuneToTraps;
                    }
                    break;
                case "display_name":
                    _enemyDefinition._displayName = tablePair.Value.String;
                    break;
                case "id":
                    _enemyDefinition._id = (int)tablePair.Value.Number;
                    break;
                case "update_tempo":
                    _enemyDefinition._updateTempoInBeats = (float)tablePair.Value.Number;
                    break;
            }
        }
    }
    public void PlayLuaAnimation(string name, float length, bool loops)
    {
        LuaCurrentAnimationName = name;
        LuaCurrentAnimationLength = length;
        LuaCurrentAnimationLoops = loops;
        LuaCurrentAnimationProgress = 0.0f;
    }

    public void AddEnemySpawn(int id, int rel_x, int rel_y)
    {
        SpawnEnemyOnDeathData spawnEnemyOnDeathData = default(SpawnEnemyOnDeathData);
        spawnEnemyOnDeathData.EnemyId = id;
        spawnEnemyOnDeathData.RelativeGridPostionToSpawnAt = new int2(rel_x, rel_y);
        _enemiesToSpawnOnDeath.Add(spawnEnemyOnDeathData);
    }

    public void SetHealth(int amount)
    {
        CurrentHealthValue = amount;
    }
    public void SetHealthIndicators(int amount)
    {
        UpdateHealthIndicatorStatus(amount);
    }
    public void SetInHitWindows(bool flag)
    {
        IsHitWindowActive = flag;
    }
    FmodTimeCapsule cached_time_capsule = new FmodTimeCapsule();
    public void SetNextBeat(float true_beat)
    {
        int a = (int)true_beat;
        float b = true_beat - a;
        UpdateBeatActionValues(a, b, cached_time_capsule.BeatDivisions);
    }
    public void SetTargetGridPosition(int x, int y)
    {
        TargetGridPosition = new int2(x, y);
    }
    public void ArriveAtTargetPositionLua()
    {
        ArriveAtTargetPosition();
    }
    public static float[] ObscureRowEnds = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 }; //probably enough
    public void ObscureRow(int row, float duration)
    {
        if (row < 0 || row >= RRStageControllerPatch.instance._obscureAnimators.Count) return;
        float end = cached_time_capsule.TrueBeatNumber + duration;
        if (end > ObscureRowEnds[row])
        {
            ObscureRowEnds[row] = end;
        }
        RRStageControllerPatch.instance._obscureAnimators[row].SetTrigger("GloomOn");
    }

    public void RearrangeInputs(int lx, int ly, int ux, int uy, int rx, int ry)
    {
        RRStageController rrsc = RRStageControllerPatch.instance;
        int columns = rrsc._gridView.NumColumns;
        /*
        if (lx > columns) lx -= columns;
        if (lx < columns) lx += columns;
        if (ux > columns) ux -= columns;
        if (ux < columns) ux += columns;
        if (rx > columns) rx -= columns;
        if (rx < columns) rx += columns;
        */
        rrsc._leftArrowGridPosition = new int2(lx, ly);
        rrsc._midArrowGridPosition = new int2(ux, uy);
        rrsc._rightArrowGridPosition = new int2(rx, ry);

        rrsc._gridView._arrows[0].transform.localPosition = rrsc._gridView._tileViewsByGridPosition[new int2(lx, ly)].transform.localPosition;
        rrsc._gridView._arrows[1].transform.localPosition = rrsc._gridView._tileViewsByGridPosition[new int2(ux, ry)].transform.localPosition;
        rrsc._gridView._arrows[2].transform.localPosition = rrsc._gridView._tileViewsByGridPosition[new int2(rx, ry)].transform.localPosition;
    }

    public void CameraShake(float amount)
    {
        RRStageControllerPatch.instance._cameraController.AddCameraTrauma(amount);
    }

    public void SetSprite(string sprite_name)
    {
        if (!LuaManager.Sprites.ContainsKey(sprite_name)) return;
        _spriteRenderer.sprite = LuaManager.Sprites[sprite_name];
    }

    public void SetSpriteSize(float x, float y)
    {
        _spriteRenderer.transform.localScale = new Vector2(x, y);
    }

    public void SetSpritePosition(float x, float y)
    {
        _spriteRenderer.transform.localPosition = new Vector2(x, y);
    }

    public void SetSpriteTint(float r, float g, float b, float a, float v)
    {
        //This is buggy due to weird shallow material sharing
        bool flag = false;
        Color color = _enemyMatPropBlock.GetColor(TintShaderPropertyId);
        if (Mathf.Abs(color.r - r) > 0.0001f || Mathf.Abs(color.g - g) > 0.0001f || Mathf.Abs(color.b - b) > 0.0001f || Mathf.Abs(color.a - a) > 0.0001f)
        {
            _enemyMatPropBlock.SetColor(TintShaderPropertyId, new Color(r, g, b, a));
            flag = true;
        }

        if (Mathf.Abs(_enemyMatPropBlock.GetFloat(TintOverlayShaderPropertyId) - v) > 0.0001f)
        {
            _enemyMatPropBlock.SetFloat(TintOverlayShaderPropertyId, v);
            flag = true;
        }

        if (flag) _spriteRenderer.SetPropertyBlock(_enemyMatPropBlock);
    }

    public void UpdateAnimationsLua(FmodTimeCapsule fmodTimeCapsule)
    {
        //_animationComponent.Stop();
        cached_time_capsule = fmodTimeCapsule;

        float rel_beats = fmodTimeCapsule.BeatLengthInSeconds * LuaCurrentAnimationLength;

        if (!_currentSpriteAnimData.ShouldIgnoreEnemyTempo) rel_beats *= GetUpdateTempoInBeats();

        float inv_length = LuaCurrentAnimationLength / rel_beats;
        float step = fmodTimeCapsule.DeltaTime * inv_length;

        if (float.IsNaN(LuaCurrentAnimationProgress) || float.IsInfinity(LuaCurrentAnimationProgress) || float.IsNaN(step) || float.IsInfinity(step))
        {
            LuaCurrentAnimationProgress = 0f;
            return;
        }

        LuaCurrentAnimationProgress += step;

        if (LuaCurrentAnimationProgress > LuaCurrentAnimationLength)
        { //Animation finished
            if (LuaCurrentAnimationLoops)
            {
                LuaCurrentAnimationProgress -= LuaCurrentAnimationLength;
            }
            else
            {
                LuaCurrentAnimationProgress = 0;
                RunFunction("AnimationFinished", [fmodcapsule_to_table(fmodTimeCapsule)]);
            }
        }

        RunFunction("UpdateAnimations", [fmodcapsule_to_table(fmodTimeCapsule)]);
    }
}
