using System;
using System.Collections;
using UnityEngine;
using MDS.Core;

namespace MDS.Systems
{
    // Resolves where a summon should land for a given caller.
    //
    //  - Embodied (alive and spawned): the caller's own transform, resolved immediately, unchanged behavior.
    //  - Free roam or spectating: the caller's viewpoint, taken from their next packet and dropped to the ground
    //    (camera position when the server gets it, otherwise the reported owner position).
    //
    // The second case exists because PlayerObject survives death as the corpse, so summoning from the free camera
    // used to teleport bots to the dead body. StateTracker.IsSpectator is the right test: free-flight, spectate,
    // and not-yet-spawned callers all land in the spectator list (OnStartFreeflight, OnStartSpectate, and
    // OnPlayerJoined feed it, and OnPlayerSpawned removes them again).
    //
    // The free-roam path is asynchronous: it resolves on the caller's next packet, the same trade-off the
    // arena-corner commands already accept. If the client sends no packet, onResolved never fires.
    public static class SummonOrigin
    {
        // Below this planar length the camera points almost straight up/down and has no usable yaw.
        private const float MinPlanarForwardSqr = 1e-4f;

        // How long to wait for a packet before giving up on a free-roam summon.
        private const float ViewpointTimeoutSeconds = 2f;

        // targetPlayerId: when given ('at <playerId>'), place at that player instead of the caller. This is the
        // practical stand-in for free-roam summoning, which the packet route cannot serve.
        public static void Resolve(int playerId, int? targetPlayerId, Action<BotPlacement> onResolved, Action<string> onFailed)
        {
            if (targetPlayerId.HasValue)
            {
                ResolveAtPlayer(targetPlayerId.Value, onResolved, onFailed);
                return;
            }

            if (StateTracker.IsSpectator(playerId))
            {
                ResolveFromViewpoint(playerId, onResolved, onFailed);
                return;
            }

            IPlayer caller = StateTracker.GetPlayerById(playerId);
            if (caller?.PlayerObject == null)
            {
                onFailed("Cannot summon - your position is unavailable (are you spawned?).");
                return;
            }

            Transform t = caller.PlayerObject.transform;
            onResolved(new BotPlacement(t.position, t.eulerAngles.y));
        }

        // Origin at another player's live position. Synchronous, since their transform is readable directly with
        // no packet needed. Refuses a target who isn't currently embodied, since PlayerObject would then be a
        // stale corpse, the same trap the caller path avoids.
        private static void ResolveAtPlayer(int targetPlayerId, Action<BotPlacement> onResolved, Action<string> onFailed)
        {
            IPlayer target = StateTracker.GetPlayerById(targetPlayerId);
            if (target == null)
            {
                onFailed($"Cannot summon - no player with id {targetPlayerId}.");
                return;
            }

            if (target.PlayerObject == null || StateTracker.IsSpectator(targetPlayerId))
            {
                onFailed($"Cannot summon - player {targetPlayerId} ({target.PlayerName}) is not currently spawned.");
                return;
            }

            Transform t = target.PlayerObject.transform;
            onResolved(new BotPlacement(t.position, t.eulerAngles.y));
        }

        // Free roam: place directly below the viewpoint (its X,Z dropped to the ground), facing the view direction.
        //
        // CameraPosition is preferred, but the server isn't always given camera fields for a free-flying or
        // spectating player (they come back null), so OwnerPosition is the fallback; in free flight it tracks the
        // flying viewpoint rather than the corpse. Which fields actually arrived is logged, because this varies by
        // mode and is the first thing to check if placement looks wrong.
        private static void ResolveFromViewpoint(int playerId, Action<BotPlacement> onResolved, Action<string> onFailed)
        {
            Logger.Log($"Awaiting viewpoint of player {playerId} to resolve summon origin...", LogLevel.DEBUG);

            bool answered = false;

            PlayerPacketAwaiter.WaitForPacket(playerId, packet =>
            {
                answered = true;

                Logger.Log($"Summon origin packet for {playerId}: camPos={packet.CameraPosition.HasValue}, " +
                           $"ownerPos={packet.OwnerPosition.HasValue}, camFwd={packet.CameraForward.HasValue}, " +
                           $"yaw={packet.OwnerYaw.HasValue}, rotY={packet.OwnerRotationY.HasValue}.", LogLevel.DEBUG);

                Vector3? viewpoint = packet.CameraPosition ?? packet.OwnerPosition;
                if (!viewpoint.HasValue)
                {
                    onFailed("Cannot summon - your position is unavailable while spectating.");
                    return;
                }

                Vector2 planar = new Vector2(viewpoint.Value.x, viewpoint.Value.z);
                Vector3 position = new Vector3(planar.x, TerrainSampler.GetYAt(planar), planar.y);

                onResolved(new BotPlacement(position, ViewHeading(packet)));
            });

            if (Application.isPlaying)
                MonoBehaviourRunner.Instance.StartCoroutine(FailIfNoPacket(playerId, () => answered, onFailed));
        }

        // A free-flying player can send NO packets at all. Without this the wait would sit queued and fire
        // much later - when they eventually respawn - summoning a bot at that moment instead of failing.
        private static IEnumerator FailIfNoPacket(int playerId, Func<bool> answered, Action<string> onFailed)
        {
            yield return new WaitForSeconds(ViewpointTimeoutSeconds);
            if (answered()) yield break;

            PlayerPacketAwaiter.CancelWait(playerId);
            Logger.Log($"Summon origin: no packet from player {playerId} within {ViewpointTimeoutSeconds}s (the server appears to receive none while free-flying).", LogLevel.WARNING);
            onFailed("Cannot summon - no position received while in free roam. Spawn in first, or place bots with 'rc spawnLine <x> <z> <rotation>'.");
        }

        // The heading (degrees from North) the viewpoint looks along: the camera forward when present,
        // otherwise the reported yaw, otherwise North. A near-vertical camera (looking straight down, likely
        // when placing from above) has no usable planar direction, so it falls through to the yaw.
        private static float ViewHeading(PlayerPacketData packet)
        {
            if (packet.CameraForward.HasValue)
            {
                Vector2 planar = new Vector2(packet.CameraForward.Value.x, packet.CameraForward.Value.z);
                if (planar.sqrMagnitude >= MinPlanarForwardSqr)
                    return MovementSolver.HeadingOf(planar);
            }

            if (packet.OwnerYaw.HasValue) return packet.OwnerYaw.Value;
            if (packet.OwnerRotationY.HasValue) return packet.OwnerRotationY.Value;
            return 0f;
        }
    }
}
