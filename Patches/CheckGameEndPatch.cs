using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using System.Collections;
using TOHE.Modules;
using TOHE.Modules.Rpc;
using TOHE.Roles.AddOns.Common;
using TOHE.Roles.AddOns.Crewmate;
using TOHE.Roles.AddOns.Impostor;
using TOHE.Roles.Core;
using TOHE.Roles.Neutral;
using UnityEngine;
using static TOHE.CustomWinnerHolder;
using static TOHE.Translator;

namespace TOHE;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.CheckEndGameViaTasks))]
class CheckEndGameViaTasksForNormalPatch
{
    public static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}
[HarmonyPatch(typeof(GameManager), nameof(GameManager.CheckTaskCompletion))]
class CheckTaskCompletionPatch
{
    public static bool Prefix(ref bool __result)
    {
        if (Options.DisableTaskWin.GetBool() || Options.NoGameEnd.GetBool() || TaskState.InitialTotalTasks == 0 || Options.CurrentGameMode == CustomGameMode.FFA)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.CheckEndCriteria))]
class GameEndCheckerForNormal
{
    public static GameEndPredicate predicate;
    public static bool GameIsEnded = false;
    public static bool ShouldNotCheck = false;

    public static bool Prefix()
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        if (predicate == null || ShouldNotCheck) return false;

        if (Options.NoGameEnd.GetBool() && WinnerTeam is not CustomWinner.Draw and not CustomWinner.Error) return false;

        GameIsEnded = false;
        var reason = GameOverReason.ImpostorsByKill;
        predicate.CheckForEndGame(out reason);

        // FFA
        switch (Options.CurrentGameMode)
        {
            case CustomGameMode.FFA:
                if (WinnerIds.Count > 0 || WinnerTeam != CustomWinner.Default)
                {
                    ShipStatus.Instance.enabled = false;
                    StartEndGame(reason);
                    predicate = null;
                }
                return false;
            case CustomGameMode.SpeedRun:
                if (WinnerIds.Count > 0 || WinnerTeam != CustomWinner.Default)
                {
                    SpeedRun.RpcSyncSpeedRunStates();
                    ShipStatus.Instance.enabled = false;
                    StartEndGame(reason);
                    predicate = null;
                }
                return false;
        }

