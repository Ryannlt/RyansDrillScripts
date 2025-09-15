using HoldfastSharedMethods;
using NUnit.Framework;
using UnityEngine;
using MDS.Systems;

[TestFixture]
public class StateTrackerTests
{
    [SetUp]
    public void Setup()
    {
        StateTracker.OnIsServer(true);

        StateTracker.OnRoundDetails(
            roundId: 1,
            serverName: "TestServer",
            mapName: "TestMap",
            attackingFaction: FactionCountry.French,
            defendingFaction: FactionCountry.British,
            gameplayMode: GameplayMode.ArmyAssault,
            gameType: GameType.Standard
        );
    }

    [Test]
    public void Test_OnIsServer_SetsServerFlagOnce()
    {
        Assert.IsTrue(StateTracker.IsServer);
    }

    [Test]
    public void Test_OnPlayerJoined_AddsHumanToSpectators()
    {
        StateTracker.OnPlayerJoined(1, 12345, "TestHuman", "Reg", false);
        var player = StateTracker.GetPlayerById(1);

        Assert.NotNull(player);
        Assert.IsFalse(player.IsBot);
        CollectionAssert.Contains(StateTracker.SpectatorPlayers, player);
    }

    [Test]
    public void Test_OnPlayerJoined_AddsBotButNotToSpectators()
    {
        StateTracker.OnPlayerJoined(2, 0, "TestBot", "", true);
        var bot = StateTracker.GetPlayerById(2);

        Assert.NotNull(bot);
        Assert.IsTrue(bot.IsBot);
        CollectionAssert.DoesNotContain(StateTracker.SpectatorPlayers, bot);
    }

    [Test]
    public void Test_OnPlayerSpawned_AssignsToCorrectTeam()
    {
        StateTracker.OnPlayerJoined(3, 9999, "Soldier", "A", false);
        StateTracker.OnPlayerSpawned(3, 0, FactionCountry.French, PlayerClass.ArmyInfantryOfficer, 1, new GameObject());

        var player = StateTracker.GetPlayerById(3);
        Assert.NotNull(player);
        CollectionAssert.Contains(StateTracker.AttackingPlayers, player);
        CollectionAssert.DoesNotContain(StateTracker.SpectatorPlayers, player);
    }

    [Test]
    public void Test_OnPlayerEnterSpectatorMode_MovesToSpectatorList()
    {
        StateTracker.OnPlayerJoined(4, 4567, "Spectator", "R", false);
        StateTracker.OnPlayerSpawned(4, 0, FactionCountry.British, PlayerClass.Surgeon, 1, new GameObject());

        var player = StateTracker.GetPlayerById(4);
        StateTracker.OnPlayerEnterSpectatorMode(4);

        CollectionAssert.Contains(StateTracker.SpectatorPlayers, player);
        CollectionAssert.DoesNotContain(StateTracker.AttackingPlayers, player);
        CollectionAssert.DoesNotContain(StateTracker.DefendingPlayers, player);
    }

    [Test]
    public void Test_OnPlayerDisconnected_RemovesFromAllLists()
    {
        StateTracker.OnPlayerJoined(5, 1111, "DisconnectMe", "", false);
        StateTracker.OnPlayerSpawned(5, 1, FactionCountry.French, PlayerClass.Musician, 2, new GameObject());

        var player = StateTracker.GetPlayerById(5);
        Assert.NotNull(player);

        StateTracker.OnPlayerDisconnected(5);

        Assert.Null(StateTracker.GetPlayerById(5));
        CollectionAssert.DoesNotContain(StateTracker.AllPlayers, player);
        CollectionAssert.DoesNotContain(StateTracker.AttackingPlayers, player);
        CollectionAssert.DoesNotContain(StateTracker.SpectatorPlayers, player);
    }

    [Test]
    public void Test_OnRoundDetails_ClearsBots_AndPreservesPlayers()
    {
        StateTracker.OnPlayerJoined(6, 2222, "Human", "", false);
        StateTracker.OnPlayerJoined(7, 0, "BotToClear", "", true);

        StateTracker.OnRoundDetails(2, "NewServer", "Map2", FactionCountry.Prussian, FactionCountry.Russian, GameplayMode.ArmyAssault, GameType.Standard);

        var human = StateTracker.GetPlayerById(6);
        var bot = StateTracker.GetPlayerById(7);

        Assert.NotNull(human);
        Assert.Null(bot);
        CollectionAssert.Contains(StateTracker.SpectatorPlayers, human);
    }
}
