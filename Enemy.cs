using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MoonSharp.Interpreter;
using RhythmRift;
using RhythmRift.Enemies;
using Shared.RhythmEngine;
using Shared.SceneLoading.Payloads;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace EnemyMod;

/*
RRArmadilloEnemy
    GetDesiredGridPosition: Hooked
    lots of other stuff
RRBatEnemy
    GetDesiredGridPosition: Hooked
    CompleteHitMovement: Unhooked
RRBlademasterEnemy
    GetDesiredGridPosition: Hooked
    lots of other stuff
RREnemyHarpy
    GetDesiredGridPosition: Hooked
    SpecialMoveActionDurationInBeats: Unhooked
RRHealthItem
    GetHealingSoundEventRef: Unhooked
    GetDesiredGridPosition: Hooked
    PerformTakeDamageBehaviour: Unhooked
RRSkeletonEnemy
    GetDesiredGridPosition: Hooked
    PerformCollisionResponse: Unhooked
    lots of other stuff
RRSkullEnemy
    GetDesiredGridPosition: Hooked
RRSlime
    GetDesiredGridPosition: Hooked
RRWyrmEnemy
    Yeah im not dealing with this rn
RRZombieEnemy
    GetDesiredGridPosition: Hooked
    PerformBeatActions: Hooked
*/

