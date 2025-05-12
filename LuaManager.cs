#nullable enable

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
    public static Dictionary<int, string> EnemyLuaPath = new Dictionary<int, string>();

    public static Script? PlayerPortraitLua = null;
    public static Script? CounterpartPortraitLua = null;

    public static void Reset()
    {
        //foreach (Script lua in GlobalLua)
        //{
        //theres no function to clean up the lua state so I guess it's just auto GC'd
        //}
        EnemyLua = new Dictionary<int, Script>();
        EnemyLuaPath = new Dictionary<int, string>();
        LuaEnemyRemaps = new Dictionary<int, int>();
        PlayerPortraitLua = null;
        CounterpartPortraitLua = null;

        foreach (string key in Sprites.Keys)
        {
            UnityEngine.Object.Destroy(Sprites[key]);
        }
        Sprites = new Dictionary<string, Sprite>();
    }

    public static int? GetInt(Script lua_state, string var_name)
    {
        DynValue dynValue = lua_state.Globals.Get(var_name);
        if (dynValue.IsNil()) return null;
        if (dynValue.Type != DataType.Number) return null;
        return (int)dynValue.Number;
    }

    public static bool? GetBool(Script lua_state, string var_name)
    {
        DynValue dynValue = lua_state.Globals.Get(var_name);
        if (dynValue.IsNil()) return null;
        if (dynValue.Type != DataType.Boolean) return null;
        return (bool)dynValue.Boolean;
    }

    public static Script GetScriptInstance(int id)
    {
        if (EnemyLuaPath.ContainsKey(id))
        {
            Script lua = new Script(MoonSharp.Interpreter.CoreModules.Preset_HardSandbox);
            lua.Options.ScriptLoader = new FileSystemScriptLoader();
            lua.DoFile(EnemyLuaPath[id]);
            return lua;
        }
        return null;
    }

    public static void LoadFile(string path)
    {
        Log(String.Format("Attempting to load lua at {0}", path));

        Script lua = new Script(MoonSharp.Interpreter.CoreModules.Preset_HardSandbox);
        lua.Options.ScriptLoader = new FileSystemScriptLoader();
        //Create Vars / Functions
        lua.Globals["log"] = (System.Object)Log;
        lua.Globals["load_texture"] = (System.Object)LoadTexture;

        try
        {
            lua.DoFile(path);

            bool? player_override = GetBool(lua, "player_portrait_override");
            if (player_override is bool player)
            {
                PlayerPortraitLua = lua;
                return;
            }

            bool? counterpart_override = GetBool(lua, "counterpart_portrait_override");
            if (counterpart_override is bool counterpart)
            {
                CounterpartPortraitLua = lua;
                return;
            }

            int? lua_id = GetInt(lua, "lua_id");
            if (lua_id is int l_id)
            {
                int? original_id = GetInt(lua, "original_id");
                if (original_id is int o_id)
                {
                    Log(String.Format("Found enemy remap {0} -> {1}", l_id, o_id));
                    LuaEnemyRemaps[l_id] = o_id;
                    EnemyLua[l_id] = lua;
                    EnemyLuaPath[l_id] = path;


                    DynValue dv = lua.Globals.Get("Preloads");
                    if (!dv.IsNil())
                    {
                        if (dv.Type == MoonSharp.Interpreter.DataType.Function)
                        {
                            dv.Function.Call();
                        }
                    }
                }
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

    public static void set_property_by_name(object instance, string name, object value)
    {
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
        FieldInfo property = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
        {
            property.SetValue(instance, Convert.ChangeType(value, property.FieldType));
        }
        else
        {
            property = t.BaseType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            property.SetValue(instance, Convert.ChangeType(value, property.FieldType));
        }
    }

    public static object get_var_by_name(object instance, string name)
    {
        FieldInfo property = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property.GetValue(instance);
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
        RRStageControllerPatch.instance.HandleEnemyAttack(enemy.CurrentGridPosition, amount, 0, enemy.DisplayName, false, false, enemy.EnemyTypeId);
    }

    private static void CreateSound(string path)
    {
        /*
        MODE mode = MODE._2D | MODE.CREATESTREAM | MODE.ACCURATETIME | MODE.NONBLOCKING;
        CREATESOUNDEXINFO createsoundexinfo = new CREATESOUNDEXINFO
        {
            initialseekposition = 0.0f,
            initialseekpostype = TIMEUNIT.MS,
            cbsize = MarshalHelper.SizeOf(typeof(CREATESOUNDEXINFO))
        };
        Sound customSound;
        RuntimeManager.CoreSystem.createSound(path, mode, ref createsoundexinfo, out customSound);

        customSound.release();
        customSound.clearHandle();
        */
    }
}
