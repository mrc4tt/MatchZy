using System;
using System.Collections.Generic;
using System.IO;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public class ConfigManager
    {
        // Lazy: avoids calling native Server.GameDirectory in the ctor, which CSS
        // may construct before the engine pointer is ready (would throw → load abort).
        // Resolves to an existing case-variant dir (e.g. "MatchZy") if one is present,
        // so we don't create a duplicate lowercase "matchzy" on case-sensitive Linux fs.
        private string ServerPath => ResolveConfigDir(Path.Combine(Server.GameDirectory, "csgo", "cfg"), "matchzy");

        // Public accessor so the exec path in MatchZy.Load() uses the SAME case-resolved
        // dir the config files were created in. Hardcoding lowercase "matchzy" for the
        // File.Exists/execifexists broke on case-sensitive Linux when the on-disk dir was
        // e.g. "MatchZy" → config.cfg was never exec'd → user cvars (demo_path, etc.) ignored.
        public string GetMatchZyCfgDir() => ServerPath;

        private static string ResolveConfigDir(string parent, string name)
        {
            try
            {
                if (Directory.Exists(parent))
                {
                    foreach (var dir in Directory.GetDirectories(parent))
                    {
                        if (string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase))
                            return dir;
                    }
                }
            }
            catch { /* fall through to default path */ }
            return Path.Combine(parent, name);
        }

        private void CreateConfigFile(string fileName, string content)
        {
            try
            {
                string filePath = Path.Combine(ServerPath, fileName);
                if (!Directory.Exists(ServerPath))
                {
                    Directory.CreateDirectory(ServerPath);
                }
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, content.TrimStart());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating {fileName}: {ex.Message}");
            }
        }

        public void InitializeConfigs()
        {
            var configs = new Dictionary<string, string>
            {
                [ConfigFiles.Paths.Config] =
                    @"
// Whether warmup mode is enabled. Default value: true
// If set to false, warmup.cfg will not be loaded/executed when entering warmup phases.
matchzy_warmup_enabled true

// Whether whitelist is enabled by default or not. Default value: false
// This is the default value, but whitelist can be toggled by admin using .whitelist command
matchzy_whitelist_enabled_default false

// Whether knife round is enabled by default or not. Default value: true
// This is the default value, but knife can be toggled by admin using .knife command
matchzy_knife_enabled_default true

// Minimum ready players required to start the match. If set to 0, all connected players have to ready-up to start the match. Default: 10
matchzy_minimum_ready_required 10

// Path of folder in which demos will be saved. If defined, it must not start with a slash and must end with a slash. Set to empty string to use the csgo root.
matchzy_demo_path demos/

// Demo Name Formatting
matchzy_demo_name_format ""{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}""

// Whether !stop/.stop command is enabled by default or not. Default value: true
// Note: We are using Valve backup system to record and restore the backups. In most of the cases, this should be just fine.
// But in some cases, this may not be reliable hence default value is false
matchzy_stop_command_available true

// Whether to use !pause/.pause command for tactical pause or normal pause (unpauses only when both teams use unpause command, for admin force-unpauses the game)
// Default value: false
matchzy_use_pause_command_for_tactical_pause false

// Whether to keep .tech command enabled or not
// Default value: true
matchzy_enable_tech_pause true

// Tech pause duration in seconds. Set -1 to keep it infinite.
// Default value: 300
matchzy_tech_pause_duration 300

// Max tech pauses allowed.
// Default value: 2
matchzy_max_tech_pauses_allowed 2

// Whether to automatically pause when a team has fewer than minimum players
// Default value: true
matchzy_autopause_enabled true

// Minimum players required per team before auto-pause triggers
// Default value: 5 (set to 2 for wingman, 4 for relaxed scrims)
matchzy_autopause_minplayers 5

// Delay in seconds before auto-resuming when teams are balanced
// Default value: 3
matchzy_autopause_resume_delay 3

// Set Custom Teamname for CT
// Set to """" to disable/use default
matchzy_ct_name """"

// Set Custom Teamname for T.
// Set to """" to disable/use default
matchzy_t_name """"

// Whether to pause the match after round restore or not. Default value: true
// Players/admins can unpause the match using !unpause/.unpause. (For players, both the teams will have to use unpause command)
matchzy_pause_after_restore true

// Chat prefix to show whenever a MatchZy message is sent to players. Default value: [{Green}MatchZy{Default}]
// Available Colors: {Default}, {Darkred}, {Green}, {LightYellow}, {LightBlue}, {Olive}, {Lime}, {Red}, {Purple}, {Grey}, {Yellow}, {Gold}, {Silver}, {Blue}, {DarkBlue}
// {BlueGrey}, {Magenta} and {LightRed}. Make sure to end your prefix with {Default} to avoid coloring the messages in your prefix color.
matchzy_chat_prefix {Green}[MatchZy]{Default}

// Chat prefix to show whenever an admin sends message using .asay <message>. Default value: [{Red}ADMIN{Default}]
// Avaiable Colors are mentioned above
matchzy_admin_chat_prefix [{Red}ADMIN{Default}]

// Number of seconds of delay before sending reminder messages from MatchZy (like unready message, paused message, etc).
// Default: 13 (Because each message is kept in chat for ~13 seconds)
// Note: Changing this timer wont affect the active timer, so if you change this setting in warmup, you will have to restart warmup to make the change effective
matchzy_chat_messages_timer_delay 21

// Whether playout (play max rounds) is enabled. Default value: false
// This is the default value, but playout can be toggled by admin using .scrim or .playout command
matchzy_playout_enabled_default false

// Whether to kick all clients and prevent anyone from joining the server if no match is loaded. Default value: false
// This means if server is in match mode, a match needs to be set-up using matchzy_loadmatch/matchzy_loadmatch_url to load and configure a match
// Only players in that match will be able to join the server, else they will be kicked
matchzy_kick_when_no_match_loaded false

// Whether parameters from the cvars section of a match configuration are restored to their original values when a series ends.
// Default: true
matchzy_reset_cvars_on_series_end true

// Whether the plugin will load the match mode, the practice mode or neither by startup. 
// 0 for neither, 1 for match mode, 2 for practice mode. Default: 1
matchzy_autostart_mode 1

// Whether nades should be saved globally instead of being privated to players by default or not. Default value: false
matchzy_save_nades_as_global_enabled false

// Whether force ready using !forceready is enabled or not (Currently works in Match Setup only). Default value: True
matchzy_allow_force_ready true

// Maximum number of grenade history that may be saved per-map, per-client. Set to 0 to disable. Default value: 512
matchzy_max_saved_last_grenades 512

// Whether player-specific smoke color is enabled or not. Default: false
matchzy_smoke_color_enabled false

// Coach viewing-spot source: 1 = use a spawns/coach/<map>.json spot if present (hand-tuned via
// .savecoachspawn) else compute it, 2 = always compute behind the team (ignore JSON). Default: 1
matchzy_coaching_mode 1

// If set to true, all the players will have admin privilege. Default: false
matchzy_everyone_is_admin false

// The server hostname to use. Set to """" to disable/use existing.
// Example matchzy_hostname_format ""MatchZy | {TEAM1} vs {TEAM2}""
matchzy_hostname_format """"

// Whether to show damage report after each round or not. Default: true.
matchzy_enable_damage_report true

// Ready-status display mode. 0 = classic center text. 1 = HTML READY-UP panel (progress bar, per-player status) with the native warmup suppressed: own WARMUP badge, no native 'Warmup' pill, frozen 1:00 top timer. Default: 0
matchzy_ready_hint_style 0

// Blink the 'YOU ARE NOT READY' line on the HTML READY-UP panel to grab attention. ONLY applies when matchzy_ready_hint_style is 1 (the HTML panel); it has no effect in classic style (0). Default: false
matchzy_ready_hint_blink false

// Whether to show [READY] / [UNREADY] clan tags on the scoreboard during the ready phase. Default: true
matchzy_ready_clantag_enabled true

// Whether pinging (middle-mouse / scroll button) toggles your ready status during warmup. Set false if players ready up by accident when pinging. Default: true
matchzy_ready_up_by_ping true

// Message to show when the match starts. Use $$$ to break message into multiple lines. Set to """" to disable.
// Available variables: {TIME}, {MATCH_ID}, {MAP}, {MAPNUMBER}, {TEAM1}, {TEAM2}, {TEAM1_SCORE}, {TEAM2_SCORE}
// Available Colors: {Default}, {Darkred}, {Green}, {LightYellow}, {LightBlue}, {Olive}, {Lime}, {Red}, {Purple}, {Grey}, {Yellow}, {Gold}, {Silver}, {Blue}, {DarkBlue}
// Example: {Green} Welcome to the server! {Default} $$$ Agent models are not allowed and may lead to {Red}disqualification!{Default}
matchzy_match_start_message """"

// Whether to automatically change map after match end. Disable this for G5API/tournament matches.
// Default value: 1 (enabled)
// Set to 0 for G5API Tournament Servers (Get5 will handle map rotation)
// Note: If using G5API, this is automatically detected and disabled, but you can override here if needed
matchzy_match_end_auto_changelevel 1

// Whether MatchZy registers the css_map console command (!map). Default value: true
// MatchZy auto-yields it: if a dedicated map plugin (CS2-SimpleAdmin / CS2MapChange) is installed
// alongside, css_map is NOT registered even when this is true, avoiding a ConCommand conflict that
// can block players from connecting. Set to false to never register it.
// The .map chat command stays available regardless of this setting.
matchzy_map_console_command_enabled true
",

                [ConfigFiles.Paths.Dryrun] =
                    @"
ammo_grenade_limit_default 1
ammo_grenade_limit_flashbang 2
ammo_grenade_limit_total 4
bot_quota 0
cash_player_bomb_defused 300
cash_player_bomb_planted 300
cash_player_damage_hostage -30
cash_player_interact_with_hostage 300
cash_player_killed_enemy_default 300
cash_player_killed_enemy_factor 1
cash_player_killed_hostage -1000
cash_player_killed_teammate -300
cash_player_rescued_hostage 1000
cash_team_elimination_bomb_map 3250
cash_team_elimination_hostage_map_ct 3000
cash_team_elimination_hostage_map_t 3000
cash_team_hostage_alive 0
cash_team_hostage_interaction 600
cash_team_loser_bonus 1400
cash_team_loser_bonus_consecutive_rounds 500
cash_team_planted_bomb_but_defused 600
cash_team_rescued_hostage 600
cash_team_terrorist_win_bomb 3500
cash_team_win_by_defusing_bomb 3500
cash_team_win_by_hostage_rescue 2900
cash_team_win_by_time_running_out_bomb 3250
cash_team_win_by_time_running_out_hostage 3250
ff_damage_reduction_bullets 0.33
ff_damage_reduction_grenade 0.85
ff_damage_reduction_grenade_self 1
ff_damage_reduction_other 0.4
mp_afterroundmoney 0
mp_autokick 0
mp_autoteambalance 0
mp_backup_restore_load_autopause 1
mp_backup_round_auto 1
mp_buy_anywhere 0
mp_buy_during_immunity 0
mp_buytime 15
mp_c4timer 40
mp_ct_default_melee weapon_knife
mp_ct_default_secondary weapon_hkp2000
mp_ct_default_primary """"
mp_death_drop_defuser 1
mp_death_drop_grenade 2
mp_death_drop_gun 1
mp_defuser_allocation 0
mp_display_kill_assists 1
mp_endmatch_votenextmap 0
mp_forcecamera 1
mp_free_armor 0
mp_freezetime 6
mp_friendlyfire 1
mp_give_player_c4 1
mp_halftime 1
mp_halftime_duration 15
mp_halftime_pausetimer 0
mp_ignore_round_win_conditions 0
mp_limitteams 0
mp_match_can_clinch 1
mp_match_end_restart 0
mp_maxmoney 16000
mp_maxrounds 24
mp_overtime_enable 0
mp_overtime_halftime_pausetimer 0
mp_overtime_maxrounds 6
mp_overtime_startmoney 16000
mp_playercashawards 1
mp_randomspawn 0
mp_respawn_immunitytime 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_round_restart_delay 5
mp_roundtime 1.92
mp_roundtime_defuse 1.92
mp_roundtime_hostage 1.92
mp_solid_teammates 1
mp_starting_losses 1
mp_startmoney 16000
mp_t_default_melee weapon_knife
mp_t_default_secondary weapon_glock
mp_t_default_primary """"
mp_teamcashawards 1
mp_timelimit 0
mp_weapons_allow_map_placed 1
mp_weapons_allow_zeus 1
mp_win_panel_display_time 5
spec_freeze_deathanim_time 0
spec_freeze_time 2
spec_freeze_time_lock 2
spec_replay_enable 0
sv_allow_votes 0
sv_auto_full_alltalk_during_warmup_half_end 1
sv_deadtalk 1
sv_hibernate_postgame_delay 300
sv_ignoregrenaderadio 0
sv_infinite_ammo 0
sv_talk_enemy_dead 0
sv_talk_enemy_living 0
sv_voiceenable 1
mp_team_timeout_max 4
mp_team_timeout_time 30
sv_vote_command_delay 0
cash_team_bonus_shorthanded 1000
mp_spectators_max 10
mp_team_intro_time 0
mp_disconnect_kills_players 0
sv_hide_roundtime_until_seconds 0
sv_showimpacts 0
mp_warmup_end
",

                [ConfigFiles.Paths.Knife] =
                    @"
mp_team_intro_time 0
mp_ct_default_secondary """"
mp_free_armor 1
mp_freezetime 12
mp_give_player_c4 0
mp_maxmoney 0
mp_respawn_immunitytime 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_roundtime 3
mp_roundtime_defuse 3
mp_t_default_secondary """"
mp_round_restart_delay 3
sv_deadtalk 0
mp_solid_teammates 1
mp_spectators_max 10
sv_hide_roundtime_until_seconds 0
",

                [ConfigFiles.Paths.Hill] =
                    @"
mp_ct_default_primary """"
mp_ct_default_secondary ""weapon_hkp2000""
mp_t_default_primary """"
mp_t_default_secondary ""weapon_glock""
mp_t_default_melee weapon_knife
mp_ct_default_melee weapon_knife
cash_player_bomb_defused 300
cash_player_bomb_planted 300
cash_player_damage_hostage -30
cash_player_interact_with_hostage 150
cash_player_killed_enemy_default 300
cash_player_killed_enemy_factor 1
cash_player_killed_hostage -1000
cash_player_killed_teammate -300
cash_player_rescued_hostage 1000
cash_team_elimination_bomb_map 3250
cash_team_hostage_alive 150
cash_team_hostage_interaction 150
cash_team_loser_bonus 1400
cash_team_loser_bonus_consecutive_rounds 500
cash_team_planted_bomb_but_defused 600
cash_team_rescued_hostage 750
cash_team_terrorist_win_bomb 3500
cash_team_win_by_defusing_bomb 3500
cash_team_win_by_hostage_rescue 3500
cash_player_get_killed 0
cash_player_respawn_amount 0
cash_team_bonus_shorthanded 0
cash_team_elimination_hostage_map_ct 2000
cash_team_elimination_hostage_map_t 1000
cash_team_win_by_time_running_out_bomb 3250
cash_team_win_by_time_running_out_hostage 3250
ff_damage_reduction_grenade 0.85
ff_damage_reduction_bullets 0.33            
ff_damage_reduction_other 0.4                   
ff_damage_reduction_grenade_self 1             
mp_afterroundmoney 16000				
mp_autokick 0				
mp_buytime 15                      
mp_c4timer 40                          
mp_death_drop_defuser 1			
mp_death_drop_grenade 2			
mp_death_drop_gun 1			
mp_defuser_allocation 0			
mp_forcecamera 1                    
mp_force_pick_time 160				
mp_free_armor 0				
mp_freezetime 17                        
mp_friendlyfire 1 
mp_halftime 1
mp_halftime_duration 10	
mp_logdetail 3
mp_match_can_clinch 0		
mp_match_end_restart false
mp_match_end_changelevel true
mp_match_restart_delay 25
mp_maxmoney 16000			
mp_maxrounds 24                  
mp_playercashawards 1			
mp_playerid 0				
mp_playerid_delay 0.5			
mp_playerid_hold 0.25				
mp_round_restart_delay 5
mp_roundtime 1.92
mp_roundtime_defuse 1.92                
mp_solid_teammates 1
mp_startmoney 16000                 
mp_teamcashawards 1				
mp_timelimit 0
mp_tkpunish 0				
mp_weapons_allow_map_placed 1
mp_weapons_allow_zeus 1				
mp_win_panel_display_time 10
spec_freeze_time 2.0
spec_freeze_time_lock 2
spec_freeze_deathanim_time 0
sv_accelerate 5.5
sv_stopspeed 80				
sv_cheats 0
sv_deadtalk 0					
sv_friction 5.2					
sv_full_alltalk 0				
sv_gameinstructor_disable 1
sv_ignoregrenaderadio 0                         
sv_kick_players_with_cooldown 0                 
sv_kick_ban_duration 0                          
sv_lan 0                                       
sv_competitive_minspec 1                       
sv_pausable 0                                   
sv_spawn_afk_bomb_drop_time 30
sv_steamgroup_exclusive 0
sv_voiceenable 1 
mp_starting_losses 1
mp_teammates_are_enemies 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_respawnwavetime_ct 10
mp_respawnwavetime_t 10
sv_infinite_ammo 0
mp_team_timeout_time 31
mp_team_timeout_max 3
mp_technical_timeout_per_team 1
mp_technical_timeout_duration_s 120
mp_overtime_enable false
mp_team_timeout_ot_add_once 0
mp_team_timeout_ot_add_each 0
mp_team_timeout_ot_max 0
mp_drop_grenade_enable 1
sv_showimpacts 0
sv_grenade_trajectory_prac_pipreview false
sv_grenade_trajectory_prac_trailtime 0
sv_grenade_trajectory_time_spectator 0
mp_give_player_c4 1
mp_warmup_end
mp_restartgame 1
mp_buy_anywhere 0
mp_team_intro_time 0
mp_ct_default_grenades """" 
mp_t_default_grenades """"
ammo_grenade_limit_default 1
ammo_grenade_limit_flashbang 2
ammo_grenade_limit_total 4
mp_spectators_max 10
sv_hide_roundtime_until_seconds 0
",

                [ConfigFiles.Paths.Live] =
                    @"
ammo_grenade_limit_default 1
ammo_grenade_limit_flashbang 2
ammo_grenade_limit_total 4
bot_quota 0
cash_player_bomb_defused 300
cash_player_bomb_planted 300
cash_player_damage_hostage -30
cash_player_interact_with_hostage 300
cash_player_killed_enemy_default 300
cash_player_killed_enemy_factor 1
cash_player_killed_hostage -1000
cash_player_killed_teammate -300
cash_player_rescued_hostage 1000
cash_team_elimination_bomb_map 3250
cash_team_elimination_hostage_map_ct 3000
cash_team_elimination_hostage_map_t 3000
cash_team_hostage_alive 0
cash_team_hostage_interaction 600
cash_team_loser_bonus 1400
cash_team_loser_bonus_consecutive_rounds 500
cash_team_planted_bomb_but_defused 600
cash_team_rescued_hostage 600
cash_team_terrorist_win_bomb 3500
cash_team_win_by_defusing_bomb 3500
cash_team_win_by_hostage_rescue 2900
cash_team_win_by_time_running_out_bomb 3250
cash_team_win_by_time_running_out_hostage 3250
ff_damage_reduction_bullets 0.33
ff_damage_reduction_grenade 0.85
ff_damage_reduction_grenade_self 1
ff_damage_reduction_other 0.4
mp_afterroundmoney 0
mp_autokick 0
mp_autoteambalance 0
mp_backup_restore_load_autopause 1
mp_backup_round_auto 1
mp_buy_anywhere 0
mp_buy_during_immunity 0
mp_buytime 15
mp_c4timer 40
mp_ct_default_melee weapon_knife
mp_ct_default_primary """"
mp_ct_default_secondary weapon_hkp2000
mp_death_drop_defuser 1
mp_death_drop_grenade 2
mp_death_drop_gun 1
mp_defuser_allocation 0
mp_display_kill_assists 1
mp_endmatch_votenextmap 0
mp_forcecamera 1
mp_free_armor 0
mp_freezetime 18
mp_friendlyfire 1
mp_give_player_c4 1
mp_halftime 1
mp_halftime_duration 15
mp_halftime_pausetimer 0
mp_ignore_round_win_conditions 0
mp_limitteams 0
mp_match_can_clinch 1
mp_match_end_restart false
mp_match_end_changelevel true
mp_match_restart_delay 25
mp_maxmoney 16000
mp_maxrounds 24
mp_overtime_enable 1
mp_overtime_halftime_pausetimer 0
mp_overtime_maxrounds 6
mp_overtime_startmoney 10000
mp_playercashawards 1
mp_randomspawn 0
mp_respawn_immunitytime 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_round_restart_delay 5
mp_roundtime 1.92
mp_roundtime_defuse 1.92
mp_roundtime_hostage 1.92
mp_solid_teammates 1
mp_starting_losses 1
mp_startmoney 800
mp_t_default_melee weapon_knife
mp_t_default_primary """"
mp_t_default_secondary weapon_glock
mp_teamcashawards 1
mp_timelimit 0
mp_weapons_allow_map_placed 1
mp_weapons_allow_zeus 1
mp_win_panel_display_time 10
spec_freeze_deathanim_time 0
spec_freeze_time 2
spec_freeze_time_lock 2
spec_replay_enable 0
sv_allow_votes 0
sv_auto_full_alltalk_during_warmup_half_end 0
sv_deadtalk 1
sv_hibernate_postgame_delay 300
sv_ignoregrenaderadio 0
sv_infinite_ammo 0
sv_talk_enemy_dead 0
sv_talk_enemy_living 0
sv_voiceenable 1
mp_team_timeout_max 3
mp_team_timeout_time 30
sv_vote_command_delay 0
cash_team_bonus_shorthanded 0
mp_spectators_max 10
mp_team_intro_time 0
mp_team_timeout_ot_max 1
mp_team_timeout_ot_add_each 1
mp_weapons_allow_typecount 5
mp_warmup_end
sv_hide_roundtime_until_seconds 0
",

                [ConfigFiles.Paths.LiveWingman] =
                    @"
ammo_grenade_limit_default 1
ammo_grenade_limit_flashbang 2
ammo_grenade_limit_total 4
bot_quota 0
cash_player_bomb_defused 300
cash_player_bomb_planted 300
cash_player_damage_hostage -30
cash_player_interact_with_hostage 300
cash_player_killed_enemy_default 300
cash_player_killed_enemy_factor 1
cash_player_killed_hostage -1000
cash_player_killed_teammate -300
cash_player_rescued_hostage 1000
cash_team_bonus_shorthanded 1000
cash_team_elimination_bomb_map 3250
cash_team_elimination_hostage_map_t 3000
cash_team_elimination_hostage_map_ct 3000
cash_team_hostage_alive 0
cash_team_hostage_interaction 600
cash_team_loser_bonus 1400
cash_team_loser_bonus_consecutive_rounds 500
cash_team_planted_bomb_but_defused 600
cash_team_rescued_hostage 600
cash_team_terrorist_win_bomb 3500
cash_team_win_by_defusing_bomb 3500
cash_team_win_by_hostage_rescue 2900
cash_team_win_by_time_running_out_hostage 3250
cash_team_win_by_time_running_out_bomb 3250
ff_damage_reduction_bullets 0.33
ff_damage_reduction_grenade 0.85
ff_damage_reduction_grenade_self 1
ff_damage_reduction_other 0.4
mp_afterroundmoney 0
mp_autokick 0
mp_autoteambalance 0
mp_backup_restore_load_autopause 0
mp_backup_round_auto 1
mp_buy_anywhere 0
mp_buy_during_immunity 0
mp_buytime 15
mp_c4timer 40
mp_ct_default_melee weapon_knife
mp_ct_default_primary """"
mp_ct_default_secondary weapon_hkp2000
mp_death_drop_defuser 1
mp_death_drop_grenade 2
mp_death_drop_gun 1
mp_defuser_allocation 0
mp_display_kill_assists 1
mp_endmatch_votenextmap 0
mp_forcecamera 1
mp_free_armor 0
mp_freezetime 12
mp_friendlyfire 1
mp_give_player_c4 1
mp_halftime 1
mp_halftime_duration 15
mp_halftime_pausetimer 0
mp_ignore_round_win_conditions 0
mp_limitteams 0
mp_match_can_clinch 1
mp_match_end_restart false
mp_match_end_changelevel true
mp_maxmoney 8000
mp_maxrounds 16
mp_overtime_enable 1
mp_overtime_halftime_pausetimer 0
mp_overtime_maxrounds 4
mp_overtime_startmoney 8000
mp_playercashawards 1
mp_randomspawn 0
mp_respawn_immunitytime 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_round_restart_delay 5
mp_roundtime 1.5
mp_roundtime_defuse 1.5
mp_roundtime_hostage 1.5
mp_solid_teammates 1
mp_starting_losses 1
mp_startmoney 800
mp_t_default_melee weapon_knife
mp_t_default_primary """"
mp_t_default_secondary weapon_glock
mp_teamcashawards 1
mp_timelimit 0
mp_weapons_allow_map_placed 1
mp_weapons_allow_zeus 1
mp_win_panel_display_time 5
spec_freeze_deathanim_time 0
spec_freeze_time 2
spec_freeze_time_lock 2
spec_replay_enable 0
sv_allow_votes 0
sv_auto_full_alltalk_during_warmup_half_end 0
sv_deadtalk 1
sv_hibernate_postgame_delay 300
sv_ignoregrenaderadio 0
sv_infinite_ammo 0
sv_talk_enemy_dead 0
sv_talk_enemy_living 0
sv_voiceenable 1
mp_team_intro_time 0
mp_spectators_max 10
mp_match_restart_delay 25
sv_hide_roundtime_until_seconds 0
",

                [ConfigFiles.Paths.Practice] =
                    @"
mp_team_intro_time 0
mp_freezetime 0
bot_kick
bot_quota 0
mp_give_player_c4 0
sv_cheats                           ""true"" 
mp_force_pick_time                  ""0""         
bot_quota                           ""0""        
sv_showimpacts                      ""1""         
mp_limitteams                       ""0""       
sv_deadtalk                         ""true""     
sv_full_alltalk                     ""true""   
sv_ignoregrenaderadio               ""false""     
mp_forcecamera                      ""1""         
sv_grenade_trajectory_prac_pipreview ""true""
sv_grenade_trajectory_prac_trailtime ""3""
sv_infinite_ammo                    ""1""       
weapon_auto_cleanup_time            ""15""        
weapon_max_before_cleanup           ""30""    
mp_buy_anywhere                     ""1""         
mp_maxmoney                         ""9999999""   
mp_startmoney                       ""9999999""
mp_afterroundmoney                  ""9999999""
mp_weapons_allow_typecount          ""-1""
mp_death_drop_defuser               ""false""
mp_death_drop_taser                 ""false""
mp_drop_knife_enable                ""true""
mp_death_drop_grenade               ""0""
ammo_grenade_limit_total            ""5""
mp_defuser_allocation               ""0""
mp_free_armor                       ""2""
ammo_grenade_limit_flashbang ""1""
ammo_grenade_limit_default ""1""
mp_ct_default_grenades ""weapon_incgrenade weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"";
mp_ct_default_primary ""weapon_m4a1"";
mp_t_default_grenades ""weapon_molotov weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"";
mp_t_default_primary ""weapon_ak47"";
mp_warmup_end
mp_buytime 999999
mp_buy_allow_grenades 1
mp_respawn_on_death_ct true
mp_respawn_on_death_t true
bot_quota_mode normal
mp_solid_teammates 2
mp_autoteambalance false
mp_teammates_are_enemies false
mp_freezetime 0
mp_roundtime 60
mp_roundtime_defuse 60
buddha 1
buddha_ignore_bots 1
buddha_reset_hp 100
mp_give_player_c4 0
mp_death_drop_gun 0
mp_spectators_max 10
sv_hide_roundtime_until_seconds 0
mp_freezetime 0
",

                [ConfigFiles.Paths.Scrim] =
                    @"
mp_ct_default_primary """";
mp_ct_default_secondary ""weapon_hkp2000"";
mp_t_default_primary """";
mp_t_default_secondary ""weapon_glock"";
mp_t_default_melee weapon_knife
mp_ct_default_melee weapon_knife
cash_player_bomb_defused 300
cash_player_bomb_planted 300
cash_player_damage_hostage -30
cash_player_interact_with_hostage 150
cash_player_killed_enemy_default 300
cash_player_killed_enemy_factor 1
cash_player_killed_hostage -1000
cash_player_killed_teammate -300
cash_player_rescued_hostage 1000
cash_team_elimination_bomb_map 3250
cash_team_hostage_alive 150
cash_team_hostage_interaction 150
cash_team_loser_bonus 1400
cash_team_loser_bonus_consecutive_rounds 500
cash_team_planted_bomb_but_defused 600
cash_team_rescued_hostage 750
cash_team_terrorist_win_bomb 3500
cash_team_win_by_defusing_bomb 3500
cash_team_win_by_hostage_rescue 3500
cash_player_get_killed 0
cash_player_respawn_amount 0
cash_team_bonus_shorthanded 0
cash_team_elimination_hostage_map_ct 2000
cash_team_elimination_hostage_map_t 1000
cash_team_win_by_time_running_out_bomb 3250
cash_team_win_by_time_running_out_hostage 3250
ff_damage_reduction_grenade 0.85                // How much to reduce damage done to teammates by a thrown grenade.  Range is from 0 - 1 (with 1 being damage equal to what is done to an enemy)
ff_damage_reduction_bullets 0.33                // How much to reduce damage done to teammates when shot.  Range is from 0 - 1 (with 1 being damage equal to what is done to an enemy)
ff_damage_reduction_other 0.4                   // How much to reduce damage done to teammates by things other than bullets and grenades.  Range is from 0 - 1 (with 1 being damage equal to what is done to an enemy)
ff_damage_reduction_grenade_self 1              // How much to damage a player does to himself with his own grenade.  Range is from 0 - 1 (with 1 being damage equal to what is done to an enemy)
mp_afterroundmoney 0				// amount of money awared to every player after each round
mp_autokick 0					// Kick idle/team-killing players
mp_autoteambalance 0
mp_buytime 15                           	// How many seconds after round start players can buy items for.
mp_c4timer 40                           	// How long from when the C4 is armed until it blows
mp_death_drop_defuser 1				// Drop defuser on player death
mp_death_drop_grenade 2				// Which grenade to drop on player death: 0=none, 1=best, 2=current or best
mp_death_drop_gun 1				// Which gun to drop on player death: 0=none, 1=best, 2=current or best
mp_defuser_allocation 0				// How to allocate defusers to CTs at start or round: 0=none, 1=random, 2=everyone
mp_forcecamera 1                        	// Restricts spectator modes for dead players
mp_force_pick_time 160				// The amount of time a player has on the team screen to make a selection before being auto-teamed 
mp_free_armor 0					// Determines whether armor and helmet are given automatically.
mp_freezetime 17                        	// How many seconds to keep players frozen when the round starts
mp_friendlyfire 1                       	// Allows team members to injure other members of their team
mp_halftime 1					// Determines whether or not the match has a team-swapping halftime event.
mp_halftime_duration 10				// Number of seconds that halftime lasts
mp_limitteams 0                         	// Max # of players 1 team can have over another (0 disables check)
mp_logdetail 3                          	// Logs attacks.  Values are: 0=off, 1=enemy, 2=teammate, 3=both)
mp_match_can_clinch 0				// Can a team clinch and end the match by being so far ahead that the other team has no way to catching up
mp_match_end_restart false
mp_match_end_changelevel true
mp_match_restart_delay 25
mp_maxmoney 16000				// maximum amount of money allowed in a player's account
mp_maxrounds 24					// max number of rounds to play before server changes maps
mp_playercashawards 1				// Players can earn money by performing in-game actions
mp_playerid 0					// Controls what information player see in the status bar: 0 all names; 1 team names; 2 no names 
mp_playerid_delay 0.5				// Number of seconds to delay showing information in the status bar
mp_playerid_hold 0.25				// Number of seconds to keep showing old information in the status bar
mp_round_restart_delay 5			// Number of seconds to delay before restarting a round after a win
mp_roundtime 1.92                       	// How many minutes each round takes.
mp_roundtime_defuse 1.92                	// How many minutes each round takes on defusal maps.
mp_solid_teammates 1 				// Determines whether teammates are solid or not.
mp_startmoney 800                       	// amount of money each player gets when they reset
mp_teamcashawards 1				// Teams can earn money by performing in-game actions
mp_timelimit 0                           	// game time per map in minutes
mp_tkpunish 0					// Will a TK'er be punished in the next round?  {0=no,  1=yes}
mp_weapons_allow_map_placed 1             	// If this convar is set, when a match starts, the game will not delete weapons placed in the map.
mp_weapons_allow_zeus 1				// Determines whether the Zeus is purchasable or not.
mp_win_panel_display_time 3	                // The amount of time to show the win panel between matches / halfs
spec_freeze_time 2.0                            // Time spend frozen in observer freeze cam.
spec_freeze_time_lock 2
spec_freeze_deathanim_time 0
sv_accelerate 5.5
sv_stopspeed 80
sv_cheats 0                     // Allow cheats on server
sv_deadtalk 0					// Dead players can speak (voice, text) to the living
sv_friction 5.2					// World friction.
sv_full_alltalk 0				// Any player (including Spectator team) can speak to any other player
sv_gameinstructor_disable 1		// Force all clients to disable their game instructors.
sv_ignoregrenaderadio 0                         // Turn off Fire in the hole messages
sv_kick_players_with_cooldown 0                 // (0: do not kick; 1: kick Untrusted players; 2: kick players with any cooldown)
sv_kick_ban_duration 0                          // How long should a kick ban from the server should last (in minutes)
sv_lan 0                                        // Server is a lan server ( no heartbeat, no authentication, no non-class C addresses )
sv_competitive_minspec 1                        // Enable to force certain client convars to minimum/maximum values to help prevent competitive advantages.
sv_pausable 0                                   // Is the server pausable.
sv_spawn_afk_bomb_drop_time 30                 	// Players that spawn and don't move for longer than sv_spawn_afk_bomb_drop_time (default 15 seconds) will automatically drop the bomb.
sv_steamgroup_exclusive 0                     	// If set, only members of Steam group will be able to join the server when it's empty, public people will be able to join the server only if it has players.
sv_voiceenable 1 
mp_starting_losses 1
mp_teammates_are_enemies 0;
mp_respawn_on_death_ct 0;
mp_respawn_on_death_t 0;
mp_respawnwavetime_ct 10;
mp_respawnwavetime_t 10;
sv_infinite_ammo 0;
mp_team_timeout_time            31
mp_team_timeout_max            3
mp_technical_timeout_per_team						1
mp_technical_timeout_duration_s						120
mp_overtime_enable false
mp_team_timeout_ot_add_once 0
mp_team_timeout_ot_add_each 0
mp_team_timeout_ot_max 0
mp_drop_grenade_enable 1
sv_showimpacts 0
sv_grenade_trajectory_prac_pipreview false
sv_grenade_trajectory_prac_trailtime 0
sv_grenade_trajectory_time_spectator 0
mp_give_player_c4 1
mp_warmup_end
mp_restartgame 1
mp_buy_anywhere 0
mp_team_intro_time 0
mp_ct_default_grenades """" 
mp_t_default_grenades """"
ammo_grenade_limit_default 1
ammo_grenade_limit_flashbang 2
ammo_grenade_limit_total 4
mp_spectators_max 10
sv_hide_roundtime_until_seconds 0
",

                [ConfigFiles.Paths.Sleep] =
                    @"
ammo_grenade_limit_default 1
ammo_grenade_limit_flashbang 2
ammo_grenade_limit_total 4
bot_quota 0
cash_player_bomb_defused 300
cash_player_bomb_planted 300
cash_player_damage_hostage -30
cash_player_interact_with_hostage 300
cash_player_killed_enemy_default 300
cash_player_killed_enemy_factor 1
cash_player_killed_hostage -1000
cash_player_killed_teammate -300
cash_player_rescued_hostage 1000
cash_team_elimination_bomb_map 3250
cash_team_elimination_hostage_map_ct 3000
cash_team_elimination_hostage_map_t 3000
cash_team_hostage_alive 0
cash_team_hostage_interaction 600
cash_team_loser_bonus 1400
cash_team_loser_bonus_consecutive_rounds 500
cash_team_planted_bomb_but_defused 600
cash_team_rescued_hostage 600
cash_team_terrorist_win_bomb 3500
cash_team_win_by_defusing_bomb 3500
cash_team_win_by_hostage_rescue 2900
cash_team_win_by_time_running_out_bomb 3250
cash_team_win_by_time_running_out_hostage 3250
ff_damage_reduction_bullets 0.33
ff_damage_reduction_grenade 0.85
ff_damage_reduction_grenade_self 1
ff_damage_reduction_other 0.4
mp_afterroundmoney 0
mp_autokick 0
mp_autoteambalance 0
mp_backup_restore_load_autopause 1
mp_backup_round_auto 1
mp_buy_anywhere 0
mp_buy_during_immunity 0
mp_buytime 15
mp_c4timer 40
mp_ct_default_melee weapon_knife
mp_ct_default_primary """"
mp_ct_default_secondary weapon_hkp2000
mp_death_drop_defuser 1
mp_death_drop_grenade 2
mp_death_drop_gun 1
mp_defuser_allocation 0
mp_display_kill_assists 1
mp_endmatch_votenextmap 0
mp_forcecamera 1
mp_free_armor 0
mp_freezetime 12
mp_friendlyfire 1
mp_give_player_c4 1
mp_halftime 1
mp_halftime_duration 15
mp_halftime_pausetimer 0
mp_ignore_round_win_conditions 0
mp_limitteams 0
mp_match_can_clinch 1
mp_match_end_restart 0
mp_maxmoney 16000
mp_maxrounds 24
mp_overtime_enable 1
mp_overtime_halftime_pausetimer 0
mp_overtime_maxrounds 6
mp_overtime_startmoney 10000
mp_playercashawards 1
mp_randomspawn 0
mp_respawn_immunitytime 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_round_restart_delay 5
mp_roundtime 1.92
mp_roundtime_defuse 1.92
mp_roundtime_hostage 1.92
mp_solid_teammates 1
mp_starting_losses 1
mp_startmoney 800
mp_t_default_melee weapon_knife
mp_t_default_primary """"
mp_t_default_secondary weapon_glock
mp_teamcashawards 1
mp_timelimit 0
mp_weapons_allow_map_placed 1
mp_weapons_allow_zeus 1
mp_win_panel_display_time 5
spec_freeze_deathanim_time 0
spec_freeze_time 2
spec_freeze_time_lock 2
spec_replay_enable 0
sv_allow_votes 1
sv_auto_full_alltalk_during_warmup_half_end 0
sv_deadtalk 1
sv_hibernate_postgame_delay 300
sv_ignoregrenaderadio 0
sv_infinite_ammo 0
sv_talk_enemy_dead 0
sv_talk_enemy_living 0
sv_voiceenable 1
tv_relayvoice 0
mp_team_timeout_max 4
mp_team_timeout_time 30
sv_vote_command_delay 0
cash_team_bonus_shorthanded 1000
mp_spectators_max 10
mp_team_intro_time 0
mp_disconnect_kills_players 0
mp_warmup_end
mp_restartgame 1
sv_hide_roundtime_until_seconds 0
",

                [ConfigFiles.Paths.Warmup] =
                    @"
bot_kick
bot_quota 0
mp_autokick 0
mp_autoteambalance 0
mp_buy_anywhere 0
mp_buytime 15
mp_death_drop_gun 1
mp_free_armor 1
mp_ignore_round_win_conditions 0
mp_limitteams 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_solid_teammates 0
mp_spectators_max 20
mp_maxmoney 16000
mp_startmoney 16000
mp_timelimit 0
sv_alltalk 0
sv_auto_full_alltalk_during_warmup_half_end 0
sv_deadtalk 1
sv_full_alltalk 0
sv_hibernate_when_empty 0
mp_weapons_allow_typecount -1
sv_infinite_ammo 0
sv_showimpacts 0
sv_voiceenable 1
tv_relayvoice 0
sv_grenade_trajectory_prac_pipreview false
sv_grenade_trajectory_prac_trailtime 0
sv_showimpacts 0
sv_cheats 0
mp_ct_default_melee weapon_knife
mp_ct_default_secondary weapon_hkp2000
mp_ct_default_primary """"
mp_ct_default_grenades """";
mp_t_default_grenades """";
mp_t_default_melee weapon_knife
mp_t_default_secondary weapon_glock
mp_t_default_primary """"
mp_maxrounds 24
mp_warmuptime 9999
mp_warmup_online_enabled 1
mp_warmup_start
mp_warmup_pausetimer 1
cash_team_bonus_shorthanded 0
sv_hide_roundtime_until_seconds 0
ammo_grenade_limit_default 1
ammo_grenade_limit_flashbang 2
ammo_grenade_limit_total 4
",
            };
            foreach (var config in configs)
            {
                CreateConfigFile(config.Key, config.Value);
            }

            // Append any cvars missing from an existing config.cfg (preserves user edits)
            MergeMissingConfigCvars(configs[ConfigFiles.Paths.Config]);

            // Create matchzymaps.cfg separately with default map rotation
            CreateMapRotationFile();
        }

        private void MergeMissingConfigCvars(string templateContent)
        {
            try
            {
                string filePath = Path.Combine(ServerPath, ConfigFiles.Paths.Config);
                if (!File.Exists(filePath))
                {
                    return;
                }

                string existing = File.ReadAllText(filePath);
                var existingCvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawLine in existing.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    if (line.StartsWith("//"))
                    {
                        // Treat a commented-out cvar (e.g. "// matchzy_x true") as present so
                        // we do not re-append something the admin deliberately disabled. Only
                        // when the cvar name is the FIRST token after //, not a passing mention
                        // in prose (e.g. "// Example matchzy_hostname_format ...").
                        var commented = line.TrimStart('/').Trim();
                        var commentedName = commented.Split(new[] { ' ', '\t' }, 2)[0];
                        if (commentedName.StartsWith("matchzy_", StringComparison.OrdinalIgnoreCase))
                        {
                            existingCvars.Add(commentedName);
                        }
                        continue;
                    }
                    var name = line.Split(new[] { ' ', '\t' }, 2)[0];
                    if (name.Length > 0)
                    {
                        existingCvars.Add(name);
                    }
                }

                var missingBlocks = new List<string>();
                var currentBlock = new List<string>();

                void FlushBlock()
                {
                    if (currentBlock.Count == 0)
                    {
                        return;
                    }
                    string? cvarName = null;
                    foreach (var bl in currentBlock)
                    {
                        var t = bl.Trim();
                        if (t.Length == 0 || t.StartsWith("//"))
                        {
                            continue;
                        }
                        cvarName = t.Split(new[] { ' ', '\t' }, 2)[0];
                        break;
                    }
                    if (cvarName != null && !existingCvars.Contains(cvarName))
                    {
                        missingBlocks.Add(string.Join("\n", currentBlock));
                    }
                    currentBlock.Clear();
                }

                foreach (var rawLine in templateContent.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        FlushBlock();
                    }
                    else
                    {
                        currentBlock.Add(line);
                    }
                }
                FlushBlock();

                const string header = "// --- Added by MatchZy update (missing cvars appended) ---";

                // Collapse duplicate update headers left by older builds, which appended a FRESH
                // header on every load (one per newly-added cvar). Keep the first; removing the
                // rest only drops redundant comment lines, never a cvar. Ensures there is only ever
                // ONE "Added by MatchZy update" header in the file.
                var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
                int firstHeaderIdx = lines.FindIndex(l => l.Trim() == header);
                bool removedDuplicateHeaders = false;
                if (firstHeaderIdx >= 0)
                {
                    for (int i = lines.Count - 1; i > firstHeaderIdx; i--)
                    {
                        if (lines[i].Trim() == header)
                        {
                            lines.RemoveAt(i);
                            removedDuplicateHeaders = true;
                        }
                    }
                }

                // Prune retired cvars: a convar that was renamed/removed leaves a dead line in old
                // config.cfg files that spams "Unknown command '<name>'!" every time config.cfg execs.
                // Remove the setting line (or a commented-out one) plus a directly-preceding help
                // comment. Only EXACT retired names are touched; the current replacement (if any) is
                // appended by the missing-cvar pass above, so this migrates the file cleanly.
                var retiredCvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "matchzy_ready_hint_suppress_warmup", // renamed -> matchzy_ready_hide_warmup_hud (itself now retired)
                    "matchzy_ready_hide_warmup_hud",      // folded into matchzy_ready_hint_style modes 1/2
                    "matchzy_ready_block_warmup_announce",// removed: experimental warmup-announce block (dead end)
                };
                bool removedRetired = false;
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    var t = lines[i].Trim();
                    if (t.Length == 0)
                        continue;
                    string first = t.StartsWith("//")
                        ? t.TrimStart('/').Trim().Split(new[] { ' ', '\t' }, 2)[0]
                        : t.Split(new[] { ' ', '\t' }, 2)[0];
                    if (!retiredCvars.Contains(first))
                        continue;
                    lines.RemoveAt(i);
                    // Also drop a single directly-preceding help comment (not the update header).
                    if (i - 1 >= 0 && lines[i - 1].Trim().StartsWith("//") && lines[i - 1].Trim() != header)
                        lines.RemoveAt(i - 1);
                    removedRetired = true;
                }

                // Nothing new to add and nothing to clean -> leave the file untouched.
                if (missingBlocks.Count == 0 && !removedDuplicateHeaders && !removedRetired)
                {
                    return;
                }

                var sb = new System.Text.StringBuilder(string.Join("\n", lines).TrimEnd('\n'));
                sb.Append('\n');
                if (missingBlocks.Count > 0)
                {
                    sb.Append('\n');
                    // Add the header only when the file has none (after the dedup above).
                    if (firstHeaderIdx < 0)
                    {
                        sb.Append(header).Append('\n');
                    }
                    foreach (var block in missingBlocks)
                    {
                        sb.Append(block);
                        sb.Append("\n\n");
                    }
                }

                // Rewrite (not append): only redundant header comment lines were removed, so admin
                // edits to actual cvars are preserved.
                File.WriteAllText(filePath, sb.ToString());
                if (missingBlocks.Count > 0)
                    Console.WriteLine($"[MatchZy] Appended {missingBlocks.Count} missing cvar(s) to {ConfigFiles.Paths.Config}.");
                if (removedDuplicateHeaders)
                    Console.WriteLine($"[MatchZy] Collapsed duplicate update headers in {ConfigFiles.Paths.Config}.");
                if (removedRetired)
                    Console.WriteLine($"[MatchZy] Removed retired cvar line(s) from {ConfigFiles.Paths.Config}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error merging {ConfigFiles.Paths.Config}: {ex.Message}");
            }
        }

        private void CreateMapRotationFile()
        {
            try
            {
                string filePath = Path.Combine(ServerPath, "matchzymaps.cfg");
                if (!Directory.Exists(ServerPath))
                {
                    Directory.CreateDirectory(ServerPath);
                }
                if (!File.Exists(filePath))
                {
                    string defaultMaps =
                        @"# MatchZy Map Rotation Configuration
# 
# This file controls automatic map changes after matches end
# 
# How it works:
# - One map per line
# - Lines starting with # are comments and will be ignored
# - Empty lines are ignored
# - Maps will rotate in the order listed (top to bottom)
# - After the last map, rotation returns to the first map
# - If only one map is listed, the server will reload that same map
# 
# Map Format Examples:
# de_dust2                    - Standard map
# de_ancient                  - Another standard map
# workshop/3070212801         - Workshop map (recommended format)
# 3070212801                  - Workshop map by ID only (also works)
# workshop/3070212801/de_cache - Workshop map with name (works too)
# 
# Important Notes:
# - Workshop maps MUST be downloaded on your server before they can be used
# - Use +host_workshop_collection in your server launch options to auto-download
# - The plugin will use host_workshop_map for workshop maps automatically
# - The plugin will use changelevel for standard maps automatically
# 
# Tip: You can have just one map for a dedicated server, or multiple maps for variety
#
# Default Active Duty Map Pool:

de_dust2
de_inferno
de_mirage
de_nuke
de_overpass
de_vertigo
de_ancient

# Workshop Map Examples (uncomment to use):
# workshop/3070212801
# workshop/3121217565";

                    File.WriteAllText(filePath, defaultMaps);
                    Console.WriteLine($"[MatchZy] Created default map rotation file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchZy] Error creating matchzymaps.txt: {ex.Message}");
            }
        }

        public List<string> LoadMapRotation()
        {
            var maps = new List<string>();
            try
            {
                string filePath = Path.Combine(ServerPath, "matchzymaps.cfg");
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                            continue;

                        maps.Add(trimmed);
                    }
                    Console.WriteLine($"[MatchZy] Loaded {maps.Count} maps from map rotation");
                }
                else
                {
                    Console.WriteLine($"[MatchZy] matchzymaps.cfg not found, creating default...");
                    CreateMapRotationFile();
                    return LoadMapRotation();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchZy] Error loading map rotation: {ex.Message}");
            }
            return maps;
        }
    }
}
