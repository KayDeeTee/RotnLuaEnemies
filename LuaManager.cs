using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Reflection;
using FMODUnity;
using Mono.Cecil.Cil;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop.LuaStateInterop;
using MoonSharp.Interpreter.Loaders;
using RhythmRift;
using RhythmRift.Enemies;
using Shared;
using Shared.RhythmEngine;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace EnemyMod;

public static class LuaManager
{
    public static Dictionary<string, DynValue> LuaFuncCache = new Dictionary<string, DynValue>();
    public static Dictionary<int, int> LuaEnemyRemaps = new Dictionary<int, int>();
    public static Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
    public static Dictionary<int, Script> EnemyLua = new Dictionary<int, Script>();
    public static Script[] GlobalLua;
    public static void Reset()
    {
        //foreach (Script lua in GlobalLua)
        //{
        //theres no function to clean up the lua state so I guess it's just auto GC'd
        //}
        GlobalLua = [];
        EnemyLua = new Dictionary<int, Script>();
        LuaEnemyRemaps = new Dictionary<int, int>();


        foreach (string key in Sprites.Keys)
        {
            UnityEngine.Object.Destroy(Sprites[key]);
        }
        Sprites = new Dictionary<string, Sprite>();
    }

    public static void SetCurrentInstance(int lua_id, object instance)
    {
        if (!LuaManager.EnemyLua.ContainsKey(lua_id)) return;
        Script lua = LuaManager.EnemyLua[lua_id];
        lua.Globals["instance"] = instance; 
    }

    public static DynValue RunFunction(int lua_id, string func_name, object[] args)
    {
        if (!LuaManager.EnemyLua.ContainsKey(lua_id)) return DynValue.Nil;
        Script lua = LuaManager.EnemyLua[lua_id];
        DynValue f = lua.Globals.Get(func_name);
        if (f.IsNil()) return DynValue.Nil; ;
        if (f.Type != MoonSharp.Interpreter.DataType.Function) return DynValue.Nil;
        try
        {
            DynValue dv = lua.Call(f, args);
            return dv;
        }
        catch (MoonSharp.Interpreter.ScriptRuntimeException ex)
        {
            Log(String.Format("LUA ScriptRuntimeEx: {0}", ex.DecoratedMessage));
        }
        catch (MoonSharp.Interpreter.SyntaxErrorException ex)
        {
            Log(String.Format("LUA SyntaxErrorEx: {0}", ex.DecoratedMessage));
        }
        return DynValue.Nil;
    }

    public static int? GetInt(Script lua_state, string var_name)
    {
        DynValue dynValue = lua_state.Globals.Get(var_name);
        if (dynValue.IsNil()) return null;
        if (dynValue.Type != DataType.Number) return null;
        return (int)dynValue.Number;
    }

    public static void LoadFile(string path)
    {
        Log(String.Format("Attempting to load lua at {0}", path));

        Script lua = new Script(MoonSharp.Interpreter.CoreModules.Preset_HardSandbox);
        lua.Options.ScriptLoader = new FileSystemScriptLoader();
        //Create Vars / Functions
        lua.Globals["HOME_ROW"] = RRGridView.HOME_ROW_COORD.y;
        lua.Globals["log"] = (System.Object)Log;
        lua.Globals["load_texture"] = (System.Object)LoadTexture;
        lua.Globals["add_spawn"] = (System.Object)AddSpawns;
        lua.Globals["set_beat_flip"] = (System.Object)SetBeatFlip;
        lua.Globals["set_health"] = (System.Object)SetHealth;
        lua.Globals["play_anim"] = (System.Object)PlayAnim;
        lua.Globals["heal_player"] = (System.Object)ForceHeal;
        lua.Globals["hit_player"] = (System.Object)ForceDamage;
        lua.Globals["flip"] = (System.Object)FlipHorizontally;

        try
        {
            lua.DoFile(path);

            Log(String.Format("Attempting to run on_load"));
            DynValue lua_on_load = lua.Globals.Get("on_load");
            if (lua_on_load.IsNotNil())
            {
                if (lua_on_load.Type == MoonSharp.Interpreter.DataType.Function)
                {
                    lua.Call(lua_on_load);
                }
            }

            bool global_lua_file = true;
            int? lua_id = GetInt(lua, "lua_id");
            if (lua_id is int l_id)
            {
                int? original_id = GetInt(lua, "original_id");
                if (original_id is int o_id)
                {
                    Log(String.Format("Found enemy remap {0} -> {1}", l_id, o_id));
                    LuaEnemyRemaps[l_id] = o_id;
                    EnemyLua[l_id] = lua;
                    global_lua_file = false;
                }
            }
            if (global_lua_file)
            {
                GlobalLua.Append(lua);
            }
        }
        catch (ScriptRuntimeException ex)
        {
            Log(String.Format("LUA ScriptRuntimeEx: {0}", ex.DecoratedMessage));
        }
        catch (SyntaxErrorException ex)
        {
            Log(String.Format("LUA SyntaxErrorEx: {0}", ex.DecoratedMessage));
        }
    }

