using HoldfastSharedMethods;
using UnityEngine;

namespace MDS.Systems
{
    public interface IPlayer
    {
        int PlayerId { get; }
        string PlayerName { get; }
        string RegimentTag { get; }
        bool IsBot { get; }
        bool IsAdmin { get; }

        void AssignSpawnDetails(int spawnSectionId, FactionCountry faction, PlayerClass playerClass, int uniformId, GameObject playerObject);
    }
}
