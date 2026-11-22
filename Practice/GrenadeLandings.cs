using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using Vector3 = System.Numerics.Vector3;

namespace MatchZy
{
    // Landing icons for the lineup library: `.shownades` also shows a floating grenade of the
    // right type where each saved smoke or decoy LANDS (calculated with the finder's throw model,
    // nothing is thrown). Lineups that land in the same place share one icon. E on an icon lists
    // the lineups that land there and draws a line to each throw spot; the ghost on a throw spot
    // takes the player onto the lineup as before.
    public partial class MatchZy
    {
        private sealed class LandingSpot
        {
            public Vector3 Pos;
            public string Type = "";
            public readonly List<NadeLineup> Lineups = new();
            public string Kind = "";                 // smoke, decoy, he, flash, molotov
            public CBaseEntity? Icon, Label;
            public readonly HashSet<NadeMarkerGroup> Groups = new(); // the throw spots of these lineups
        }

        // What each viewer sees: the grenade in their hand filters the marks to its type (nothing
        // or a rifle in hand = every type), and the throw spots stay hidden until they pick a
        // landing mark (E on it): then only that mark's throw spots show.
        private readonly Dictionary<int, string?> landingHeld = new();
        private readonly Dictionary<int, LandingSpot> landingSelected = new();
        // Throw spots that belong to some landing mark (the others have no calculated landing and
        // stay visible, or nobody could ever reach them).
        private readonly HashSet<NadeMarkerGroup> landingCovered = new();

        private static string? LineupKind(string savedType) => (savedType ?? "").Trim().ToLowerInvariant() switch
        {
            "smoke" => "smoke", "decoy" => "decoy", "he" => "he", "flash" => "flash",
            "molly" or "molotov" or "incendiary" => "molotov",
            _ => null,
        };

        // Looked up a few times a second here, not in the transmit path.
        private void RefreshLandingHeld()
        {
            foreach (var player in Utilities.GetPlayers())
                if (player != null && player.IsValid && !player.IsBot && _nadeViewers.Contains(player.SteamID))
                    landingHeld[player.Slot] = FinderHeldType(player);
        }

        // Rolled back 2026-11-17: off = every mark and every throw spot is shown to every viewer,
        // as that morning. On = the grenade in hand filters the marks and the throw spots stay
        // hidden until a landing mark is picked.
        private static readonly bool LandingMarksByHand = false;

        // The same rule as the transmit filter below, for the aim pick.
        private bool LandingGroupShown(int slot, NadeMarkerGroup g)
        {
            if (!LandingMarksByHand)
                return true;
            landingHeld.TryGetValue(slot, out string? held);
            if (landingCovered.Contains(g))
                return landingSelected.TryGetValue(slot, out var picked) && picked.Groups.Contains(g);
            return held == null || g.Lineups.Any(lu => LineupKind(lu.Type) == held);
        }

        // Called from the library's CheckTransmit for a viewer: takes out what this viewer should
        // not see. Never throws (a throw in the transmit path takes the server down).
        private void FilterLandingTransmit(int slot, CCheckTransmitInfo info)
        {
            if (!LandingMarksByHand)
                return;
            landingHeld.TryGetValue(slot, out string? held);
            landingSelected.TryGetValue(slot, out var picked);
            var list = info.TransmitEntities;
            foreach (var spot in landingSpots)
            {
                if (held == null || spot.Kind == held)
                    continue;
                if (spot.Icon != null && spot.Icon.IsValid) list.Remove(spot.Icon);
                if (spot.Label != null && spot.Label.IsValid) list.Remove(spot.Label);
            }
            foreach (var g in activeNadeGroups)
            {
                bool show = landingCovered.Contains(g)
                    ? picked != null && picked.Groups.Contains(g)
                    : held == null || g.Lineups.Any(lu => LineupKind(lu.Type) == held);
                if (!show && g.Glow != null && g.Glow.IsValid)
                    list.Remove(g.Glow);
            }
        }

        private readonly List<LandingSpot> landingSpots = new();
        private readonly List<uint> landingLines = new();
        private readonly HashSet<int> landingUseDown = new();
        private int landingLinesToken;
        private const float LandingClusterRadius = 90f;
        private const int LandingMaxCalculated = 250;
        private const float LandingIconHeight = 28f;
        private const float LandingLinesSeconds = 45f;

