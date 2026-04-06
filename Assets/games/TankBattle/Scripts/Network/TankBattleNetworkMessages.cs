using System;

namespace TankBattle.Networking
{
    public enum TankBattleLobbyPhase
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

    public static class TankBattleRoomModes
    {
        public const string Random = "random";
        public const string Private = "private";
    }

    public static class TankBattlePayloadKinds
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
    public class TankBattleMessageTypeEnvelope
    {
        public string type;
    }

    [Serializable]
    public class TankBattleWelcomeMessage
    {
        public string type;
        public string userId;
    }

    [Serializable]
    public class TankBattleErrorMessage
    {
        public string type;
        public string message;
    }

    [Serializable]
    public class TankBattleJoinedMessage
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
    public class TankBattleLeftMessage
    {
        public string type;
    }

    [Serializable]
    public class TankBattleRoomMessage
    {
        public string type;
        public string from;
    }

    [Serializable]
    public class TankBattleRoomPayloadEnvelope
    {
        public string kind;
    }

    [Serializable]
    public class TankBattlePresencePayload
    {
        public string kind = TankBattlePayloadKinds.Presence;
        public int chairId;
        public int capacity;
        public string[] chairs;
    }

    [Serializable]
    public class TankBattleHostAnnouncementPayload
    {
        public string kind = TankBattlePayloadKinds.HostAnnounce;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public int hostChairId;
    }

    [Serializable]
    public class TankBattleRosterEntry
    {
        public string userId;
        public int chairId;
        public bool isBot;
        public bool isHost;
        public int appearanceIndex;
    }

    [Serializable]
    public class TankBattleRosterUpdatePayload
    {
        public string kind = TankBattlePayloadKinds.RosterUpdate;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public TankBattleRosterEntry[] roster;
    }

    [Serializable]
    public class TankBattleMatchPreparePayload
    {
        public string kind = TankBattlePayloadKinds.MatchPrepare;
        public string roomMode;
        public string roomId;
        public string hostUserId;
        public bool fillMissingSeatsWithBots;
        public int targetPlayerCount;
        public float countdownSeconds;
        public int simulationSeed;
        public TankBattleRosterEntry[] roster;
    }

    [Serializable]
    public class TankBattleMatchStartPayload
    {
        public string kind = TankBattlePayloadKinds.MatchStart;
        public string roomId;
        public string hostUserId;
        public int simulationSeed;
        public float startAtLocalTime;
    }

    [Serializable]
    public class TankBattlePlayerInputPayload
    {
        public string kind = TankBattlePayloadKinds.PlayerInput;
        public string playerId;
        public int chairId;
        public int sequence;
        public float moveX;
        public float moveY;
        public bool shootPressed;
    }

    [Serializable]
    public class TankBattlePlayerStateSnapshot
    {
        public int chairId;
        public string userId;
        public float x;
        public float y;
        public float facingAngle;
        public float velocityX;
        public float velocityY;
        public bool isAlive;
        public bool isBot;
        public int remainingShots;
    }

    [Serializable]
    public class TankBattleBulletStateSnapshot
    {
        public int bulletId;
        public int ownerChairId;
        public float x;
        public float y;
        public float velocityX;
        public float velocityY;
    }

    [Serializable]
    public class TankBattleUiStateSnapshot
    {
        public int player1Health;
        public int player2Health;
        public int player1Score;
        public int player2Score;
        public bool gameIsOver;
        public int winnerPlayerNumber;
    }

    [Serializable]
    public class TankBattleStateSnapshotPayload
    {
        public string kind = TankBattlePayloadKinds.StateSnapshot;
        public string roomId;
        public string hostUserId;
        public int tick;
        public bool isMatchStarting;
        public float matchStartDelayRemaining;
        public TankBattlePlayerStateSnapshot[] players;
        public TankBattleBulletStateSnapshot[] bullets;
        public TankBattleUiStateSnapshot ui;
    }

    [Serializable]
    public class TankBattleMatchEndPayload
    {
        public string kind = TankBattlePayloadKinds.MatchEnd;
        public string roomId;
        public string hostUserId;
        public int winningChairId = -1;
    }

    [Serializable]
    public class TankBattleJoinRequest
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
    public class TankBattleLeaveRequest
    {
        public string type = "leave";
    }
}
