# MetaFour - Multiplayer Connect Four for the Meta 2

[TLDR](#TLDR)

MetaFour is the game of Connect Four for the Meta 2, and allows two players to play a game of the classic Connect-Four board game across a local network. It was made to familiarize myself with the platform (and holographic AR in general), and as a test of how multiplayer games could be done in AR.

![Screenshot](https://media.githubusercontent.com/media/macklinb/MetaFour/master/screenshot.png)

## Multiplayer AR and shared coordinate space
One big challenge that we face with multiplayer collaborative AR is that we need some way for content to exist in a fixed position in the real world for both players. When each players starts, their world origin is the headset position, therefore what one player considers to be the origin would be completely offset - as well as misaligned rotationally - to what the other player sees. At the moment, this could theoretically be done in a number of ways:
- Using a point of reference from the real world, such as a marker, or a manually-placed point. In this way the virtual world origin position is different for both players, and all gameplay has to be relative to each players own reference point.
- Manually setting the origin so that the center point and coordinate spaces are roughly the same for both players.
- Using outside-in trackers to get a shared coordinate space (e.g. SteamVR)
- Amalgamated point cloud / SLAM data from both headsets to form a map of the shared space.

Right now, the easiest (but probably least accurate) solution without using external hardware is by manually setting this point. With this comes two more options regarding how the content is kept in sync:

### Zeroed/centered board and offset player
After setting the point, the origin position and rotation is set to zero, and we then rotate/move the player in the inverse direction:

```csharp
// in ConnectFourBoardSetup.FinalizeSetup
// origin and rotation refer to the manually-placed board position/rotation in world space.

// Offset the camera position so that the board appears in the same position as before
cameraRig.position -= origin;

// Rotate the camera position around the inverse rotation of the board
cameraRig.position = Quaternion.Inverse(rotation) * cameraRig.position;

// Subtract the rotation of the board from the rotation of the camera, so the board appears in the same rotation as before.
cameraRig.rotation = Quaternion.Inverse(rotation) * cameraRig.rotation;

// Board position is zeroed after this...
```
    
This makes it appear that the point/board is in the same position, but in fact the player is now offset from the center position. If the position and rotation is set correctly, this will make it so that both players share roughly the same coordinate space. Using this method, no positional translation is necessary, so each player doesn't need to know about the other players offset in order to translate networked positions - other than a basic inversion along the world X/Z axis:

```csharp
// In ConnectFourBoard.Update
// otherBall is a local representation of the other players ball. It is rendered
// otherBallActual is the synced representation of the other players ball. It is not rendered

otherBall.transform.position =
new Vector3(otherBallActual.transform.position.x * -1.0f,
            otherBallActual.transform.position.y * 1.0f, 
            otherBallActual.transform.position.z * -1.0f);
```

### Offset board and centered player
This is used for Meta SDK v2.7.0 and earlier, as it doesn't allow the player to be moved from the origin without [breaking hand tracking](https://community.metavision.com/t/grab-interaction-does-not-work-if-metacamerarig-is-not-at-root). After setting the board position, it is kept as-is. Using this method, game logic and positioning has to take into account that the networked position of the ball will now be relative to the board offset (for each player), and not in a shared world space. In this case, we have to send the other player the positional/rotational offset of our board from zero, so that they can properly translate our networked ball position into one that is relative to their board, and vice versa.

```csharp
// in ConnectFourBoard.Update

// otherBoard is the Transform representation of the other players board. It is created in ConnectFourBoard.OnBoardOffsetMessage from the coordinates that they send when they complete the board setup
// board is the same, but for our board
// otherBall is a local representation of the other players ball. It is rendered
// otherBallActual is the synced representation of the other players ball. It is not rendered

// Convert the worlds-space position of the networked ball to one that is local to the other players board 
Vector3 pos = otherBoard.InverseTransformPoint(otherBallActual.transform.position);

// Invert the local position using the inversion factor (Vector3.Scale is effectively (Vector3)left *= (Vector3)right
pos = Vector3.Scale(pos, inversionFactor);

// Convert that local position to a world position using our board origin 
otherBall.transform.position = board.TransformPoint(pos);
```

Originally MetaFour used an offset player and centered board - for simplicity. However, due to the MetaHands issue I had to change over to using an offset board and centered player, switchable via the `META_CENTERED_PLAYER` define symbol.

In the future, I would like to see Meta include some sort of consideration for shared coordinates spaces, or at least their own take on ways to accomplish this - as it is an important part of AR as a whole. Ideally, the process would need to be seamless, involving as little setup as possible.

## Board setup phase

MetaFour implements a sort of mutually-agreed board placement, for when we want two players sharing the same board in real life. This involves a pre-game setup phase where both players size and center their virtual playfield in roughly the same spot in the real world. Because of this, the play space of each player is completely independent. Setting up the board can be done in one of three ways:
- Pressing **D** on the keyboard will place the board in a fixed position.
- Manually, by dragging around the playfield in headset (using hand gestures) or on the PC using the arrow keys. While using the arrow keys, holding **Shift** will translate the board along the X/Z axis, holding **Alt** will rotate the board and translate on the Y axis, holding **Control** will change the size of the board.
- Using the mesh reconstruction feature to determine the center of the board, by scanning a surface that it should sit on (although rotation cannot be inferred accurately this way, we point the playfield at the player - usually -Z). In this case, it follows the default mesh reconstruction shortcuts. **Alt + I** begins reconstruction, **Alt + S** ends it. Pressing **F4** after completing a reconstruction will reset it and allow you to redo the process. When you're done, pressing the **capture button** on the right side of the headset will then center the board.

Once you're satified with the position of the board, press **Enter** to finish the setup phase. After this is done, the board offset and board size is sent to the other player. The offset is only used when we have an offset board and centered player - and is mainly used when translating the networked position of the other players ball as they are moving it around. The board size, although originally meant to change the physical size of the board, is only used to position the ball spawner. The first time that the board size is set by either player, the other player will be locked to that size.

## Controls

During gameplay, you can pick up and place the ball in a desired column using either your hands, or with the keyboard (**Arrow** keys (**←**, **→**) to move, and **Space** to place). If you're using MetaHands, I've found that tracking works best with no sleeves or rolled-up sleeves, and that the open/closed detection is more reliable by using a grip rather than a "pinching" motion.

Press **P** at any time during gameplay to toggle the AI / computer player for your moves. This will restart the current game. The AI is basic and easily beatable.

Press **Backtick (`)** to show/hide the console.

**Note:** Ensure that the main game window, and not the webcam view, is focused when pressing keys, so that it recieves input

## Networking

This is the first project I've done which uses uNET, beyond sending simple messages. As it is a two-player, turn based game - it doesn't require any complex synchronization of GameObjects in the scene, but instead relies on basic messaging to-and-from the host via `NetworkMessage`'s, and `NetworkBehaviour`'s Command and ClientRpc calls. While I'm not doing anything special here, the way I've implemented it is modified slightly to allow adding multiple handlers per MsgType, (something that I think [should be supported](https://feedback.unity3d.com/suggestions/ability-to-add-more-than-one-handler-per-msgtype)).

The server stores and manages the active board state and actual game logic, it is responsible for checking for winning conditions after each turn, as well as messaging clients for important game state changes (e.g. a players turn, win, lose, etc). Server code is mainly spread across `NetworkManager` and `ConnectFourPlayer`.

The client is responsible for inputting moves, as well as displaying a visual representation of the board. Each client has a player object, the ball, a NetworkTransform synced object that both players see. Client gameplay logic and messaging is handled by `ConnectFourPlayer`, while board visuals are handled by `ConnectFourBoard`.

Because MetaFour was originally made to be run across two separate machines, it only supports one local player per instance. So while you are able to play a single player game, the bot player will need to be run in another instance of the game. See the config file for more information about running a single player instance.

### Config file

Game options can be configured by using either the config.ini file, placed in the root of a build, or with command-line arguments. Command line arguments take precedence over anything in the config file, meaning if any arguments are present - the entirity of the config file will be ignored. If no config file, and no command line arguments are present, settings will be set to their default values.

Do not wrap keys or values in quotation marks. Do not leave a space either side of the equals sign.

```ini
; config.ini

; Determines what network configuration to run in. Defaults to host.
; Valid values are "client", "host" (standalone "server" has not been tested, and should not be used).
config=host

; Specifies the IP address of the server to connect to. This can only be used on a client and will have no effect if present on the server.
; This will be set to the loopback address (127.0.0.1) if mode is singleplayer.
; Comment out if using broadcast/network discovery
;address=192.168.1.2

; Specifies either the port to be connected to (if client), or the port to host the game on (if host/server).
; Defaults to 11474 if the key or value is left empty, or is "default"
port=default

; Which port to broadcast on (if server) and listen to (if client) for auto-connection. Will be ignored if an address is specified. If this key is not provided, we will not broadcast or listen at all. If an address is not specified, and this key is not present, we enable network discovery on the default port. Defaults to 11475 if the value is empty or "default".
broadcast_port=default

; Specifies the type of game/number of players. Defaults to "multiplayer"
; Valid values are "singleplayer" and "multiplayer". If running in single player mode, another instance of the game will be run as a child process, using the built-in arguments "batchmode" and "nographics". The instance will be connected to the loopback address and the AI will be enabled from start. The process will be closed automatically when the main instance is closed. The headless instance's network config will be set to the opposite of the main instance. This is the same as running two multiplayer instances on the same machine, and enabling AI on one of them.
mode=multiplayer

; Specifies whether to enable meta-related GameObjects or not. Set this to false to not try to connect to a Meta 2 device. If true, meta objects are only enabled if the device is connected.
meta_enabled=false
```

If the client cannot connect to the host (and vice versa), ensure that the Windows Firewall is disabled - or at least exclusions are added on both machines. In some domain environments, network discovery will not work at all, so use a direct connection in these cases.

### Command line arguments

The same arguments as the config file are available. Parameters/keys have to be prefixed with '-', '--' or '/', omitted values will default depending on the parameter.

- Direct connect (as client only)

    `MetaFour.exe --config client --address 192.168.1.2 --port 1234`

- Direct host (as server/host)

    `MetaFour.exe --config host --port 4444`

- Listen for servers (as client only), listen port `broadcast_port` is optional (will default if omitted). In this case, since a server connection port is not provided, we will use the default port

    `MetaFour.exe --config client --broadcast_port 5555`

- Broadcast to potential clients (as server/host), broadcast port is optional (will default if omitted)

    `MetaFour.exe --config host --broadcast_port`

### Defaults
```
config = host
port = 11474
broadcast_port = 11475
mode = multiplayer
meta_enabled = true
```

## Network initialization

Network initialization and discovery sequence

Network settings are provided by a NetworkSettingsProvider, which determines if the program is run as a server, host or client, ports, addresses, etc. Depending on the NetworkSettings:

- Server:
    1. Start listening on the `port` provided for direct connections.
    2. Register handlers for server.
    3. If `broadcast_port` is set, setup the NetworkDiscovery component and start broadcasting.

- Client:
    1. Register handers for client.
    2. Direct connect to server if IP address `address` is provided, otherwise setup the NetworkDiscovery components and start listening for a server.

- Host:
    1. Start as Server, connecting as local player.

- Client connects. 

Player number assignment
- [Server] `NetworkManager.OnServerConnect` called on server, in response to a new client connecting.
- [Server] Connection given player a unique playerId, `ConnectFourMsgType.AssignPlayerNumber` is sent to the client that connected.
- [Client] `OnClientPlayerNumberAssigned` called on client, NetworkManager.PlayerID is set to the byte value that was passed to it.
- [Server] Check to see if the number of connected clients is now MAX_CONNECTIONS, and if so, stop broadcasting (if we were) and stop listening for connections

In case that a client has already set up their environment:
- [Server] `ConnectFourBoardSetup.OnServerConnect` called on server.
- [Server] If either client has already completed a setup (`hasFixedBoardSize` is true), send a message of type `ConnectFourMsgType.BoardSize` to all clients - containing the board size that was set.
- [Client] `ConnectFourBoardSetup.OnBoardSizeMessage` called on client. This changes the behaviour of the tool to only allow position/rotation changes, setting the width and height offsets automatically.

Environment setup + readying up
- [Client] `ConnectFourBoardSetup.OnClientConnect` called on client, in response to it connecting to a server.
- [Client] Player allowed time to set up environment, we show the setup UI.
- [Client] If the client completes their setup, or has already completed their setup (which can happen if this connect is a re-connect, occuring after the setup has already completed):
    - Send a message ot type `ConnectFourMsgType.BoardSize`, containing our board size. This is only sent if the board size wasn't fixed (obtained from the server already).
    - Send a message of type `ConnectFourMsgType.BoardOffset` containing our board offset.
    - Call `ClientScene.Ready`.
    - Enable board visuals with `ConnectFourBoard.Instance.ShowBoard()`.
- [Server] `ConnectFourBoardSetup.OnServerBoardSizeMessage` called on the server, setting the server-side fixedBoardSize variables. Forwards the message to all clients.
    - [Client] `ConnectFourBoardSetup.OnClientBoardSizeMessage` called, forcing our board size to be fixed.
    - [Client] `ConnectFourBoard.OnClientBoardSizeMessage` called, we use the size to adjust the position of the spawners, as well as the collider of the play surface.
- [Server] `ConnectFourBoardSetup.OnServerBoardOffsetMessage` called on server. Saves the players offset, forwarding the message to all clients.
    - [Client] `ConnectFourBoard.OnClientBoardOffsetMessage` called on client. This is used both to position our board if the offset was ours (although it is already done in `ConnectFourBoardSetup`), and to transform networked player positions if the offset was of the other player's board.

Readying/Adding player
- [Server] `NetworkManager.OnServerReadyMessage` called on server in response to `ClientScene.Ready` being called on the client. Here we mark the client as ready with `NetworkServer.SetClientReady`, as well as send a `MsgType.Ready` ByteMessage to all clients, containing the playerId of the client that was readied. This is done manually, since uNET for some reason doesn't do this - and I can't find another way of telling the client that they are now marked as ready.
    - [Client] `NetworkManager.OnClientReady` is called on client. If the client that was readied is us, we call `ClientScene.AddPlayer(0)`
- [Server] `NetworkManager.OnServerAddPlayerMessage` is called on the server in response to `ClientScene.AddPlayer`.  
Here, the server instantiates a new player object, including a ConnectFourPlayer component, setting `ConnectFourPlayer.ownerPlayerId` to that of the client that called AddPlayer. `NetworkServer.AddPlayerForConnection` is called, which in turn spawns the player object on all connected clients (clients that are added at a later time will have existing player objects automatically spawn).
    - [Client] `ConnectFourPlayer.OnStartClient` (a NetworkBehaviour method override) is called on both clients. This adds references to the local version of the player object that was spawned to `ConnectFourBoard`, for use during gameplay.
    - [Client] `ConnectFourPlayer.OnStartLocalPlayer` (a NetworkBehaviour method override) is called on only the client who owns the player object. Here we send a `ConnectFourMsgType.PlayerReady` message to the server, telling it that we are ready to start the game.

Game start
- [Server] `NetworkManager.OnServerPlayerReady` is called in response to the `ConnectFourMsgType.PlayerReady` message. This increments the `playersReady` value. If all players are ready, `NetworkManager.StartGame` is called. This does the following:
    - Clears the board logic in `ConnectFour` via `ConnectFour.WipeBoard`.
    - `ConnectFourMsgType.StartGame` message is sent to all clients.
    - One second later, a `ConnectFourMsgType.PlayerStartTurn` ByteMessage is sent to all clients, containing the playerId of the player whose turn it is.
- [Client] `ConnectFourBoard.OnStartGame` is called on all clients. Here we clear and reset the visual state of the board for the client.
- [Client] `ConnectFourBoard.OnPlayerStartTurn` is called on all clients, where we show the ball for the player whose turn it is.
- [Client] `ConnectFourPlayer.OnPlayerStartTurn` is called on all clients. We enable control of the player if it is our turn.

## TLDR
This is Connect Four for the Meta 2. You can play it in either singleplayer or multiplayer mode.
Edit config.ini to set some options before playing:
- For singleplayer set `mode` to `singleplayer`, for multiplayer set `mode` to `multiplayer`.
- To disable meta: Set `meta_enabled` to `false`.
- Run the executable, and [set up the board](##Board-setup-phase).
- Play using hand gestures, or with the arrow keys.

## Third party software/libraries used

**Meta SDK**  
Copyright (C) 2018, Meta Company  
Link: https://devcenter.metavision.com/home  
License: [EULA](https://docs.metavision.com/external/doc/latest/sdk_meta_eula.html) / [Third-Party](https://docs.metavision.com/external/doc/latest/sdk_meta_tpln.html)

**DOTween**  
Copyright (C) 2014, Daniele Giardini - Demigiant  
Link: http://dotween.demigiant.com  
License: http://dotween.demigiant.com/license.php

**TextMesh Pro**  
Copyright (C) 2016-18, Stephan Bouchard / Unity Technologies ApS   
Link: [Asset Store](https://assetstore.unity.com/packages/essentials/beta-projects/textmesh-pro-84126) / [Homepage](http://digitalnativestudios.com/)  
License: https://unity3d.com/legal

**HologramShader**  
Copyright (C) 2018, Andy Duboc
Link: https://github.com/andydbc/HologramShader  
License: https://github.com/andydbc/HologramShader/blob/master/LICENSE

**UnityMainThreadDispatcher**  
Copyright (C) 2017, Pim de Witte  
Link: https://github.com/PimDeWitte/UnityMainThreadDispatcher  
License: https://github.com/PimDeWitte/UnityMainThreadDispatcher/blob/master/LICENSE

**Unity Post-Processing Stack**  
Copyright (C) 2018, Unity Technologies ApS  
Link: https://github.com/Unity-Technologies/PostProcessing  
License: https://github.com/Unity-Technologies/PostProcessing/blob/v2/LICENSE.md
