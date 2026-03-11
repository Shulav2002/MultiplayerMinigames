using System;

namespace HotDynamite.Networking
{
    public enum HotDynamiteLobbyPhase
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

    public static class HotDynamiteRoomModes
    {
        public const string Random = "random";
        public const string Private = "private";
    }

    public static class HotDynamitePayloadKinds
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
    public class HotDynamiteMessageTypeEnvelope
    {
        public string type;
    }

    [Serializable]
    public class HotDynamiteWelcomeMessage
    {
        public string type;
        public string userId;
    }

    [Serializable]
    public class HotDynamiteErrorMessage
    {
        public string type;
        public string message;
    }

    [Serializable]
    public class HotDynamiteJoinedMessage
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
    public class HotDynamiteLeftMessage
    {
        public string type;
    }

    [Serializable]
    public class HotDynamiteRoomMessage
    {
        public string type;
        public string from;
    }

    [Serializable]
    public class HotDynamiteRoomPayloadEnvelope
    {
        public string kind;
    }

    [Serializable]
    public class HotDynamitePresencePayload
    {
        public string kind = HotDynamitePayloadKinds.Presence;
        public int chairId;
        public int capacity;
        public string[] chairs;
    }

    [Serializable]
    public class HotDynamiteHostAnnouncementPayload
    {
        public string kind = HotDynamitePayloadKinds.HostAnnounce;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public int hostChairId;
    }

    [Serializable]
    public class HotDynamiteRosterEntry
    {
        public string userId;
        public int chairId;
        public bool isBot;
        public bool isHost;
        public int appearanceIndex;
    }

    [Serializable]
    public class HotDynamiteRosterUpdatePayload
    {
        public string kind = HotDynamitePayloadKinds.RosterUpdate;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public HotDynamiteRosterEntry[] roster;
    }

    [Serializable]
    public class HotDynamiteMatchPreparePayload
    {
        public string kind = HotDynamitePayloadKinds.MatchPrepare;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public bool fillMissingSeatsWithBots;
        public int targetPlayerCount;
        public float countdownSeconds;
        public HotDynamiteRosterEntry[] roster;
    }

    [Serializable]
    public class HotDynamiteMatchStartPayload
    {
        public string kind = HotDynamitePayloadKinds.MatchStart;
        public string roomId;
        public string hostUserId;
        public int simulationSeed;
        public float startAtLocalTime;
    }

    [Serializable]
    public class HotDynamitePlayerInputPayload
    {
        public string kind = HotDynamitePayloadKinds.PlayerInput;
        public string playerId;
        public int chairId;
        public int sequence;
        public float moveX;
        public float moveY;
    }

    [Serializable]
    public class HotDynamitePlayerStateSnapshot
    {
        public int chairId;
        public float x;
        public float y;
        public float facingAngle;
        public float velocityX;
        public float velocityY;
        public bool isAlive;
        public bool hasBomb;
        public bool isBot;
        public int appearanceIndex;
    }

    [Serializable]
    public class HotDynamiteStateSnapshotPayload
    {
        public string kind = HotDynamitePayloadKinds.StateSnapshot;
        public string roomId;
        public string hostUserId;
        public int tick;
        public float bombTimeRemaining;
        public int bombHolderChairId = -1;
        public bool isMatchStarting;
        public float matchStartDelayRemaining;
        public HotDynamitePlayerStateSnapshot[] players;
    }

    [Serializable]
    public class HotDynamiteMatchEndPayload
    {
        public string kind = HotDynamitePayloadKinds.MatchEnd;
        public string roomId;
        public string hostUserId;
        public int winningChairId = -1;
    }

    [Serializable]
    public class HotDynamiteJoinRequest
    {
        public string type = "join";
        public int capacity;
        public string game_id;
    }

    [Serializable]
    public class HotDynamiteLeaveRequest
    {
        public string type = "leave";
    }
}