        // From the two places that build the library markers, after the groups are drawn.
        private void DrawLandingSpots(List<NadeLineup> lineups)
        {
            landingSpots.Clear();
            landingSelected.Clear();
            landingCovered.Clear();
            if (!HasCssTraceApi)
                return;
            int calculated = 0;
            foreach (var lu in lineups)
            {
                string type = lu.Type.Trim().ToLowerInvariant();
                // Every grenade type: where a smoke or decoy comes to rest, where a molotov
                // ignites, where an HE or flash goes off.
                string? finder = type switch
                {
                    "smoke" => "smoke", "decoy" => "decoy", "he" => "he", "flash" => "flash",
                    "molly" or "molotov" or "incendiary" => "molotov",
                    _ => null,
                };
                if (finder == null)
                    continue;
                // The lineup's own stored release when it has one (any kind of throw), else the
                // throw model for the kinds it knows.
                GrenadeSimLogic.Launch? own = null;
                if (LineupFormat.TryParse(lu.NadePos, out float px, out float py, out float pz)
                    && LineupFormat.TryParse(lu.NadeVel, out float vx, out float vy, out float vz) && vx * vx + vy * vy + vz * vz > 1f)
                    own = new GrenadeSimLogic.Launch(new Vector3(px, py, pz), new Vector3(vx, vy, vz));
                var known = FinderKindFromThrow(lu.Throw);
                if (own == null && known == null)
                    continue;
                if (++calculated > LandingMaxCalculated)
                    break;
                var feet = new Vector3(lu.Pos.PlayerPosition.X, lu.Pos.PlayerPosition.Y, lu.Pos.PlayerPosition.Z);
                GrenadeSimLogic.Flight flight;
                try
                {
                    var fuse = FinderFuseOf(finder);
                    GrenadeSimLogic.TraceFn landTrace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                        FinderGrenadeTrace(a, b, out hit, out normal, out fraction, null);
                    flight = own is GrenadeSimLogic.Launch l0
                        ? GrenadeSimLogic.Simulate(l0, landTrace, 20f, 1, null, fuse)
                        : FinderFlight(feet, known!.Value, lu.Pos.PlayerAngle.X, lu.Pos.PlayerAngle.Y, null, null, fuse);
                }
                catch (Exception e)
                {
                    Log($"[GrenadeLibrary] landing of {lu.Name}: {e.Message}");
                    continue;
                }
                if (!flight.Stopped)
                    continue;
                var spot = landingSpots.FirstOrDefault(s => s.Type == type && Vector3.Distance(s.Pos, flight.Rest) < LandingClusterRadius);
                if (spot == null)
                {
                    spot = new LandingSpot { Pos = flight.Rest, Type = type, Kind = finder };
                    landingSpots.Add(spot);
                }
                spot.Lineups.Add(lu);
                foreach (var g in activeNadeGroups)
                    if (g.Lineups.Contains(lu))
                    {
                        spot.Groups.Add(g);
                        landingCovered.Add(g);
                    }
            }

            foreach (var spot in landingSpots)
            {
                var first = spot.Lineups[0];
                Color color = NadeTypeColor(first.Type);
                // The grenade itself, glowing through walls, floating over the landing point.
                // prop_dynamic_override, not prop_dynamic: a weapon model carries prop data, and the
                // game deletes a plain prop_dynamic that uses one ("...which has propdata... DELETED"),
                // so the icon was never there.
                var icon = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic_override");
                if (icon != null)
                {
                    // The model only AFTER the spawn. Set before it, the game checks the model at
                    // spawn, finds prop data and deletes the icon (console: "...which has propdata...
                    // DELETED"), also for prop_dynamic_override. Seen live 2026-11-17: every icon gone.
                    icon.Collision.CollisionGroup = 2;
                    icon.Collision.SolidType = SolidType_t.SOLID_NONE;
                    icon.Spawnflags = 256u; // no collisions
                    icon.Teleport(new Vector(spot.Pos.X, spot.Pos.Y, spot.Pos.Z + LandingIconHeight), new QAngle(0, 0, 0), new Vector(0, 0, 0));
                    icon.DispatchSpawn();
                    icon.SetModel(NadeGlowModel(first.Type));
                    icon.AcceptInput("SetScale", value: NadeIconScale);
                    SetNadeGlowColor(icon, color);
                    nadeMarkerEntities.Add(icon);
                    spot.Icon = icon;
                }
                // One label for every viewer: the server language (no single player to localize for).
                string text = spot.Lineups.Count == 1 ? first.Name : Localizer["matchzy.pp.track.landing.lineups", spot.Lineups.Count].Value;
                uint? label = FinderLabel(text, new Vector3(spot.Pos.X, spot.Pos.Y, spot.Pos.Z + LandingIconHeight + 16f), color);
                if (label.HasValue && new CHandle<CBaseEntity>(label.Value).Value is CBaseEntity labelEnt)
                {
                    nadeMarkerEntities.Add(labelEnt);
                    spot.Label = labelEnt;
                }
            }
        }

