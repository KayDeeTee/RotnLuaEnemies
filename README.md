# RotnLuaEnemies
Adds lua bindings for basic custom enemies

## Installation
First install bepinex and ensure that works then, download EnemyPlugin.zip from releases and put EnemyPlugin folder with all 3 dlls into RiftOfTheNecroDancer/BepInEx/plugins

## Usage
### Setting an enemy to be custom
You need to manually edit the json currently to add `{"_eventDataKey":"CustomEnemyId","_eventDataValue":"<your enemy id>"}` to every event you want to override (it should go next to EnemyId) like so `{"_eventDataKey":"EnemyId","_eventDataValue":"1234"},{"_eventDataKey":"CustomEnemyId","_eventDataValue":"11422"}`, this will spawn a zombie (1234) if you don't have the mod otherwise it'll load your custom enemy 11422

### Lua 
Each enemy has a single lua script that controls all instances, and you get functions called with a proxy state for the enemy that needs updating.
- original_id determines the backing your custom enemy uses, this is the fallback behaviour, audio and non-sprite animations (scale / translation)
- lua_id is what you put in CustomEnemyId, if theres a collision I don't know what will happen probably a race condition.

#### C# callbacks
Use these to modify your entity.
- load_texture( key, path, pixels_per_unit, xpivot, ypivot) => Loads a texture at path.
- add_spawn( guid, enemy_id, rel_x, rel_y, facing ) => Adds an ondeath spawn like skulls, facing is currently unused but will likely at some point affect the facing of the spawned entities.
- set_beat_flip( guid, flag ) => Sets if the enemy flips every beat like a slime.
- set_health( guid, hp ) => Sets the enemies current health.
- play_anim( guid, anim_name, loops, duration_in_beats ) => Begins playing an animation, you can check the current animation and progress in get_sprite_override and return the correct sprite
- heal_player( guid, amount ) => Acts as if you just hit a food item, heals amount.
- hit_player( guid, amount ) => Acts as if you just missed this note, deals amount damage.
- flip( guid ) => Causes enemy to flip horizontally.

#### Hooked methods
These automatically get called by c#
- override_init_definition() => expects you to return a table, lets you override the enemyDefinition for this enemy.
- on_load() => called once when the chart starts, preload assets here.
- on_entity_create(state) => called when a new instance of the custom enemy is created.
- get_max_collisions(state) => how many times to request another position, vanilla this is 0 except for zombies which are 2.
- on_beat_action(state) => called whenever you arrive at a beat.
- get_desired_grid_position(state) => expects you to return x, y where x/y are the grid positions you want to move to next. You don't have to worry about wrapping, for collisions check purplezombie.lua.
- anim_finished(state) => called when a non-looping animation finishes
- get_sprite_override(state) => expects you to return a string that corresponds to a key you loaded a texure with via load_texture()
- get_move_delay(state) => expects you to return a float, how long it takes you to complete a single move from get_desired_grid_position()
- get_hit_delay(state) => expects you to return a float, move delay gets applied too when you get hit so for values smaller than move delay you need to return a negative value. i.e. shield skeletons are -0.5 and blue armadillos are -0.66

#### State
Most hooked methods send state info about the current entity that you can use to decide what to do, check `get_enemy_state` in `EnemyController.cs` to see what it currently stores.

#### Storing your own state
Your script for each enemy type stays loaded for the entire chart so you can store state in a table, check purplezombie.lua and max_col_counts for an example.

### Known Limitations
- You only have access to exactly what you are given, so unless you can do exactly what you want via the given callbacks it's likely impossible (for now this is stuff like pushing other entities, or messing with user inputs)
- You need at least 1 of your original_id in your chart due to the game preloading assets based on what enemies exist in the chart.
- Held enemies like wyrms are low on priority.
- Bandages are based on the original_id and won't scale correctly.