        // Start end game
        if (WinnerTeam != CustomWinner.Default)
        {
            // Clear all Notice players 
            NameNotifyManager.Reset();

            // Reset Camouflage
            Main.AllPlayerControls.Do(pc => Camouflage.RpcSetSkin(pc, ForceRevert: true, RevertToDefault: true, GameEnd: true));

            // Show all roles
            GameIsEnded = true;

            Logger.Info("Start end game", "CheckEndCriteria.Prefix");

            Logger.Info($"WinnerTeam on enter: {WinnerTeam}", "CheckEndCriteriaForNormal.Prefix");
            Logger.Info($"WinnerIds: {string.Join(", ", WinnerIds)}", "CheckEndCriteriaForNormal.Prefix");

            if (reason == GameOverReason.ImpostorsBySabotage && (CustomRoles.Jackal.RoleExist() || CustomRoles.Sidekick.RoleExist()) && Jackal.CanWinBySabotageWhenNoImpAlive.GetBool() && !Main.AllAlivePlayerControls.Any(x => x.GetCustomRole().IsImpostorTeamV3() || (x.Is(CustomRoles.Madmate) && Madmate.MadmateCountMode.GetInt() == 1)))
            {
                reason = GameOverReason.ImpostorsByKill;
                WinnerIds.Clear();
                ResetAndSetWinner(CustomWinner.Jackal);
                WinnerRoles.Add(CustomRoles.Jackal);
            }

            foreach (var pc in Main.AllPlayerControls)
            {
                if (WinnerIds.Contains(pc.PlayerId)) continue;
                var countType = Main.PlayerStates[pc.PlayerId].countTypes;

                switch (WinnerTeam)
                {
                    case CustomWinner.Crewmate:
                        if ((pc.Is(Custom_Team.Crewmate) && (countType == CountTypes.Crew || pc.Is(CustomRoles.Soulless)) && !Main.PlayerStates[pc.PlayerId].IsNecromancer)
                            || pc.Is(CustomRoles.Admired) || pc.Is(CustomRoles.Narc)
                           )
                        {
                            // When admired neutral win, set end game reason "HumansByVote"
                            if (reason is not GameOverReason.CrewmatesByVote and not GameOverReason.CrewmatesByTask)
                            {
                                reason = GameOverReason.CrewmatesByVote;
                            }
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Impostor:
                        if (((pc.Is(Custom_Team.Impostor) || pc.GetCustomRole().IsMadmate()) && (countType == CountTypes.Impostor || pc.Is(CustomRoles.Soulless)) && !Main.PlayerStates[pc.PlayerId].IsNecromancer)
                            || pc.Is(CustomRoles.Madmate)
                            )
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Coven:
                        if (((pc.Is(Custom_Team.Coven) || pc.Is(CustomRoles.Enchanted) || Main.PlayerStates[pc.PlayerId].IsNecromancer) && (countType == CountTypes.Coven || pc.Is(CustomRoles.Soulless)))
                            || pc.Is(CustomRoles.Enchanted)
                           )
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Apocalypse:
                        if ((pc.IsNeutralApocalypse()) && (countType == CountTypes.Apocalypse || pc.Is(CustomRoles.Soulless) && !Main.PlayerStates[pc.PlayerId].IsNecromancer))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Cultist:
                        if ((pc.Is(CustomRoles.Cultist) && (countType == CountTypes.Cultist || pc.Is(CustomRoles.Soulless)) && !Main.PlayerStates[pc.PlayerId].IsNecromancer)
                            || pc.Is(CustomRoles.Charmed))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.CursedSoul:
                        if (pc.Is(CustomRoles.Soulless))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Infectious:
                        if ((pc.Is(CustomRoles.Infectious) && (countType == CountTypes.Infectious || pc.Is(CustomRoles.Soulless)) && !Main.PlayerStates[pc.PlayerId].IsNecromancer)
                            || pc.Is(CustomRoles.Infected))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.PlagueDoctor:
                        if (pc.Is(CustomRoles.PlagueDoctor))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Virus:
                        if ((pc.Is(CustomRoles.Virus) && (countType == CountTypes.Virus || pc.Is(CustomRoles.Soulless)) && !Main.PlayerStates[pc.PlayerId].IsNecromancer)
                            || pc.Is(CustomRoles.Contagious))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Jackal:
                        if (((pc.Is(CustomRoles.Sidekick) || pc.Is(CustomRoles.Jackal)) && (countType == CountTypes.Jackal || pc.Is(CustomRoles.Soulless)) && !Main.PlayerStates[pc.PlayerId].IsNecromancer)
                            || pc.Is(CustomRoles.Recruit))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                    case CustomWinner.Spiritcaller:
                        if (pc.Is(CustomRoles.EvilSpirit))
                        {
                            WinnerIds.Add(pc.PlayerId);
                        }
                        break;
                }
            }

            if (WinnerTeam is CustomWinner.RuthlessRomantic)
            {
                Romantic.BetPlayer.Do(x =>
                {
                    if (Main.PlayerStates[x.Key].MainRole == CustomRoles.RuthlessRomantic
                        && WinnerIds.Contains(x.Key) && !WinnerIds.Contains(x.Value))
                        WinnerIds.Add(x.Value);
                });
            }

            if (WinnerTeam is not CustomWinner.Draw and not CustomWinner.None and not CustomWinner.Error)
            {
                foreach (PlayerControl pc in Main.AllPlayerControls)
                {
                    switch (pc.GetCustomRole())
                    {
                        case CustomRoles.Stalker when pc.IsAlive() && ((WinnerTeam == CustomWinner.Impostor && !reason.Equals(GameOverReason.ImpostorsBySabotage)) || WinnerTeam == CustomWinner.Stalker
                            || (WinnerTeam == CustomWinner.Crewmate && !reason.Equals(GameOverReason.CrewmatesByTask) && Stalker.IsWinKill[pc.PlayerId] && Stalker.SnatchesWins)):
                            if (!CheckForConvertedWinner(pc.PlayerId))
                            {
                                reason = GameOverReason.ImpostorsByKill;
                                ResetAndSetWinner(CustomWinner.Stalker);
                                WinnerIds.Add(pc.PlayerId);
                            }
                            break;
                        case CustomRoles.Specter when pc.GetPlayerTaskState().IsTaskFinished && !pc.IsAlive() && Specter.SnatchesWin.GetBool():
                            reason = GameOverReason.ImpostorsByKill;
                            if (!CheckForConvertedWinner(pc.PlayerId))
                            {
                                ResetAndSetWinner(CustomWinner.Specter);
                                WinnerIds.Add(pc.PlayerId);
                            }
                            break;
                        case CustomRoles.Quizmaster when pc.IsAlive() && !Quizmaster.CanKillsAfterMark() && WinnerTeam == CustomWinner.Default:
                            reason = GameOverReason.ImpostorsByKill;
                            if (!CheckForConvertedWinner(pc.PlayerId))
                            {
                                ResetAndSetWinner(CustomWinner.Quizmaster);
                                WinnerIds.Add(pc.PlayerId);
                            }
                            break;
                        case CustomRoles.CursedSoul when pc.IsAlive() && WinnerTeam == CustomWinner.Default:
                            reason = GameOverReason.ImpostorsByKill;
                            if (!CheckForConvertedWinner(pc.PlayerId))
                            {
                                ResetAndSetWinner(CustomWinner.CursedSoul);
                                WinnerRoles.Add(CustomRoles.Soulless);
                                WinnerIds.Add(pc.PlayerId);
                            }
                            else WinnerRoles.Add(CustomRoles.Soulless);
                            break;
                    }
                }

                // Egoist (Crewmate)
                if (WinnerTeam == CustomWinner.Crewmate)
                {
                    var egoistCrewArray = Main.AllAlivePlayerControls.Where(x => x != null && x.GetCustomRole().IsCrewmate() && x.Is(CustomRoles.Egoist)).ToArray();

                    if (egoistCrewArray.Length > 0)
                    {
                        reason = GameOverReason.ImpostorsByKill;
                        ResetAndSetWinner(CustomWinner.Egoist);

                        foreach (var egoistCrew in egoistCrewArray)
                        {
                            WinnerIds.Add(egoistCrew.PlayerId);
                        }
                    }
                }

                // Egoist (Impostor)
                if (WinnerTeam == CustomWinner.Impostor)
                {
                    var egoistImpArray = Main.AllAlivePlayerControls.Where(x => x != null && x.GetCustomRole().IsImpostor() && x.Is(CustomRoles.Egoist)).ToArray();

                    if (egoistImpArray.Length > 0)
                    {
                        reason = GameOverReason.ImpostorsByKill;
                        ResetAndSetWinner(CustomWinner.Egoist);

                        foreach (var egoistImp in egoistImpArray)
                        {
                            WinnerIds.Add(egoistImp.PlayerId);
                        }
                    }
                }

                if (CustomRoles.God.RoleExist())
                {
                    var godArray = Main.AllAlivePlayerControls.Where(x => x.Is(CustomRoles.God));

                    if (godArray.Any())
                    {
                        bool isGodWinConverted = false;
                        foreach (var god in godArray.ToArray())
                        {
                            if (CheckForConvertedWinner(god.PlayerId))
                            {
                                isGodWinConverted = true;
                                break;
                            }
                        }
                        if (!isGodWinConverted)
                        {
                            ResetAndSetWinner(CustomWinner.God);
                            godArray.Do(p => WinnerIds.Add(p.PlayerId));
                        }
                    }
                }

                if (CustomRoles.Lovers.RoleExist() && !reason.Equals(GameOverReason.CrewmatesByTask))
                {
                    if (!(!Main.LoversPlayers.ToArray().All(p => p.IsAlive()) && Options.LoverSuicide.GetBool()))
                    {
                        if (WinnerTeam is CustomWinner.Crewmate or CustomWinner.Impostor or CustomWinner.Jackal or CustomWinner.Pelican)
                        {
                            ResetAndSetWinner(CustomWinner.Lovers);
                            Main.AllPlayerControls
                                .Where(p => p.Is(CustomRoles.Lovers))
                                .Do(p => WinnerIds.Add(p.PlayerId));
                        }
                    }
                }


                if (Main.AllAlivePlayerControls.All(p => p.IsNeutralApocalypse()))
                {
                    foreach (var pc in Main.AllPlayerControls.Where(x => x.IsNeutralApocalypse() && !Main.PlayerStates[x.PlayerId].IsNecromancer))
                    {
                        if (!WinnerIds.Contains(pc.PlayerId))
                            WinnerIds.Add(pc.PlayerId);
                    }
                }
                if (Main.AllAlivePlayerControls.All(p => p.IsPlayerCoven() || p.Is(CustomRoles.Enchanted)))
                {
                    foreach (var pc in Main.AllPlayerControls.Where(x => x.IsPlayerCoven() || x.Is(CustomRoles.Enchanted) || Main.PlayerStates[x.PlayerId].IsNecromancer))
                    {
                        if (!WinnerIds.Contains(pc.PlayerId))
                            WinnerIds.Add(pc.PlayerId);
                    }
                }

                if (WinnerTeam is CustomWinner.Youtuber)
                {
                    var youTuber = Main.AllPlayerControls.FirstOrDefault(x => x.Is(CustomRoles.Youtuber) && WinnerIds.Contains(x.PlayerId));

                    if (youTuber != null && Youtuber.KillerWinsWithYouTuber.GetBool())
                    {
                        var realKiller = youTuber.GetRealKiller();

                        if (realKiller != null && !WinnerIds.Contains(realKiller.PlayerId))
                            WinnerIds.Add(realKiller.PlayerId);
                    }
                }

                //Neutral Win Together
                if (Options.NeutralWinTogether.GetBool() && !WinnerIds.Any(x => Utils.GetPlayerById(x) != null && (Utils.GetPlayerById(x).GetCustomRole().IsCrewmate() || Utils.GetPlayerById(x).GetCustomRole().IsImpostor() || Utils.GetPlayerById(x).GetCustomRole().IsCoven()) && !Main.PlayerStates[x].IsNecromancer))
                {
                    foreach (var pc in Main.AllPlayerControls)
                        if (pc.GetCustomRole().IsNeutral() && !pc.GetCustomRole().IsMadmate() && !WinnerIds.Contains(pc.PlayerId) && !WinnerRoles.Contains(pc.GetCustomRole()))
                            WinnerIds.Add(pc.PlayerId);
                }
                else if (!Options.NeutralWinTogether.GetBool() && Options.NeutralRoleWinTogether.GetBool())
                {
                    foreach (var id in WinnerIds)
                    {
                        var pc = Utils.GetPlayerById(id);
                        if (pc == null || !(pc.GetCustomRole().IsNeutral() && !pc.GetCustomRole().IsMadmate()) || Main.PlayerStates[pc.PlayerId].IsNecromancer) continue;

                        foreach (var tar in Main.AllPlayerControls)
                            if (!WinnerIds.Contains(tar.PlayerId) && tar.GetCustomRole() == pc.GetCustomRole())
                                WinnerIds.Add(tar.PlayerId);
                    }
                }

                //Remove hurried task not done player from winner id
                foreach (var pc in Main.AllPlayerControls.Where(x => x.Is(CustomRoles.Hurried)).ToArray())
                {
                    if (!Hurried.CheckWinState(pc) && WinnerIds.Contains(pc.PlayerId))
                    {
                        WinnerIds.Remove(pc.PlayerId);
                        Logger.Info($"Removed {pc.GetNameWithRole()} from winner ids", "Hurried Win Check");
                    }
                }

                for (int i = 0; i < Main.AllPlayerControls.Length + 1; i++)
                {
                    CheckAdditionalWinners();
                    if (i == Main.AllPlayerControls.Length)
                    {
                        if (AdditionalWinnerTeams.Any()) Logger.Info($"Additional winners: {string.Join(", ", AdditionalWinnerTeams)}", "CheckAdditionalWinner");
                        else Logger.Info($"No additional winners", "CheckAdditionalWinner");
                    }
                }

                static void CheckAdditionalWinners()
                {
                    foreach (var pc in Main.AllPlayerControls)
                    {
                        if (WinnerIds.Contains(pc.PlayerId)) continue;
                        switch (pc.GetCustomRole())
                        {
                            case CustomRoles.Opportunist when pc.IsAlive():
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Opportunist);
                                break;
                            case CustomRoles.Pixie when !CheckForConvertedWinner(pc.PlayerId):
                                Pixie.PixieWinCondition(pc);
                                break;
                            case CustomRoles.Shaman when pc.IsAlive():
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Shaman);
                                break;
                            case CustomRoles.Taskinator when pc.IsAlive() && WinnerTeam != CustomWinner.Crewmate:
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Taskinator);
                                break;
                            case CustomRoles.Pursuer when pc.IsAlive() && WinnerTeam is not CustomWinner.Jester and not CustomWinner.Lovers and not CustomWinner.Terrorist and not CustomWinner.Executioner and not CustomWinner.Collector and not CustomWinner.Innocent and not CustomWinner.Youtuber:
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Pursuer);
                                break;
                            case CustomRoles.Sunnyboy when !pc.IsAlive():
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Sunnyboy);
                                break;
                            case CustomRoles.Maverick when pc.IsAlive() && pc.GetAbilityUseLimit() >= Maverick.MinKillsForWin.GetInt():
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Maverick);
                                break;
                            case CustomRoles.Specter when !Specter.SnatchesWin.GetBool() && !pc.IsAlive() && pc.GetPlayerTaskState().IsTaskFinished:
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Specter);
                                break;
                            case CustomRoles.Provocateur:
                                if (Provocateur.Provoked.TryGetValue(pc.PlayerId, out var tarId) && !WinnerIds.Contains(tarId))
                                {
                                    WinnerIds.Add(pc.PlayerId);
                                    AdditionalWinnerTeams.Add(AdditionalWinners.Provocateur);
                                }
                                break;
                            case CustomRoles.Hater when Hater.isWon:
                                AdditionalWinnerTeams.Add(AdditionalWinners.Hater);
                                // You have a player id list, no need for another list; also use a for loop instead of LINQ
                                //Hater.winnerHaterList.Do(x => WinnerIds.Add(x));

                                var HaterArray = Hater.playerIdList.ToArray();
                                foreach (var Hater in HaterArray)
                                {
                                    WinnerIds.Add(Hater);
                                }
                                break;
                            case CustomRoles.Troller when pc.IsAlive():
                                AdditionalWinnerTeams.Add(AdditionalWinners.Troller);
                                WinnerIds.Add(pc.PlayerId);
                                break;
                            case CustomRoles.Romantic:
                                if (Romantic.BetPlayer.TryGetValue(pc.PlayerId, out var betTarget)
                                    && (WinnerIds.Contains(betTarget) || (Main.PlayerStates.TryGetValue(betTarget, out var betTargetPS) && WinnerRoles.Contains(betTargetPS.MainRole))))
                                {
                                    WinnerIds.Add(pc.PlayerId);
                                    AdditionalWinnerTeams.Add(AdditionalWinners.Romantic);
                                }
                                break;
                            case CustomRoles.VengefulRomantic when VengefulRomantic.hasKilledKiller:
                                WinnerIds.Add(pc.PlayerId);
                                WinnerIds.Add(Romantic.BetPlayer[pc.PlayerId]);
                                AdditionalWinnerTeams.Add(AdditionalWinners.VengefulRomantic);
                                break;
                            case CustomRoles.Lawyer:
                                if (pc.GetRoleClass() is Lawyer lawerClass)
                                {
                                    var lawyertarget = lawerClass.GetTargetId();
                                    if (WinnerIds.Contains(lawyertarget)
                                        || (Main.PlayerStates.TryGetValue(lawyertarget, out var lawyerTargetPS) && WinnerRoles.Contains(lawyerTargetPS.MainRole)))
                                    {
                                        WinnerIds.Add(pc.PlayerId);
                                        AdditionalWinnerTeams.Add(AdditionalWinners.Lawyer);
                                    }
                                }
                                break;
                            case CustomRoles.Follower when Follower.BetPlayer.TryGetValue(pc.PlayerId, out var followerTarget)
                               && (WinnerIds.Contains(followerTarget) || (Main.PlayerStates.TryGetValue(followerTarget, out var followerTargetPS) && WinnerRoles.Contains(followerTargetPS.MainRole))):
                                WinnerIds.Add(pc.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Follower);
                                break;
                        }
                    }

                    //Lovers follow winner
                    if (WinnerTeam is not CustomWinner.Lovers)
                    {
                        var loverArray = Main.AllPlayerControls.Where(x => x.Is(CustomRoles.Lovers)).ToArray();

                        foreach (var lover in loverArray)
                        {
                            if (WinnerIds.Any(x => Utils.GetPlayerById(x).Is(CustomRoles.Lovers)) && !WinnerIds.Contains(lover.PlayerId))
                            {
                                WinnerIds.Add(lover.PlayerId);
                                AdditionalWinnerTeams.Add(AdditionalWinners.Lovers);
                            }
                        }
                    }

                    if (WinnerTeam == CustomWinner.Lovers || AdditionalWinnerTeams.Contains(AdditionalWinners.Lovers))
                    {
                        Main.AllPlayerControls
                            .Where(p => p.Is(CustomRoles.Lovers) && !WinnerIds.Contains(p.PlayerId))
                            .Do(p => WinnerIds.Add(p.PlayerId));
                    }

                    /*Keep Schrodinger cat win condition at last*/
                    Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.SchrodingersCat)).ToList().ForEach(SchrodingersCat.SchrodingerWinCondition);
                }
            }

            ShipStatus.Instance.enabled = false;

            Logger.Info($"Final WinnerTeam: {WinnerTeam}", "CheckEndCriteriaForNormal.Prefix");
            Logger.Info($"WinnerIds: {string.Join(", ", WinnerIds)}", "CheckEndCriteriaForNormal.Prefix");
            // When crewmates win, show as impostor win, for displaying all names players
            //reason = reason is GameOverReason.HumansByVote or GameOverReason.HumansByTask ? GameOverReason.ImpostorByVote : reason;
            StartEndGame(reason);
            predicate = null;
        }
        return false;
    }
    public static void StartEndGame(GameOverReason reason)
    {
        // Sync of CustomWinnerHolder info
        var msg = new RpcEndGame(PlayerControl.LocalPlayer.NetId, WinnerTeam, AdditionalWinnerTeams, WinnerRoles, WinnerIds);
        RpcUtils.LateBroadcastReliableMessage(msg);


        AmongUsClient.Instance.StartCoroutine(CoEndGame(AmongUsClient.Instance, reason).WrapToIl2Cpp());
    }
    public static bool ForEndGame = false;
    private static IEnumerator CoEndGame(AmongUsClient self, GameOverReason reason)
    {
        CustomRoleManager.AllEnabledRoles.Do(roleClass => roleClass.OnCoEndGame());
        ForEndGame = true;
        CovenManager.necroHolder = byte.MaxValue;

        // Set ghost role
        List<byte> ReviveRequiredPlayerIds = [];
        var winner = WinnerTeam;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (winner == CustomWinner.Draw)
            {
                SetGhostRole(ToGhostImpostor: true);
                continue;
            }
            bool canWin = WinnerIds.Contains(pc.PlayerId) ||
                    WinnerRoles.Contains(pc.GetCustomRole());
            bool isCrewmateWin = reason.Equals(GameOverReason.CrewmatesByVote) || reason.Equals(GameOverReason.CrewmatesByTask);
            SetGhostRole(ToGhostImpostor: canWin ^ isCrewmateWin);
            continue;

            void SetGhostRole(bool ToGhostImpostor)
            {
                var isDead = pc.Data.IsDead;
                if (!isDead) ReviveRequiredPlayerIds.Add(pc.PlayerId);

                if (ToGhostImpostor)
                {
                    Logger.Info($"{pc.GetNameWithRole().RemoveHtmlTags()}: changed to ImpostorGhost", "ResetRoleAndEndGame");
                    pc.RpcSetRole(RoleTypes.ImpostorGhost);
                }
                else
                {
                    Logger.Info($"{pc.GetNameWithRole().RemoveHtmlTags()}: changed to CrewmateGhost", "ResetRoleAndEndGame");
                    pc.RpcSetRole(RoleTypes.CrewmateGhost);
                }
                // Put it back on so it can't be auto-muted during the delay until resuscitation ~~ TOH comment
                pc.Data.IsDead = isDead;
            }
        }

        // Remember true win to display in chat
        SetEverythingUpPatch.LastWinsReason = winner is CustomWinner.Crewmate or CustomWinner.Impostor ? GetString($"GameOverReason.{reason}") : "";
        Utils.NotifyGameEnding();

        // Delay to ensure that resuscitation is delivered after the ghost roll setting
        yield return new WaitForSeconds(0.2f);

        if (ReviveRequiredPlayerIds.Count > 0)
        {
            // Resuscitation Resuscitate one person per transmission to prevent the packet from swelling up and dying
            for (int i = 0; i < ReviveRequiredPlayerIds.Count; i++)
            {
                var playerId = ReviveRequiredPlayerIds[i];
                var playerInfo = GameData.Instance.GetPlayerById(playerId);
                // revive player
                playerInfo.IsDead = false;
            }
            // sync game data
            Utils.SendGameData();
            // Delay to ensure that the end of the game is delivered at the end of the game
            yield return new WaitForSeconds(0.3f);
        }

        foreach (var winnerId in WinnerIds)
        {
            var winnerPC = winnerId.GetPlayer();
            if (winnerPC == null) continue;

            // Update winner name
            Utils.NotifyRoles(SpecifyTarget: winnerPC, NoCache: true);
        }

        // Start End Game
        GameManager.Instance.RpcEndGame(reason, false);
    }

    public static void SetPredicateToNormal() => predicate = new NormalGameEndPredicate();
    public static void SetPredicateToFFA() => predicate = new FFAGameEndPredicate();
    public static void SetPredicateToSpeedRun() => predicate = new SpeedRunGameEndPredicate();


    // ===== Check Game End =====
    // For Normal Games
    class NormalGameEndPredicate : GameEndPredicate
    {
        public override bool CheckForEndGame(out GameOverReason reason)
        {
            reason = GameOverReason.ImpostorsByKill;
            if (WinnerTeam != CustomWinner.Default) return false;
            if (CheckGameEndByLivingPlayers(out reason) || CheckGameEndByTask(out reason) || CheckGameEndBySabotage(out reason)) return true;
            return false;
        }

        public static bool CheckGameEndByLivingPlayers(out GameOverReason reason)
        {
            reason = GameOverReason.ImpostorsByKill;

            if (Sunnyboy.HasEnabled && Sunnyboy.CheckGameEnd()) return false;
            var neutralRoleCounts = new Dictionary<CountTypes, int>();
            var allAlivePlayerList = Main.AllAlivePlayerControls.ToArray();
            int dual = 0, impCount = 0, crewCount = 0, covenCount = 0;

            foreach (var pc in allAlivePlayerList)
            {
                if (pc == null) continue;

                dual = Paranoia.IsExistInGame(pc) ? 1 : 0;
                var countType = Main.PlayerStates[pc.PlayerId].countTypes;
                switch (countType)
                {
                    case CountTypes.OutOfGame:
                    case CountTypes.None:
                        continue;
                    case CountTypes.Impostor:
                        impCount++;
                        impCount += dual;
                        break;
                    case CountTypes.Crew:
                        crewCount++;
                        crewCount += dual;
                        break;
                    case CountTypes.Coven:
                        covenCount++;
                        covenCount += dual;
                        break;
                    default:
                        if (neutralRoleCounts.ContainsKey(countType))
                            neutralRoleCounts[countType]++;
                        else
                            neutralRoleCounts[countType] = 1;
                        neutralRoleCounts[countType] += dual;
                        break;
                }
            }

            int totalNKAlive = neutralRoleCounts.Sum(kvp => kvp.Value);

            if (crewCount == 0 && impCount == 0 && totalNKAlive == 0 && covenCount == 0) // Everyone is dead
            {
                reason = GameOverReason.ImpostorsByKill;
                ResetAndSetWinner(CustomWinner.None);
                return true;
            }

            else if (Main.AllAlivePlayerControls.Length > 0 && Main.AllAlivePlayerControls.All(p => p.Is(CustomRoles.Lovers))) // if lover is alive lover wins
            {
                reason = GameOverReason.ImpostorsByKill;
                ResetAndSetWinner(CustomWinner.Lovers);
                return true;
            }


            else if (totalNKAlive == 0 && covenCount == 0) // total number of nks alive 0
            {
                if (crewCount <= impCount) // Crew less than or equal to Imps, Imp wins
                {
                    reason = GameOverReason.ImpostorsByKill;
                    ResetAndSetWinner(CustomWinner.Impostor);
                }

                else if (impCount == 0) // Remaining Imps are 0, Crew wins (neutral is already dead)
                {
                    reason = GameOverReason.CrewmatesByVote;
                    ResetAndSetWinner(CustomWinner.Crewmate);
                }

                else if (crewCount > impCount) return false; // crewmate is more than imp (the game must continue)
                return true;
            }
            else
            {
                if (impCount >= 1) return false; // Both Imp and NK or Coven are alive, the game must continue

                if (totalNKAlive >= 1 && covenCount >= 1) return false; // Both Coven and NK are alive, the game must continue

                // One of NK or Coven all dead here, check nk and coven count > crew

                if (crewCount <= covenCount && totalNKAlive == 0) // Imps dead, NK dead, Crew <= Coven, Coven wins
                {
                    reason = GameOverReason.ImpostorsByKill;
                    ResetAndSetWinner(CustomWinner.Coven);
                    return true;
                }

                else if (crewCount <= totalNKAlive && covenCount == 0) // Imps dead, Coven dead, Crew <= NK, Check NK win
                {
                    var winners = neutralRoleCounts.Where(kvp => kvp.Value == totalNKAlive).ToArray();
                    var winnnerLength = winners.Length;
                    if (winnnerLength == 1)
                    {
                        // Only 1 NK team alive, NK wins
                        try
                        {
                            var winnerRole = winners.First().Key.GetNeutralCustomRoleFromCountType();
                            reason = GameOverReason.ImpostorsByKill;
                            ResetAndSetWinner(winnerRole.GetNeutralCustomWinnerFromRole());
                            WinnerRoles.Add(winnerRole);
                        }
                        catch
                        {
                            Logger.Warn("Error while trying to end game as single NK", "CheckGameEndByLivingPlayers");
                            return false;
                        }

                        return true;
                    }
                    else
                    {
                        return false; // Not all alive neutrals were in one team
                    }
                }

                else return false; // Crew > Coven or NK, the game must continue
            }
        }
    }
}

