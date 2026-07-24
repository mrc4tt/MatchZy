using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Named bot positions + botjiggle (practice).
    //   .savebotpos <name> (.sbp)  - save your current spot as a named bot placement for this map.
    //   .loadbotpos <name> (.lbp)  - spawn a bot at that saved spot (its saved team).
    //   .listbotpos (.listbp)      - list saved names on this map.
    //   .delbotpos <name> (.dbp)   - delete one.
    //   .botjiggle                 - toggle all practice bots strafing side-to-side (silent, teleport
    //                                driven) for dodge/aim reps. matchzy_botjiggle_range tunes width.
    public partial class MatchZy
    {
        // map -> name -> saved bot placement.
        private sealed class BotPos
        {
            public float X, Y, Z, Pitch, Yaw;
            public int Team;      // 2 = T, 3 = CT
            public bool Crouch;
        }

        private string BotPositionsPath =>
            Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("botpositions.json"));

        // BotPos stores its data in public FIELDS; System.Text.Json ignores fields by default, so
        // without this both save (writes "{}") and load (all-zero -> bot at 0,0,0 -> out of cell
        // bounds) were broken. IncludeFields fixes both directions.
        private static readonly JsonSerializerOptions BotPosJsonOpts = new() { IncludeFields = true, WriteIndented = true };

        private Dictionary<string, Dictionary<string, BotPos>> LoadBotPositions()
        {
            try
            {
                if (!File.Exists(BotPositionsPath))
                    return new();
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, BotPos>>>(File.ReadAllText(BotPositionsPath), BotPosJsonOpts) ?? new();
            }
            catch (Exception e)
            {
                Log($"[BotPositions] load: {e.Message}");
                return new();
            }
        }

        private void SaveBotPositions(Dictionary<string, Dictionary<string, BotPos>> data)
        {
            File.WriteAllText(BotPositionsPath, JsonSerializer.Serialize(data, BotPosJsonOpts));
        }

        // ── save / load / list / delete ──────────────────────────────────────────────────────────
        [ConsoleCommand("css_savebotpos", "Save your current position as a named bot placement")]
        [ConsoleCommand("css_sbp", "Save your current position as a named bot placement")]
        public void OnSaveBotPosCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleSaveBotPosCommand(player, command.ArgString);
        }

        public void HandleSaveBotPosCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.PlayerPawn.Value?.AbsOrigin == null)
                return;
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".savebotpos <name>"));
                return;
            }
            if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.jointeam"));
                return;
            }

            var pawn = player.PlayerPawn.Value;
            var o = pawn.AbsOrigin!;
            QAngle a = pawn.EyeAngles;
            bool crouch = false;
            if (pawn.MovementServices != null && pawn.MovementServices.Handle != IntPtr.Zero)
                crouch = new CCSPlayer_MovementServices(pawn.MovementServices.Handle).DuckAmount >= 0.5f;

            try
            {
                var data = LoadBotPositions();
                string map = Server.MapName;
                if (!data.TryGetValue(map, out var slots)) { slots = new(); data[map] = slots; }
                slots[name] = new BotPos { X = o.X, Y = o.Y, Z = o.Z, Pitch = a.X, Yaw = a.Y, Team = player.TeamNum, Crouch = crouch };
                SaveBotPositions(data);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.saved", name));
            }
            catch (Exception e)
            {
                Log($"[BotPositions] save: {e.Message}");
            }
        }

        [ConsoleCommand("css_loadbotpos", "Spawn a bot at a saved named position")]
        [ConsoleCommand("css_lbp", "Spawn a bot at a saved named position")]
        public void OnLoadBotPosCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleLoadBotPosCommand(player, command.ArgString);
        }

        public void HandleLoadBotPosCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || !IsPlayerValid(player) || !player!.UserId.HasValue)
                return;
            name = (name ?? "").Trim();
            var slots = LoadBotPositions().TryGetValue(Server.MapName, out var s) ? s : null;
            if (slots == null || slots.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.none"));
                return;
            }

            // No name -> spawn ALL saved bots for this map; else the nearest-name match.
            var targets = new List<BotPos>();
            if (string.IsNullOrEmpty(name))
            {
                targets.AddRange(slots.Values);
            }
            else
            {
                string nearest = StringSimilarity.FindNearestName(name, slots.Keys.ToList());
                if (!slots.TryGetValue(nearest, out var bp))
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.notfound", name));
                    return;
                }
                name = nearest;
                targets.Add(bp);
            }

            foreach (var bp in targets)
            {
                // Yaw ONLY - never pass the saver's view pitch to the placement. SpawnBot teleports
                // the bot with this angle; a steep saved pitch (looking up/down at save time) tilted
                // the bot's whole model back and lifted it off the ground / under the map. A placed bot
                // has no use for view pitch (the aim-mirror idea was dropped); it just faces the yaw.
                var pos = new Position(new Vector(bp.X, bp.Y, bp.Z), new QAngle(0.0f, bp.Yaw, 0.0f));
                CsTeam team = bp.Team == (byte)CsTeam.CounterTerrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
                AddBot(player, bp.Crouch, forceTeam: team, boost: false, posOverride: pos);
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.loaded", string.IsNullOrEmpty(name) ? $"{targets.Count}" : name));
        }

        [ConsoleCommand("css_listbotpos", "List saved bot positions on this map")]
        [ConsoleCommand("css_listbp", "List saved bot positions on this map")]
        public void OnListBotPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            var slots = LoadBotPositions().TryGetValue(Server.MapName, out var s) ? s : null;
            if (slots == null || slots.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.none"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.list", string.Join(", ", slots.Keys)));
        }

        [ConsoleCommand("css_delbotpos", "Delete a saved bot position")]
        [ConsoleCommand("css_dbp", "Delete a saved bot position")]
        public void OnDelBotPosCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleDelBotPosCommand(player, command.ArgString);
        }

        public void HandleDelBotPosCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".delbotpos <name>"));
                return;
            }
            try
            {
                var data = LoadBotPositions();
                if (!data.TryGetValue(Server.MapName, out var slots) || slots.Count == 0)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.none"));
                    return;
                }
                string nearest = StringSimilarity.FindNearestName(name, slots.Keys.ToList());
                if (!slots.Remove(nearest))
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.notfound", name));
                    return;
                }
                if (slots.Count == 0)
                    data.Remove(Server.MapName);
                SaveBotPositions(data);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.deleted", nearest));
            }
            catch (Exception e)
            {
                Log($"[BotPositions] delete: {e.Message}");
            }
        }

        // ── .showbotpos : draw saved bot placements in-world ─────────────────────────────────────
        private readonly List<CBaseEntity> _botPosViz = new();
        private bool _botPosVizOn;

        [ConsoleCommand("css_showbotpos", "Show saved bot positions in-world")]
        [ConsoleCommand("css_showbp", "Show saved bot positions in-world")]
        public void OnShowBotPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            if (_botPosVizOn)
            {
                ClearBotPosViz();
                ReplyToUserCommand(player!, Localizer.ForPlayer(player, "matchzy.bp.hidden"));
                return;
            }
            int shown = DrawBotPosMarkers();
            _botPosVizOn = shown > 0;
            ReplyToUserCommand(player!, shown > 0
                ? Localizer.ForPlayer(player, "matchzy.bp.showing", $"{shown}")
                : Localizer.ForPlayer(player, "matchzy.bp.none"));
        }

        private int DrawBotPosMarkers()
        {
            var slots = LoadBotPositions().TryGetValue(Server.MapName, out var s) ? s : null;
            if (slots == null)
                return 0;
            int shown = 0;
            foreach (var (name, bp) in slots)
            {
                var pos = new Vector(bp.X, bp.Y, bp.Z);
                // Colour by team the bot spawns on (green base, tinted): CT lime, T orange.
                var color = bp.Team == (byte)CsTeam.CounterTerrorist ? System.Drawing.Color.Lime : System.Drawing.Color.Orange;
                var beam = Utilities.CreateEntityByName<CBeam>("beam");
                if (beam != null)
                {
                    beam.LifeState = 1;
                    beam.Width = 3.0f;
                    beam.Render = color;
                    beam.EndPos.X = pos.X; beam.EndPos.Y = pos.Y; beam.EndPos.Z = pos.Z + 72.0f;
                    beam.Teleport(new Vector(pos.X, pos.Y, pos.Z + 2.0f), new QAngle(0, 0, 0), new Vector(0, 0, 0));
                    beam.DispatchSpawn();
                    _botPosViz.Add(beam);
                }
                var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
                if (text != null)
                {
                    text.MessageText = name;
                    text.Color = color;
                    text.FontName = "Arial Bold";
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bEnabled", true);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flFontSize", 60.0f);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flWorldUnitsPerPx", 0.25f);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bFullbright", true);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bDrawBackground", true);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flBackgroundBorderWidth", 6.0f);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flBackgroundBorderHeight", 4.0f);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyHorizontal", 1);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyVertical", 1);
                    Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nReorientMode", 1);
                    text.Teleport(new Vector(pos.X, pos.Y, pos.Z + 84.0f), new QAngle(0, bp.Yaw, 90), new Vector(0, 0, 0));
                    text.DispatchSpawn();
                    _botPosViz.Add(text);
                }
                shown++;
            }
            return shown;
        }

        private void ClearBotPosViz()
        {
            foreach (var e in _botPosViz)
                if (e != null && e.IsValid)
                    SafeRemoveEntity(e, "botposviz");
            _botPosViz.Clear();
            _botPosVizOn = false;
        }

        // Redraw on map change (entities die but the toggle stayed on).
        public void RefreshBotPosVizOnMapStart()
        {
            _botPosViz.Clear();
            if (!_botPosVizOn)
                return;
            _botPosVizOn = false;
            AddTimer(2.0f, () =>
            {
                try { _botPosVizOn = DrawBotPosMarkers() > 0; }
                catch (Exception e) { Log($"[BotPosViz] map-start redraw: {e.Message}"); }
            });
        }

        // ── botjiggle : silent side-to-side strafing ─────────────────────────────────────────────
        private bool _botJiggleOn;
        private readonly Dictionary<int, (Vector Base, QAngle Ang)> _botJiggleBase = new();
        private int _botJiggleTick;

        [ConsoleCommand("css_botjiggle", "Toggle bots strafing side-to-side (silent)")]
        public void OnBotJiggleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            _botJiggleOn = !_botJiggleOn;
            if (!_botJiggleOn)
            {
                _botJiggleBase.Clear();
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.jiggleoff"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.jiggleon"));
        }

        // OnTick listener (registered in Load). No-op unless botjiggle is on.
        public void OnBotJiggleTick()
        {
            if (!_botJiggleOn)
                return;
            try
            {
                _botJiggleTick++;
                float range = botJiggleRange.Value;
                double phase = _botJiggleTick * 0.18;   // ~ a full sweep every ~2s at 64 tick
                float offset = (float)Math.Sin(phase) * range;

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || !p.IsBot || p.IsHLTV || !p.UserId.HasValue || !p.PawnIsAlive)
                        continue;
                    var pawn = p.PlayerPawn?.Value;
                    if (pawn?.AbsOrigin == null)
                        continue;
                    int uid = p.UserId.Value;
                    if (!_botJiggleBase.TryGetValue(uid, out var b))
                    {
                        // Anchor on first sight: base position + facing right vector for the strafe axis.
                        b = (new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z), pawn.EyeAngles);
                        _botJiggleBase[uid] = b;
                    }
                    double yaw = b.Ang.Y * Math.PI / 180.0;
                    float rx = (float)Math.Sin(yaw);    // right vector (perpendicular to facing)
                    float ry = -(float)Math.Cos(yaw);
                    var dst = new Vector(b.Base.X + rx * offset, b.Base.Y + ry * offset, b.Base.Z);
                    pawn.Teleport(dst, b.Ang, new Vector(0, 0, 0));
                }
            }
            catch (Exception e)
            {
                Log($"[BotJiggle] {e.Message}");
                _botJiggleOn = false;
            }
        }
    }
}
