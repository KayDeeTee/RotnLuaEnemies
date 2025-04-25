using System;
using System.Reflection;
using EnemyMod;
using RhythmRift.Enemies;
using Shared.RhythmEngine;

class RRLuaEnemy : RREnemy
{
    public override void UpdateState(FmodTimeCapsule fmodTimeCapsule)
    {
        EnemyPlugin.Logger.LogInfo("RRLUA Update State");
        base.UpdateState(fmodTimeCapsule);
    }
}