// For FFA Games
class FFAGameEndPredicate : GameEndPredicate
{
    public override bool CheckForEndGame(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (WinnerIds.Count > 0) return false;
        if (CheckGameEndByLivingPlayers(out reason)) return true;
        return false;
    }

    public static bool CheckGameEndByLivingPlayers(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;

        if (FFAManager.RoundTime <= 0)
        {
            var winner = Main.AllPlayerControls.Where(x => !x.Is(CustomRoles.GM) && x != null).OrderBy(x => FFAManager.GetRankOfScore(x.PlayerId)).First();

            byte winnerId;
            if (winner == null) winnerId = 0;
            else winnerId = winner.PlayerId;

            Logger.Warn($"Winner: {Utils.GetPlayerById(winnerId).GetRealName().RemoveHtmlTags()}", "FFA");

            WinnerIds = [winnerId];

            Main.DoBlockNameChange = true;

            return true;
        }
        else if (Main.AllAlivePlayerControls.Length == 1)
        {
            var winner = Main.AllAlivePlayerControls.FirstOrDefault();

            Logger.Info($"Winner: {winner.GetRealName().RemoveHtmlTags()}", "FFA");

            WinnerIds = [winner.PlayerId];

            Main.DoBlockNameChange = true;

            return true;
        }
        else if (Main.AllAlivePlayerControls.Length == 0)
        {
            FFAManager.RoundTime = 0;
            Logger.Warn("No players alive. Force ending the game", "FFA");
            return false;
        }
        else return false;
    }
}