internal static class RREnemyPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RREnemy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRArmadilloEnemy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRBatEnemy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRBlademasterEnemy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RREnemyHarpy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRHealthItem), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRSkeletonEnemy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRSkeletonEnemy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRSlimeEnemy), "GetDesiredGridPosition")]
    [HarmonyPatch(typeof(RRZombieEnemy), "GetDesiredGridPosition")]
    public static bool DefaultGetDesiredGridPosition(ref RREnemy __instance, ref int2 __result)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            string guid = __instance.GroupId.ToString();
            bool facing = __instance.IsFacingLeft;
            int x = __instance.CurrentGridPosition.x;
            int y = __instance.CurrentGridPosition.y;
            bool hit = __instance.IsPerformingHitMovement;
            DynValue dv = LuaManager.RunFunction(lua_id, "get_desired_grid_position", [RREnemyControllerPatch.get_enemy_state(guid)]);
            if (dv.Type != DataType.Nil)
            {
                __result = new int2((int)dv.Tuple[0].Number, (int)(dv.Tuple[1].Number));
                return false;
            }
        }
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RREnemy), "PerformBeatActions")]
    [HarmonyPatch(typeof(RRZombieEnemy), "PerformBeatActions")]
    public static void PerformBeatActions(ref RREnemy __instance, FmodTimeCapsule fmodTimeCapsule)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            string guid = __instance.GroupId.ToString();
            LuaManager.RunFunction(lua_id, "on_beat_action", [RREnemyControllerPatch.get_enemy_state(guid)]);
        }
    }
    [HarmonyPatch(typeof(RREnemy), "NotifyInputWindowClosed")]
    [HarmonyPrefix]
    public static bool NotifyInputWindowClosed(ref RREnemy __instance, FmodTimeCapsule fmodTimeCapsule)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            string guid = __instance.GroupId.ToString();
            LuaManager.RunFunction(lua_id, "on_attack", [RREnemyControllerPatch.get_enemy_state(guid)]);
        }
        return true;
    }

    [HarmonyPatch(typeof(RREnemy), "ProcessIncomingAttack")]
    [HarmonyPostfix]
    public static void ProcessIncomingAttack(ref RREnemy __instance, FmodTimeCapsule fmodTimeCapsule, ref bool __result)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            string guid = __instance.GroupId.ToString();
            int current_hp = __instance.CurrentHealthValue;
            int x = __instance.CurrentGridPosition.x;
            int y = __instance.CurrentGridPosition.y;
            DynValue dv = LuaManager.RunFunction(lua_id, "get_hit_delay", [RREnemyControllerPatch.get_enemy_state(guid)]);
            if (dv.Type == DataType.Number)
            {
                __instance.IsPerformingHitMovement = true;
                __instance.IsHitWindowActive = false;
                __instance.ResetStatusEffects(true);
                __instance._timeSpentInSpecialActionMove = 0.0f;

                float hit_delay = (float)dv.Number;
                float new_beat = __instance.NextUpdateTrueBeatNumber + hit_delay;
                int beatNumber = (int)new_beat;
                float beatProgress = new_beat - beatNumber;

                __instance._beatNumberOfNextBeatAction = beatNumber;
                __instance._beatProgressOfNextBeatAction = beatProgress;

                //LuaManager.call_method_by_name(__instance, "UpdateBeatActionValues", [beatNumber, beatProgress, fmodTimeCapsule.BeatDivisions]);

                LuaManager.set_property_by_name(__instance, "TargetGridPosition", new int2(__instance.TargetGridPosition.x, RRGridView.HOME_ROW_COORD.y + 1));
                LuaManager.call_method_by_name(__instance, "ArriveAtTargetPosition", []);

                //__result = false;
            }
            DynValue dv2 = LuaManager.RunFunction(lua_id, "on_hit", [RREnemyControllerPatch.get_enemy_state(guid)]);

        }
    }

    [HarmonyPatch(typeof(RREnemy), "GetUpdateTempoInBeats")]
    [HarmonyPrefix]
    public static bool GetUpdateTempoInBeats(ref RREnemy __instance, ref float __result, bool shouldApplyBurnSpeedUp)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            string guid = __instance.GroupId.ToString();
            DynValue dv = LuaManager.RunFunction(lua_id, "get_move_delay", [RREnemyControllerPatch.get_enemy_state(guid)]);
            if (dv.Type == DataType.Number)
            {
                __result = (float)dv.Number;
                return false;
            }
        }
        return true;
    }

    [HarmonyPatch(typeof(RREnemy), "MaxNumTimesToAttemptMovementResolution", MethodType.Getter)]
    [HarmonyPostfix]
    public static void CollisionAttempts(ref RREnemy __instance, ref int __result)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            string guid = __instance.GroupId.ToString();
            DynValue dv = LuaManager.RunFunction(lua_id, "get_max_collisions", [RREnemyControllerPatch.get_enemy_state(guid)]);
            if (dv.Type == DataType.Number)
            {
                __result = (int)dv.Number;
            }
        }
    }


    [HarmonyPostfix]
    [HarmonyPatch(typeof(RREnemy), "UpdateAnimations")]
    public static void UpdateAnimations(ref RREnemy __instance, FmodTimeCapsule fmodTimeCapsule, ref SpriteRenderer ____spriteRenderer)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            string guid = __instance.GroupId.ToString();

            var enemy_ref = RREnemyControllerPatch.get_enemy_ref(guid);

            float speed = fmodTimeCapsule.BeatLengthInSeconds * enemy_ref.anim_length;
            enemy_ref.anim_progress += fmodTimeCapsule.DeltaTime / speed;
            if (enemy_ref.anim_progress >= 1.0)
            {
                //anim over
                if (enemy_ref.anim_loops)
                {
                    enemy_ref.anim_progress %= 1;
                }
                DynValue dv2 = LuaManager.RunFunction(lua_id, "anim_finished", [RREnemyControllerPatch.get_enemy_state(guid)]);
            }

            DynValue dv = LuaManager.RunFunction(lua_id, "get_sprite_override", [RREnemyControllerPatch.get_enemy_state(guid)]);
            if (dv.Type == DataType.String)
            {
                ____spriteRenderer.sprite = LuaManager.Sprites[dv.String];
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RREnemy), "Initialize")]
    //[HarmonyPatch(typeof(RRSkullEnemy), "Initialize")]
    public static void DefaultOnInit(ref RREnemy __instance)
    {
        OnInit(ref __instance);
    }

    public static void OnInit(ref RREnemy __instance)
    {
        if (RREnemyControllerPatch.original_ids.ContainsKey(__instance.GroupId))
        {
            string guid = __instance.GroupId.ToString();
            int lua_id = RREnemyControllerPatch.original_ids[__instance.GroupId];
            bool facing = __instance.IsFacingLeft;
            RREnemyControllerPatch.add_enemy(__instance);
            LuaManager.RunFunction(lua_id, "on_entity_create", [RREnemyControllerPatch.get_enemy_state(guid)]);

            DynValue dv = LuaManager.RunFunction(lua_id, "override_init_definition", []);

            MoonSharp.Interpreter.Table table = dv.Table;
            foreach (var pair in table.Pairs)
            {
                switch (pair.Key.String)
                {
                    case "id":
                        __instance._enemyDefinition._id = (int)pair.Value.Number;
                        break;
                    case "display_name":
                        __instance._enemyDefinition._displayName = pair.Value.String;
                        break;
                    case "max_health":
                        __instance._enemyDefinition._maxHealth = (int)pair.Value.Number;
                        __instance.CurrentHealthValue = __instance.MaxHealthValue;
                        break;
                    case "total_hits":
                        __instance._enemyDefinition._totalHitsAddedToStage = (int)pair.Value.Number;
                        break;
                    case "total_enemies_gen":
                        __instance._enemyDefinition._totalEnemiesGenerated = (int)pair.Value.Number;
                        break;
                    case "player_damage":
                        __instance._enemyDefinition._playerDamage = (int)pair.Value.Number;
                        break;
                    case "hp_on_death":
                        __instance._enemyDefinition._hpAwardedOnDeath = (int)pair.Value.Number;
                        break;
                    case "update_tempo":
                        __instance._enemyDefinition._updateTempoInBeats = (float)pair.Value.Number;
                        break; ;
                    case "is_health_item":
                        if ((bool)pair.Value.Boolean)
                        {
                            __instance._enemyDefinition._specialProperties |= EnemySpecialProperties.IsHealthItem;
                        }
                        else
                        {
                            __instance._enemyDefinition._specialProperties &= ~EnemySpecialProperties.IsHealthItem;
                        }
                        break;
                    case "is_immune_traps":
                        if ((bool)pair.Value.Boolean)
                        {
                            __instance._enemyDefinition._specialProperties |= EnemySpecialProperties.IsImmuneToTraps;
                        }
                        else
                        {
                            __instance._enemyDefinition._specialProperties &= ~EnemySpecialProperties.IsImmuneToTraps;
                        }
                        break;
                    case "wrap_grid":
                        __instance._shouldWrapAroundGrid = (bool)pair.Value.Boolean;
                        break;
                    default:
                        EnemyPlugin.Logger.LogInfo(pair.Key.String);
                        break;
                }
            }
        }
    }
}