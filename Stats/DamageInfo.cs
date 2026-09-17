using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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
                if (!player.IsValid || player.IsBot)
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
        }

        private void ShowDamageInfo()
        {
            if (!enableDamageReport.Value)
                return;
            try
            {
                HashSet<(int, int)> processedPairs = new HashSet<(int, int)>();

                foreach (var entry in playerDamageInfo)
                {
                    int attackerId = entry.Key;
                    foreach (var (targetId, targetEntry) in entry.Value)
                    {
                        if (processedPairs.Contains((attackerId, targetId)) || processedPairs.Contains((targetId, attackerId)))
                            continue;

                        // Access and use the damage information as needed.
                        int damageGiven = targetEntry.DamageHP;
                        int hitsGiven = targetEntry.Hits;
                        int damageTaken = 0;
                        int hitsTaken = 0;

                        if (playerDamageInfo.TryGetValue(targetId, out var targetInfo) && targetInfo.TryGetValue(attackerId, out var takenInfo))
                        {
                            damageTaken = takenInfo.DamageHP;
                            hitsTaken = takenInfo.Hits;
                        }

                        if (!playerData.ContainsKey(attackerId) || !playerData.ContainsKey(targetId))
                            continue;

                        var attackerController = playerData[attackerId];
                        var targetController = playerData[targetId];

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
                            string attackerName = attackerController.PlayerName;

                            int targetHP = targetController.PlayerPawn.Value.Health < 0 ? 0 : targetController.PlayerPawn.Value.Health;
                            string targetName = targetController.PlayerName;

                            PrintToPlayerChat(attackerController, $"{ChatColors.Green}To: [{damageGiven} / {hitsGiven} hits] From: [{damageTaken} / {hitsTaken} hits] - {targetName} - ({targetHP} hp){ChatColors.Default}");
                            PrintToPlayerChat(targetController, $"{ChatColors.Green}To: [{damageTaken} / {hitsTaken} hits] From: [{damageGiven} / {hitsGiven} hits] - {attackerName} - ({attackerHP} hp){ChatColors.Default}");
                        }

                        // Mark this pair as processed to avoid duplicates.
                        processedPairs.Add((attackerId, targetId));
                    }
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
    }
}