public abstract class GameEndPredicate
{
    /// <summary>Checks the game end condition and stores the value in CustomWinnerHolder</summary>
    /// <params name="reason">GameOverReason used for vanilla game end processing</params>
    /// <returns>For the game end condition</returns>
    public abstract bool CheckForEndGame(out GameOverReason reason);

    /// <summary>Determine if a task win is possible based on GameData.TotalTasks and CompletedTasks</summary>
    public virtual bool CheckGameEndByTask(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (Options.DisableTaskWin.GetBool() || TaskState.InitialTotalTasks == 0) return false;
        if (Options.DisableTaskWinIfAllCrewsAreDead.GetBool() && !Main.AllAlivePlayerControls.Any(x => x.Is(Custom_Team.Crewmate))) return false;
        if (Options.DisableTaskWinIfAllCrewsAreConverted.GetBool() && Main.AllPlayerControls
            .Where(x => x.Is(Custom_Team.Crewmate) && x.GetCustomRole().GetRoleTypes() is RoleTypes.Crewmate or RoleTypes.Engineer or RoleTypes.Scientist or RoleTypes.Noisemaker or RoleTypes.Tracker or RoleTypes.CrewmateGhost or RoleTypes.GuardianAngel)
            .All(x => x.GetCustomSubRoles().Any(y => y.IsConverted()))) return false;

        if (GameData.Instance.TotalTasks <= GameData.Instance.CompletedTasks)
        {
            reason = GameOverReason.CrewmatesByTask;
            ResetAndSetWinner(CustomWinner.Crewmate);
            Logger.Info($"Game End By Completed All Tasks", "CheckGameEndBySabotage");
            return true;
        }
        return false;
    }
    /// <summary>Determines if a sabotage win is possible based on the elements in ShipStatus.Systems</summary>
    public virtual bool CheckGameEndBySabotage(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (ShipStatus.Instance.Systems == null) return false;

        // TryGetValue is not available
        var systems = ShipStatus.Instance.Systems;
        LifeSuppSystemType LifeSupp;
        if (systems.ContainsKey(SystemTypes.LifeSupp) && // Confirmation of the existence of sabotage
            (LifeSupp = systems[SystemTypes.LifeSupp].CastFast<LifeSuppSystemType>()) != null && // Castable Confirmation
            LifeSupp.Countdown < 0f) // Time-up confirmation
        {
            ResetAndSetWinner(CustomWinner.Impostor);
            reason = GameOverReason.ImpostorsBySabotage;
            LifeSupp.Countdown = 10000f;
            Logger.Info($"Game End By LifeSupp Sabotage", "CheckGameEndBySabotage");
            return true;
        }

        ISystemType sys = null;
        if (systems.ContainsKey(SystemTypes.Reactor)) sys = systems[SystemTypes.Reactor];
        else if (systems.ContainsKey(SystemTypes.Laboratory)) sys = systems[SystemTypes.Laboratory];
        else if (systems.ContainsKey(SystemTypes.HeliSabotage)) sys = systems[SystemTypes.HeliSabotage];

        ICriticalSabotage critical;
        if (sys != null && // Confirmation of the existence of sabotage
            (critical = sys.CastFast<ICriticalSabotage>()) != null && // Castable Confirmation
            critical.Countdown < 0f) // Time-up confirmation
        {
            ResetAndSetWinner(CustomWinner.Impostor);
            reason = GameOverReason.ImpostorsBySabotage;
            critical.ClearSabotage();
            Logger.Info($"Game End By Critical Sabotage", "CheckGameEndBySabotage");
            return true;
        }

        return false;
    }
}
