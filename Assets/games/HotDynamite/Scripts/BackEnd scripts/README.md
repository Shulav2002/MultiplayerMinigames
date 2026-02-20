# HotDynamite Backend (WebSocket)

This is a server-authoritative Node.js backend that matches your Unity message contracts:

- Client -> Server: `create_room`, `join_room`, `ready`, `start_match`, `input`, `leave_room`
- Server -> Client: `room_state`, `match_started`, `state_snapshot`, `match_ended`, `error`

## Run

1. Open terminal in this folder:
`BackEnd scripts`
2. Install deps:
`npm install`
3. Start server:
`npm start`

Default URL:
`ws://localhost:8080`

Set a custom port:
`PORT=9000 npm start`

## Notes

- Minimum participants are kept at `4` with bots.
- The game simulation is authoritative on the backend.
- Unity client should send only input and render snapshots.
