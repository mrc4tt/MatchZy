using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        private void InitPlayerDamageInfo()
        {
            // Read native state once per player, not once per attacker/target pair.
            // Keep the same human T-vs-CT pairs, including zero-damage report entries.
            var players = new List<(int Id, byte Team)>(playerData.Count);
            foreach (var entry in playerData)
            {
                var player = entry.Value;
                // Coaches sit on CT/T but never fight; without this every enemy saw a
                // "0 / 0 hits" line for each coach.
                if (!player.IsValid || player.IsBot || IsMatchCoach(player))
                    continue;
                byte team = player.TeamNum;
                if (team == 2 || team == 3)
                    players.Add((entry.Key, team));
            }

            foreach (var attacker in players)
            {
                foreach (var target in players)
                {
                    if (attacker.Team == target.Team)
                        continue;
                    if (!playerDamageInfo.TryGetValue(attacker.Id, out var attackerInfo))
                        playerDamageInfo[attacker.Id] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();
                    if (!attackerInfo.ContainsKey(target.Id))
                        attackerInfo[target.Id] = new DamagePlayerInfo();
                }
            }
        }

        public Dictionary<int, Dictionary<int, DamagePlayerInfo>> playerDamageInfo = new Dictionary<int, Dictionary<int, DamagePlayerInfo>>();

        private void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
        {
            CCSPlayerController? attacker = @event.Attacker;

            if (!IsPlayerValid(attacker))
                return;
            int attackerId = (int)attacker!.UserId!;
            if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
                playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

            if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
                attackerInfo[targetId] = targetInfo = new DamagePlayerInfo();

            targetInfo.DamageHP += @event.DmgHealth;
            targetInfo.Hits++;
            // Health is what the victim has left after this hit: 0 = this attacker got the kill.
            if (@event.Health <= 0)
                targetInfo.Killed = true;
        }

        // One chat line per enemy at round end, in the FACEIT layout:
        // "To: [87 / 3 hits] From: [100 / 4 hits] - name (13 hp)", ending in "(killed)" when the
        // reader got the kill and "(dead)" when someone else did. The lines are in the reader's
        // language and sorted by damage dealt, so the enemy you hurt most is on top.
        private string DamageReportLine(CCSPlayerController reader, int dealt, int dealtHits, int taken, int takenHits,
            string enemyName, int enemyHp, bool killedByReader)
        {
            if (enemyHp > 0)
                return Localizer.ForPlayer(reader, "matchzy.dmg.line", dealt, dealtHits, taken, takenHits, enemyName, enemyHp);
            return Localizer.ForPlayer(reader, killedByReader ? "matchzy.dmg.killed" : "matchzy.dmg.dead",
                dealt, dealtHits, taken, takenHits, enemyName);
        }

        private CCSPlayerController? DamageReportPlayer(int userId)
        {
            if (playerData.TryGetValue(userId, out var known))
                return known;
            var found = Utilities.GetPlayerFromUserid(userId);
            return found != null && found.IsValid && found.IsBot && !found.IsHLTV ? found : null;
        }

        private void ShowDamageInfo()
        {
            if (!enableDamageReport.Value)
                return;
            try
            {
                HashSet<(int, int)> processedPairs = new HashSet<(int, int)>();
                var reports = new Dictionary<int, (CCSPlayerController Reader, List<(int Dealt, int Taken, string Line)> Lines)>();

                void AddLine(int readerId, CCSPlayerController reader, int dealt, int taken, string line)
                {
                    if (!reports.TryGetValue(readerId, out var report))
                        reports[readerId] = report = (reader, new List<(int, int, string)>());
                    report.Lines.Add((dealt, taken, line));
                }

                foreach (var entry in playerDamageInfo)
                {
                    int attackerId = entry.Key;
                    foreach (var (targetId, targetEntry) in entry.Value)
                    {
                        if (processedPairs.Contains((attackerId, targetId)) || processedPairs.Contains((targetId, attackerId)))
                            continue;

                        int damageGiven = targetEntry.DamageHP;
                        int hitsGiven = targetEntry.Hits;
                        bool attackerKilledTarget = targetEntry.Killed;
                        int damageTaken = 0;
                        int hitsTaken = 0;
                        bool targetKilledAttacker = false;

                        if (playerDamageInfo.TryGetValue(targetId, out var targetInfo) && targetInfo.TryGetValue(attackerId, out var takenInfo))
                        {
                            damageTaken = takenInfo.DamageHP;
                            hitsTaken = takenInfo.Hits;
                            targetKilledAttacker = takenInfo.Killed;
                        }

                        // playerData holds humans only. A bot standing in for a player is still an
                        // opponent worth a line, so it is looked up by userid instead; a human with
                        // only bots against them used to get no report at all.
                        var attackerController = DamageReportPlayer(attackerId);
                        var targetController = DamageReportPlayer(targetId);

                        if (attackerController != null && targetController != null)
                        {
                            if (!attackerController.IsValid || !targetController.IsValid)
                                continue;
                            if (attackerController.Connected != PlayerConnectedState.Connected)
                                continue;
                            if (targetController.Connected != PlayerConnectedState.Connected)
                                continue;
                            if (!attackerController.PlayerPawn.IsValid || !targetController.PlayerPawn.IsValid)
                                continue;
                            if (attackerController.PlayerPawn.Value == null || targetController.PlayerPawn.Value == null)
                                continue;

                            int attackerHP = attackerController.PlayerPawn.Value.Health < 0 ? 0 : attackerController.PlayerPawn.Value.Health;
                            int targetHP = targetController.PlayerPawn.Value.Health < 0 ? 0 : targetController.PlayerPawn.Value.Health;

                            // A bot reads no chat: lines go to the human side of the pair only.
                            if (!attackerController.IsBot)
                                AddLine(attackerId, attackerController, damageGiven, damageTaken,
                                    DamageReportLine(attackerController, damageGiven, hitsGiven, damageTaken, hitsTaken,
                                        targetController.PlayerName, targetHP, attackerKilledTarget));
                            if (!targetController.IsBot)
                                AddLine(targetId, targetController, damageTaken, damageGiven,
                                    DamageReportLine(targetController, damageTaken, hitsTaken, damageGiven, hitsGiven,
                                        attackerController.PlayerName, attackerHP, targetKilledAttacker));
                        }

                        // Mark this pair as processed to avoid duplicates.
                        processedPairs.Add((attackerId, targetId));
                    }
                }

                foreach (var (reader, lines) in reports.Values)
                {
                    lines.Sort((x, y) => y.Dealt != x.Dealt ? y.Dealt.CompareTo(x.Dealt) : y.Taken.CompareTo(x.Taken));
                    foreach (var line in lines)
                        PrintToPlayerChat(reader, line.Line);
                }

                playerDamageInfo.Clear();
            }
            catch (Exception e)
            {
                Log($"[ShowDamageInfo FATAL] An error occurred: {e.Message}");
            }
        }
    }

    public class DamagePlayerInfo
    {
        public int DamageHP { get; set; } = 0;
        public int Hits { get; set; } = 0;
        public bool Killed { get; set; } = false;
    }
}