        private void ForgetLandingSpots(bool removeEntities)
        {
            landingSpots.Clear();
            landingSelected.Clear();
            landingCovered.Clear();
            landingHeld.Clear();
            landingUseDown.Clear();
            landingLinesToken++;
            if (removeEntities)
                KillFinderEntities(landingLines);
            else
                landingLines.Clear();
        }

        // E on a landing icon (aimed at it, within a few degrees).
        private void TickLandingPick()
        {
            if (landingSpots.Count == 0 || !grenadeLibraryActive || !isPractice)
                return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot || !_nadeViewers.Contains(player.SteamID))
                    continue;
                int slot = player.Slot;
                bool use = (player.Buttons & PlayerButtons.Use) != 0;
                bool was = landingUseDown.Contains(slot);
                if (use) landingUseDown.Add(slot); else landingUseDown.Remove(slot);
                // Not pressing: only a look now and then, for the hint on the icon aimed at.
                bool hintTick = false; // parked with free roam: the "press E" hint on a landing icon
                if ((!use || was || drawSessions.ContainsKey(slot) || LookMenuOpen(slot)) && !hintTick)
                    continue;
                var best = AimedLandingSpot(player);
                if (best == null)
                    continue;
                if (hintTick)
                    continue;
                // One lineup lands here: E goes straight onto its throw spot, grenade in hand.
                // Several: a menu to pick one, then the same. (Until 2026-11-17 E drew lines to
                // follow to the throw spots: up to 2400 u on foot, and nothing to switch them off.)
                int loadSlot = slot;
                if (LandingMarksByHand)
                    landingSelected[slot] = best;
                if (best.Lineups.Count == 1)
                {
                    var only = best.Lineups[0];
                    Server.NextFrame(() =>
                    {
                        var lp = Utilities.GetPlayerFromSlot(loadSlot);
                        if (lp != null && IsPlayerValid(lp))
                            LoadNadeLineup(lp, only);
                    });
                }
                else
                {
                    var options = new List<(string Label, Action<CCSPlayerController> Run)>();
                    foreach (var lu in best.Lineups.Take(8))
                    {
                        var pick = lu;
                        options.Add(($"{lu.Name}  ·  {(string.IsNullOrEmpty(lu.Throw) ? lu.Type : lu.Throw)}", p => LoadNadeLineup(p, pick)));
                    }
                    Server.NextFrame(() =>
                    {
                        var lp = Utilities.GetPlayerFromSlot(loadSlot);
                        if (lp != null && IsPlayerValid(lp))
                            OpenLookMenu(lp, Localizer.ForPlayer(lp, "matchzy.pp.track.landing.menu_title"), options);
                    });
                }
            }
        }

        // The landing mark this player aims at (within a few degrees), of the ones they see.
        // A mark behind a wall is not aimed at: the icons glow through walls so they can be found,
        // but naming one, or E on it, through a house front read as a fault (the author,
        // 2026-11-17: "find5 lands here" while looking at a closed door). A ray from the eye has
        // to reach the mark, or all but the last of the way (marks sit on floors and walls).
        private static bool MarkInSight(Vector3 eye, Vector3 mark, CBaseEntity? ignore)
        {
            try
            {
                if (!FinderTrace(eye, mark, out _, out _, out float fraction, ignore != null && ignore.IsValid ? ignore : null))
                    return true;
                return fraction > 0.97f || Vector3.Distance(eye, mark) * (1f - fraction) < 12f;
            }
            catch
            {
                return true;
            }
        }

        private LandingSpot? AimedLandingSpot(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                return null;
            if (AimingOwnLineup(player))
                return null;
            var eye = new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            var fwd = GrenadeSimLogic.Forward(pawn.EyeAngles.X, pawn.EyeAngles.Y);
            LandingSpot? best = null;
            float bestCos = MathF.Cos(5f * MathF.PI / 180f);
            landingHeld.TryGetValue(player.Slot, out string? heldNow);
            if (!LandingMarksByHand)
                heldNow = null;
            foreach (var spot in landingSpots)
            {
                if (heldNow != null && spot.Kind != heldNow)
                    continue; // not shown to this player: not to be picked either
                foreach (float h in new[] { 4f, LandingIconHeight, LandingIconHeight + 16f })
                {
                    var to = new Vector3(spot.Pos.X, spot.Pos.Y, spot.Pos.Z + h) - eye;
                    float len = to.Length();
                    if (len < 1f)
                        continue;
                    float cos = Vector3.Dot(to / len, fwd);
                    if (cos > bestCos && MarkInSight(eye, eye + to, pawn))
                    {
                        bestCos = cos;
                        best = spot;
                    }
                }
            }
            return best;
        }

    }
}
