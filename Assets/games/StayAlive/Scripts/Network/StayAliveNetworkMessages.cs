using System;

namespace StayAlive.Networking
{
    public enum StayAliveLobbyPhase
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        JoiningRoom = 3,
        InRoomWaiting = 4,
        MatchPreparing = 5,
        MatchStarting = 6,
        InMatch = 7
    }

    public static class StayAliveRoomModes
    {
        public const string Random = "random";
        public const string Private = "private";
    }

    public static class StayAlivePayloadKinds
    {
        public const string Presence = "presence";
        public const string HostAnnounce = "host_announce";
        public const string RosterUpdate = "roster_update";
        public const string MatchPrepare = "match_prepare";
        public const string MatchStart = "match_start";
        public const string PlayerInput = "player_input";
        public const string StateSnapshot = "state_snapshot";
        public const string MatchEnd = "match_end";
    }

    [Serializable]
    public class StayAliveMessageTypeEnvelope
    {
        public string type;
    }

    [Serializable]
    public class StayAliveWelcomeMessage
    {
        public string type;
        public string userId;
    }

    [Serializable]
    public class StayAliveErrorMessage
    {
        public string type;
        public string message;
    }

    [Serializable]
    public class StayAliveJoinedMessage
    {
        public string type;
        public string roomId;
        public int chairId;
        public int capacity;
        public string[] chairs;
        public string hostUserId;
        public string state;
    }

    [Serializable]
    public class StayAliveLeftMessage
    {
        public string type;
    }

    [Serializable]
    public class StayAliveRoomMessage
    {
        public string type;
        public string from;
    }

    [Serializable]
    public class StayAliveRoomPayloadEnvelope
    {
        public string kind;
    }

    [Serializable]
    public class StayAlivePresencePayload
    {
        public string kind = StayAlivePayloadKinds.Presence;
        public int chairId;
        public int capacity;
        public string[] chairs;
    }

    [Serializable]
    public class StayAliveHostAnnouncementPayload
    {
        public string kind = StayAlivePayloadKinds.HostAnnounce;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public int hostChairId;
    }

    [Serializable]
    public class StayAliveRosterEntry
    {
        public string userId;
        public int chairId;
        public bool isBot;
        public bool isHost;
        public int appearanceIndex;
    }

    [Serializable]
    public class StayAliveRosterUpdatePayload
    {
        public string kind = StayAlivePayloadKinds.RosterUpdate;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public StayAliveRosterEntry[] roster;
    }

    [Serializable]
    public class StayAliveMatchPreparePayload
    {
        public string kind = StayAlivePayloadKinds.MatchPrepare;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public bool fillMissingSeatsWithBots;
        public int targetPlayerCount;
        public float countdownSeconds;
        public StayAliveRosterEntry[] roster;
    }

    [Serializable]
    public class StayAliveMatchStartPayload
    {
        public string kind = StayAlivePayloadKinds.MatchStart;
        public string roomId;
        public string hostUserId;
        public int simulationSeed;
        public float startAtLocalTime;
    }

    [Serializable]
    public class StayAlivePlayerInputPayload
    {
        public string kind = StayAlivePayloadKinds.PlayerInput;
        public string playerId;
        public int chairId;
        public int sequence;
        public float moveX;
        public float moveY;
    }

    [Serializable]
    public class StayAlivePlayerStateSnapshot
    {
        public int chairId;
        public float x;
        public float y;
        public float facingAngle;
        public float velocityX;
        public float velocityY;
        public bool isAlive;
        public bool isBot;
        public int appearanceIndex;
    }

    [Serializable]
    public class StayAliveStateSnapshotPayload
    {
        public string kind = StayAlivePayloadKinds.StateSnapshot;
        public string roomId;
        public string hostUserId;
        public int tick;
        public int phase;
        public float phaseTimeRemaining;
        public float matchTimeRemaining;
        public int completedSpinCount;
        public int[] symbolIndices;
        public int[] activeBombCellIndices;
        public StayAlivePlayerStateSnapshot[] players;
    }

    [Serializable]
    public class StayAliveMatchEndPayload
    {
        public string kind = StayAlivePayloadKinds.MatchEnd;
        public string roomId;
        public string hostUserId;
        public int winningChairId = -1;
    }

    [Serializable]
    public class StayAliveJoinRequest
    {
        public string type = "join";
        public int capacity;
        public string game_id;
        public string room_mode;
        public string room_id;
        public bool create_room;
        public string roomMode;
        public string roomId;
        public bool createRoom;
    }

    [Serializable]
    public class StayAliveLeaveRequest
    {
        public string type = "leave";
    }
}