    public static void Load(string[] paths)
    {
        foreach (string path in paths)
        {
            LoadFile(path);
        }
    }

    //Lua functions
    private static void Log(string message)
    {
        EnemyPlugin.Logger.LogInfo(message);
    }

    //maybe needs to be made async but I'm not sure on how easy it'll be
    //probably needs error checking too
    private static void LoadTexture(string id, string path, int ppu, float x_pivot, float y_pivot)
    {
        string AssetPath = Path.Combine(RRStageControllerPatch.LuaPath, path);
        if (File.Exists(AssetPath))
        {
            byte[] bytes = File.ReadAllBytes(AssetPath);
            Texture2D tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            tex.LoadImage(bytes);
            Rect rect = new Rect(0, 0, tex.width, tex.height);
            Vector2 pivot = new Vector2(x_pivot, y_pivot);
            Sprites[id] = Sprite.Create(tex, rect, pivot, ppu);
        }
    }

    public static void set_property_by_name( object instance, string name, object value ){
        Type t = instance.GetType();
        PropertyInfo property = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        property = property.DeclaringType.GetProperty(name);
        if (property != null)
        {
            property.SetValue(instance, value, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);  // If the setter might be public, add the BindingFlags.Public flag.
        }
        else
        {
            property = t.BaseType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            property.SetValue(instance, value, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, null, null); 
        }
    }

    public static object call_method_by_name(object instance, string name, object[] args)
    {
        MethodInfo mi = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return mi.Invoke(instance, args);
    }

    public static object get_property_by_name(object instance, string name)
    {
        PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property.GetValue(instance);
    }

    public static void set_var_by_name(object instance, string name, object value)
    {
        Type t = instance.GetType();
        FieldInfo property = t.GetField(name,  BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
        {
            property.SetValue(instance, Convert.ChangeType(value, property.FieldType));
        }
        else
        {
            property = t.BaseType.GetField(name, BindingFlags.Public |  BindingFlags.NonPublic | BindingFlags.Instance);
            property.SetValue(instance, Convert.ChangeType(value, property.FieldType));
        }
    }

    public static object get_var_by_name( object instance, string name ){
        FieldInfo property = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property.GetValue(instance);
    }

    //Putting this in EnemyPatch seemed to upset it so its going here
    private static void SetBeatFlip(string guid, bool setting)
    {
        RREnemyControllerPatch.get_enemy(guid)._shouldFlipEnemyOnBeat = setting;
        //set_var_by_name(RREnemyControllerPatch.get_enemy(guid), "_shouldFlipEnemyOnBeat", setting);
    }

    private static void SetHealth(string guid, int health)
    {
        RREnemyControllerPatch.get_enemy(guid).CurrentHealthValue = health;
        //set_property_by_name(RREnemyControllerPatch.get_enemy(guid), "CurrentHealthValue", health);
    }

    private static void ForceHeal(string guid, int amount)
    {
        RREnemy enemy = RREnemyControllerPatch.get_enemy(guid);
        Vector3 startPos = enemy.CurrentGridWorldPosition;
        float beat = enemy.NextActionRowTrueBeatNumber;
        EventReference healingSoundEventRef = default(EventReference);
        RRStageControllerPatch.instance.HandleHealthItemHit(amount, startPos, beat, healingSoundEventRef);
    }

    private static void ForceDamage(string guid, int amount)
    {
        RREnemy enemy = RREnemyControllerPatch.get_enemy(guid);
        RRStageControllerPatch.instance.HandleEnemyAttack(enemy.CurrentGridPosition, amount, 0,  enemy.DisplayName, false, false, enemy.EnemyTypeId);
    }

    private static void FlipHorizontally(string guid)
    {
        RREnemy enemy = RREnemyControllerPatch.get_enemy(guid);
        enemy.FlipHorizontally();
    }

    private static void PlayAnim(string guid, string anim_name, bool loops, float beat_duration)
    {
        var enemy_ref = RREnemyControllerPatch.get_enemy_ref(guid);
        enemy_ref.anim_progress = 0.0f;
        enemy_ref.anim_loops = loops;
        enemy_ref.current_anim = anim_name;
        enemy_ref.anim_length = beat_duration;
    }

    private static void AddSpawns(string guid, int id, int rel_x, int rel_y, bool facing)
    {
        SpawnEnemyOnDeathData spawnEnemyOnDeathData = default(SpawnEnemyOnDeathData);
        spawnEnemyOnDeathData.EnemyId = id;
        spawnEnemyOnDeathData.RelativeGridPostionToSpawnAt = new int2(rel_x, rel_y);
        RREnemyControllerPatch.get_enemy(guid).EnemiesToSpawnOnDeath.Add(spawnEnemyOnDeathData);
    }

}