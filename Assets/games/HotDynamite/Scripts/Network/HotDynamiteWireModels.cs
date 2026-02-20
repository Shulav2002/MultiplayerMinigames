using System;
using System.Collections.Generic;
using UnityEngine;

namespace HotDynamite.Network
{
    [Serializable]
    public sealed class CreateRoomPayload
    {
        public string roomName;
        public int maxPlayers;
        public bool withBots;
    }

    [Serializable]
    public sealed class JoinRoomPayload
    {
        public string roomId;
    }

    [Serializable]
    public sealed class ReadyPayload
    {
        public string roomId;
        public bool ready;
    }

    [Serializable]
    public sealed class StartMatchPayload
    {
        public string roomId;
    }

    [Serializable]
    public sealed class InputPayload
    {
        public string roomId;
        public int seq;
        public float moveX;
        public float moveY;
        public long clientTimeMs;
    }

    [Serializable]
    public sealed class RoomStatePayload
    {
        public string roomId;
        public string youId;
        public int humanCount;
        public int botCount;
        public int minPlayers;
        public string[] players;
    }

    [Serializable]
    public sealed class MatchStartedPayload
    {
        public string roomId;
        public long startServerTimeMs;
    }

    [Serializable]
    public sealed class MatchEndedPayload
    {
        public string roomId;
        public string winnerId;
        public long endedServerTimeMs;
    }

    [Serializable]
    public sealed class ErrorPayload
    {
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class BombState
    {
        public string holderId;
        public float timeLeft;
    }

    [Serializable]
    public sealed class PlayerState
    {
        public string id;
        public float x;
        public float y;
        public float vx;
        public float vy;
        public bool alive;
        public bool hasBomb;
        public bool isBot;
        public int appearance;
    }

    [Serializable]
    public sealed class StateSnapshotPayload
    {
        public string roomId;
        public long tick;
        public long serverTimeMs;
        public string phase;
        public string winnerId;
        public BombState bomb;
        public List<PlayerState> players;
    }

    [Serializable]
    public sealed class CreateRoomMessage
    {
        public string type = "create_room";
        public CreateRoomPayload payload;
    }

    [Serializable]
    public sealed class JoinRoomMessage
    {
        public string type = "join_room";
        public JoinRoomPayload payload;
    }

    [Serializable]
    public sealed class ReadyMessage
    {
        public string type = "ready";
        public ReadyPayload payload;
    }

    [Serializable]
    public sealed class StartMatchMessage
    {
        public string type = "start_match";
        public StartMatchPayload payload;
    }

    [Serializable]
    public sealed class InputMessage
    {
        public string type = "input";
        public InputPayload payload;
    }

    [Serializable]
    public sealed class RoomStateMessage
    {
        public string type;
        public RoomStatePayload payload;
    }

    [Serializable]
    public sealed class MatchStartedMessage
    {
        public string type;
        public MatchStartedPayload payload;
    }

    [Serializable]
    public sealed class StateSnapshotMessage
    {
        public string type;
        public StateSnapshotPayload payload;
    }

    [Serializable]
    public sealed class MatchEndedMessage
    {
        public string type;
        public MatchEndedPayload payload;
    }

    [Serializable]
    public sealed class ErrorMessage
    {
        public string type;
        public ErrorPayload payload;
    }

    [Serializable]
    internal sealed class TypeProbe
    {
        public string type;
    }

    public static class HotDynamiteWireJson
    {
        public static bool TryGetMessageType(string json, out string type)
        {
            type = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                TypeProbe probe = JsonUtility.FromJson<TypeProbe>(json);
                if (probe == null || string.IsNullOrWhiteSpace(probe.type))
                {
                    return false;
                }

                type = probe.type;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ToJson<T>(T message)
        {
            return JsonUtility.ToJson(message);
        }
    }
}
