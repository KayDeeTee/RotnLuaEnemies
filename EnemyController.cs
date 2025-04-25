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
using Shared.RhythmEngine;
using Shared.SceneLoading.Payloads;
using TicToc.ObjectPooling.Runtime;
using Unity.Mathematics;
using UnityEngine;

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
            spawnEnemyData.EnemyId = LuaManager.LuaEnemyRemaps[spawnEnemyData.EnemyId];
            original_ids[groupId] = orig;
            NextEnemyIsLua = true;
        }
        return true;
    }

}