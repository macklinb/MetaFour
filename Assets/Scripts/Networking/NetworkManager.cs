using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

// Note that NetworkServer.RegisterHandler and NetworkClient.RegisterHandler only support one handler per message! To circumvent this restriction, NetworkManager implements a forwarding mechanism which will forward the deserialized MessageBase to delegates of a specific MsgType. You MUST subscribe to messages via NetworkManager.RegisterClientHandler or RegisterServerHandler, otherwise previous handlers will not be called.

public class NetworkManager : MonoBehaviour
{
    // --- PUBLIC VARIABLES ---

    // Prefab that will be spawned as the "Player" GameObject (using NetworkServer.AddPlayerForConnection) when a client connects on OnServerConnect
    public GameObject playerPrefab;

    public GameObject[] prefabs;

    public NetworkStatusHud statusHud;

    // --- PRIVATE VARIABLES ---
    
    NetworkClient client;
    NetworkDiscoverySimple discovery;

    byte clientPlayerId;
    byte serverAssignedPlayerId = 1; // <- Incremented to give the client a unique playerId

    // Number of players who are ready to start their turn. This is not in response to MsgType.Ready, but to ConnectFourMessageType.PlayerReady, valid only on server
    int playersReady = 0;

    // ConnectFourPlayer object by playerId lookup, valid only on server
    Dictionary<byte, ConnectFourPlayer> players;

    // NetworkConnection by playerId lookup, valid only on server
    Dictionary<byte, NetworkConnection> playerIdToNetConn;

    // Prefab GameObject by prefab name lookup, valid only on server
    Dictionary<string, GameObject> stringToPrefab;

    // Action by MsgType lookup for clients/servers respectively
    Dictionary<short, Action<NetworkMessage>> clientHandlers;
    Dictionary<short, Action<NetworkMessage>> serverHandlers;
   
    // --- STATIC VARIABLES ---

    static NetworkManager instance;

    // Player ID of this client, only valid on clients (returns 0 on server)
    public static byte PlayerID
    {
        get
        {
            if (!IsClient) return 0;
            return instance.clientPlayerId;
        }
    }

    // Player ID of other player, only valid on clients (returns 0 on server)
    public static byte OtherPlayerID
    {
        get
        {
            if (!IsClient) return 0;
            return GetOtherPlayer(instance.clientPlayerId);
        }
    }

    public static NetworkClient Client { get { return instance.client; } }

    public static bool IsClient { get { return instance.client != null; } }
    public static bool IsServer { get { return NetworkServer.active; } }
    public static bool IsHost { get { return IsClient && IsServer; } }

    // Player whose turn it currently is, valid only on server.
    public static byte CurrentTurnPlayer = 0;

    // --- CONSTANTS ---

    // NetworkDiscovery defaults
    const int NET_DISCOVERY_BROADCAST_KEY = 11476;
    const int NET_DISCOVERY_MAX_INIT_RETRIES = 10;
    const string NET_DISCOVERY_BROADCAST_DATA = "MetaFour";
    const int NET_DISCOVERY_BROADCAST_INTERVAL = 1000;

    const int MAX_CONNECTIONS = 2;

    // --- INTERNAL TYPES ---

    void Start()
    {
        instance = this;

        // Generate string-to-prefab lookup table for GetPrefabWithName
        stringToPrefab = new Dictionary<string, GameObject>();
        stringToPrefab = prefabs.ToDictionary(p => p.name, p => p);

        players = new Dictionary<byte, ConnectFourPlayer>();
        playerIdToNetConn = new Dictionary<byte, NetworkConnection>();

        clientHandlers = new Dictionary<short, Action<NetworkMessage>>();
        serverHandlers = new Dictionary<short, Action<NetworkMessage>>();

        Open();
    }

    void Open()
    {
        if (Settings.Current.networkConfig == Settings.NETWORK_CONFIG_SERVER)
        {
            Debug.LogError("NetworkManager : Open - Server-only instance not supported at the moment!");
            SetupServer();
        }
        else if (Settings.Current.networkConfig == Settings.NETWORK_CONFIG_HOST)
        {
            SetupServer();
            SetupLocalClient();
        }
        else if (Settings.Current.networkConfig == Settings.NETWORK_CONFIG_CLIENT)
        {
            SetupClient();
        }
        else
        {
            Debug.LogError("NetworkManager : Open - Settings.networkConfig not provided!");
        }
    }

    // Sets up the network with this machine as a server, starting listen for clients.
    void SetupServer()
    {
        if (NetworkServer.Listen(Settings.Current.serverPort))
        {
            // ! This might have to be done before Listen
            RegisterServerHandlers();

            string debugStr = "Listening on port " + Settings.Current.serverPort;
            Debug.Log("[SERVER] NetworkManager : SetupServer - " + debugStr);
            statusHud.SetTextPersist(debugStr);

            // Set up network discovery for server, if it is enabled
            if (Settings.Current.UsesNetworkDiscovery)
                SetupNetworkDiscovery();
        }
        else
        {
            string debugStr = "Listen on port " + Settings.Current.serverPort + " failed!";
            Debug.LogError("[SERVER] NetworkManager : SetupServer - " + debugStr);
            statusHud.SetText(debugStr);
        }
        
    }

    // Sets up the network with this player as a client, connecting to the server. 
    void SetupClient()
    {
        if (client == null)
            client = new NetworkClient();
        
        RegisterClientHandlers();
        RegisterClientPrefabs();

        // Directly connect if we have a server IP address
        if (Settings.Current.UsesDirectConnect)
        {
            string debugStr = "Connecting to " + Settings.Current.serverAddress + ":" + Settings.Current.serverPort;
            Debug.Log("[CLIENT] NetworkManager : SetupClient - " + debugStr);
            statusHud.SetTextPersist(debugStr);

            client.Connect(Settings.Current.serverAddress, Settings.Current.serverPort);
        }

        // Always listen for broadcasting servers if we haven't provided a direct address
        else
        {
            SetupNetworkDiscovery();
        }
    }

    // A local client is one that will connect to a sever hosted on this machine, rather than one across a network
    void SetupLocalClient()
    {
        client = ClientScene.ConnectLocalServer();
        RegisterClientHandlers();
        RegisterClientPrefabs();
    }

    void SetupNetworkDiscovery()
    {
        if (discovery == null)
        {
            var obj = new GameObject("NetworkDiscovery");
            discovery = obj.AddComponent<NetworkDiscoverySimple>();
        }

        // Setup NetworkDiscovery component
        discovery.showGUI = false;
        discovery.useNetworkManager = false;

        discovery.broadcastKey = NET_DISCOVERY_BROADCAST_KEY;
        discovery.broadcastPort = Settings.Current.broadcastPort;

        // Clear event handler
        discovery.OnReceivedBroadcastEvent = null;

        int initRetries = 0;

        // Initialize NetworkDiscovery. The component returns false if the port is not availiable
        while (!discovery.Initialize())
        {
            if (initRetries < NET_DISCOVERY_MAX_INIT_RETRIES)
            {
                discovery.broadcastPort = ++discovery.broadcastPort;
                initRetries++;

                Debug.LogError("NetworkManager : SetupNetworkDiscovery - Port " + (discovery.broadcastPort - 1) + " was not available! Trying on port " + discovery.broadcastPort + " (retry #" + initRetries + ")");
            }
            else
            {
                Debug.Log("NetworkManager : SetupNetworkDiscovery - Exhausted retries!");
                break;
            }
        }

        // Start listening for broadcasting servers, in client config
        if (Settings.Current.networkConfig == Settings.NETWORK_CONFIG_CLIENT)
        {
            string debugStr = "Listening for server on port " + discovery.broadcastPort;
            Debug.Log("[CLIENT] NetworkManager : SetupNetworkDiscovery - " + debugStr);
            statusHud.SetTextPersist(debugStr);

            discovery.broadcastData = string.Empty;
            discovery.OnReceivedBroadcastEvent += OnClientReceivedBroadcast;

            if (discovery.StartAsClient() == false)
            {
                Debug.LogError("[CLIENT] NetworkManager : SetupNetworkDiscovery - Unable to start as client!");
                return;
            }
        }

        // Start broadcasting, in host and server config
        else
        {
            Debug.Log("[SERVER] NetworkManager : SetupNetworkDiscovery - Broadcasting for players on port " + discovery.broadcastPort);

            discovery.broadcastData = NET_DISCOVERY_BROADCAST_DATA;
            discovery.broadcastInterval = NET_DISCOVERY_BROADCAST_INTERVAL;

            if (discovery.StartAsServer() == false)
            {
                Debug.LogError("[SERVER] NetworkManager : SetupNetworkDiscovery - Unable to StartAsServer!");
                return;
            }
        }
    }

    void StopServer()
    {
        if (IsServer)
        {
            NetworkServer.Shutdown();
        }
    }

    void StopHost()
    {
        StopServer();
        StopClient();
    }

    // Called by OnClientDisconnect, can also be called manually
    void StopClient()
    {
        if (IsClient)
        {
            ClientScene.DestroyAllClientObjects();

            client.Disconnect();
            client.Shutdown();
            //client = null;
        }
    }

    void RegisterClientPrefabs()
    {
        if (playerPrefab != null)
            ClientScene.RegisterPrefab(playerPrefab);

        foreach (var p in prefabs)
            ClientScene.RegisterPrefab(p);
    }

    // Register handlers for messages send from the clients, to the server via NetworkClient.Send
    void RegisterServerHandlers()
    {
        CleanupServerHandlers();

        // Built-in uNET handlers
        NetworkManager.RegisterServerHandler(MsgType.Connect, OnServerConnect);
        NetworkManager.RegisterServerHandler(MsgType.Disconnect, OnServerDisconnect);
        NetworkManager.RegisterServerHandler(MsgType.AddPlayer, OnServerAddPlayerMessage);
        NetworkManager.RegisterServerHandler(MsgType.Ready, OnServerReadyMessage);
        NetworkManager.RegisterServerHandler(MsgType.Error, OnServerError);

        // Custom client-to-server handlers
        NetworkManager.RegisterServerHandler(ConnectFourMsgType.RestartGame, OnServerRestartGame);
        NetworkManager.RegisterServerHandler(ConnectFourMsgType.PrefabSpawnRequest, OnServerPrefabSpawnRequestMessage);
        NetworkManager.RegisterServerHandler(ConnectFourMsgType.PlayerReady, OnServerPlayerReady);
    }

    // Register handlers for messages to be sent from the server, to clients via NetworkServer.Send
    void RegisterClientHandlers()
    {
        CleanupClientHandlers();

        // Built in uNET handlers
        NetworkManager.RegisterClientHandler(MsgType.Connect, OnClientConnect);
        NetworkManager.RegisterClientHandler(MsgType.Disconnect, OnClientDisconnect);
        NetworkManager.RegisterClientHandler(MsgType.Error, OnClientError);
        NetworkManager.RegisterClientHandler(MsgType.AddPlayer, OnClientPlayerAdded);
        NetworkManager.RegisterClientHandler(MsgType.Ready, OnClientReady);

        // Custom handlers
        NetworkManager.RegisterClientHandler(ConnectFourMsgType.AssignPlayerNumber, OnClientPlayerNumberAssigned);
    }

    void CleanupServerHandlers()
    {
        CleanupHandlers(true);
    }

    void CleanupClientHandlers()
    {
        CleanupHandlers(false);
    }

    // Clean up server/client handlers, removing any handlers that contain a null target
    void CleanupHandlers(bool isServer)
    {
        var handlers = isServer ? serverHandlers : clientHandlers;

        if (handlers == null)
            return;

        foreach (var entry in handlers)
        {
            var action = entry.Value;

            foreach (var handler in entry.Value.GetInvocationList())
            {
                string targetAsString = handler.Target as string;

                // For some reason the handler target gets set to "null" literal string, when Unity nullifies it
                if (handler.Target == null || targetAsString == "null")
                    action -= (Action<NetworkMessage>)handler;
            }
        }
    }

    void RegisterServerHandlerInternal(short msgType, Action<NetworkMessage> handler)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[CLIENT] NetworkManager : RegisterServerHandler - Tried to register server handler on a non-server instance!");
            return;
        }
        
        // Add a new serverHandler value and redirection handler for this message type if one doesn't already exist
        //if (!serverHandlers.ContainsKey(msgType))
        if (!NetworkServer.handlers.ContainsKey(msgType) ||
            !NetworkServer.handlers[msgType].GetInvocationList().Contains((NetworkMessageDelegate)InvokeServerCallbacksForMessage))
        {
            //Debug.Log("[SERVER] NetworkManager : RegisterServerHandler - Registering redirect handler for " + MsgTypeToString(msgType));

            // Register the redirection handler with the BUILT IN messaging system
            NetworkServer.RegisterHandler(msgType, InvokeServerCallbacksForMessage);

            // Add a null handler
            if (!serverHandlers.ContainsKey(msgType))
                serverHandlers.Add(msgType, null);
        }

        // Add the callback, only if it doesn't already exist.
        if (serverHandlers[msgType] == null ||
            serverHandlers[msgType].GetInvocationList().Contains(handler) == false)
        {
            serverHandlers[msgType] += handler;
        }
        else
        {
            //Debug.LogWarning("[SERVER] NetworkManager : RegisterServerHandler - Tried to add duplicate delegate!");
        }
    }

    void RegisterClientHandlerInternal(short msgType, Action<NetworkMessage> handler)
    {
        if (!IsClient)
        {
            Debug.LogWarning("[SERVER] NetworkManager : RegisterClientHandler - Tried to register client handler on a non-client instance!");
            return;
        }
        
        // Add a new clientHandler value and redirection handler for this message type if one doesn't already exist
        //if (!clientHandlers.ContainsKey(msgType))
        if (!client.handlers.ContainsKey(msgType) ||
            !client.handlers[msgType].GetInvocationList().Contains((NetworkMessageDelegate)InvokeClientCallbacksForMessage))
        {
            //Debug.Log("[CLIENT] NetworkManager : RegisterClientHandler - Registering redirect handler for " + MsgTypeToString(msgType));

            // Register the redirection handler, with body
            client.RegisterHandler(msgType, InvokeClientCallbacksForMessage);

            // Add a null handler
            if (!clientHandlers.ContainsKey(msgType))
                clientHandlers.Add(msgType, null);
        }

        // Add the callback, only if it doesn't already exist/
        // Duplicate callbacks shouldn't exist unless an object sends RegisterClientHandler with the same delegate, multiple times. This occurs on purpose when restarting the client, as we don't clear existing clientHandler's when we stop the client, to prevent other scripts losing their callbacks.
        if (clientHandlers[msgType] == null ||
            clientHandlers[msgType].GetInvocationList().Contains(handler) == false)
        {
            clientHandlers[msgType] += handler;
        }
        else
        {
            //Debug.LogWarning("[CLIENT] NetworkManager : RegisterClientHandlerInternal - Tried to add duplicate delegate!");
        }
    }

    void UnregisterServerHandlerInternal(short msgType, Action<NetworkMessage> handler)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[CLIENT] NetworkManager : UnregisterServerHandler - Tried to unregister server handler on a non-server instance!");
            return;
        }

        // Check that the serverHandler's actually contains the handler
        if (serverHandlers[msgType] != null && serverHandlers[msgType].GetInvocationList().Contains(handler))
            serverHandlers[msgType] -= handler;
        else
            Debug.LogWarning("[SERVER] NetworkManager - UnregisterServerHandler - Handler not found in invocation list");
    }

    void UnregisterClientHandlerInternal(short msgType, Action<NetworkMessage> handler)
    {
        if (!IsClient)
        {
            Debug.LogWarning("[SERVER] NetworkManager : UnregisterClientHandler - Tried to unregister client handler on a non-client instance!");
            return;
        }

        // Check that the serverHandler's actually contains the handler
        if (clientHandlers[msgType] != null && clientHandlers[msgType].GetInvocationList().Contains(handler))
            clientHandlers[msgType] -= handler;
        else
            Debug.LogWarning("[CLIENT] NetworkManager - UnregisterClientHandler - Handler not found in invocation list");
    }

    void InvokeServerCallbacksForMessage(NetworkMessage msg)
    {
        InvokeCallbacksForMessage(msg, true);
    }

    void InvokeClientCallbacksForMessage(NetworkMessage msg)
    {
        InvokeCallbacksForMessage(msg, false);
    }

    void InvokeCallbacksForMessage(NetworkMessage msg, bool isServer)
    {
        var handlers = isServer ? serverHandlers : clientHandlers;
        string debugPrefix = isServer ? "[SERVER]" : "[CLIENT]";

        // If there is no value with that key in the dictionary, it means that the built-in handler (NetworkMessageHandlers from NetworkServer.handlers[msg.msgType]/client.handlers[msg.msgType]) for that MsgType hasn't been registered to fire this "InvokeCallbacksForMessage" function. This should only really happen if you call this function handlers without using NetworkManager.RegisterHandler first.
        if (!handlers.ContainsKey(msg.msgType))
        {
            Debug.LogWarning(debugPrefix + " NetworkManager : InvokeCallbacksForMessage - Not currently intercepting messages for " + MsgTypeToString(msg.msgType));
        }

        // This means that we have registered a built-in message, but there aren't any callbacks to fire. This is NOT an error
        else if (handlers[msg.msgType] == null)
        {
            Debug.LogWarning(debugPrefix + " NetworkManager : InvokeCallbacksForMessage - Delegate for " + MsgTypeToString(msg.msgType) + " is null");
        }
        else
        {
            // If the message doesn't have a NetworkReader, invoke all methods since we don't have to worry about resetting the reader's position
            if (msg.reader == null)
                handlers[msg.msgType].Invoke(msg);
            else
            {
                var delegates = handlers[msg.msgType].GetInvocationList();

                if (delegates.Length == 1)
                    handlers[msg.msgType].Invoke(msg);
                else
                {
                    uint offset = msg.reader.Position;

                    foreach (Delegate d in delegates)
                    {
                        // Invoke the delegate with the message
                        d.DynamicInvoke(msg);

                        // Reset the reader position if the delegate read data, regardless of if we have any more delegates to send the message to
                        if (msg.reader.Position > offset)
                        {
                            msg.reader.SeekZero();
                            msg.reader.ReadBytes((int)offset);
                        }
                    }
                }
            }
        }
    }

    // --- PUBLIC FUNCTIONS ---

    public static bool IsMe(byte playerId)
    {
        return playerId == PlayerID;
    }

    public static bool IsOther(byte playerId)
    {
        return playerId == OtherPlayerID;
    }

    public static byte GetOtherPlayer(byte playerId)
    {
        if (ConnectFour.IsPlayerValid(playerId))
            return playerId == 1 ? ConnectFour.PLAYER_TWO : ConnectFour.PLAYER_ONE;
        else
            return 0;
    }

    public static GameObject GetPrefabWithName(string name)
    {
        return instance.prefabs.FirstOrDefault(p => p.name == name);
    }

    // Valid only on server
    public static NetworkConnection GetConnectionOfPlayer(byte playerId)
    {
        return instance.playerIdToNetConn[playerId];
    }

    // Valid only on server
    public static NetworkConnection GetConnectionOfOtherPlayer(byte playerId)
    {
        return instance.playerIdToNetConn[GetOtherPlayer(playerId)];
    }

    // Valid only on server
    public static byte GetPlayerOfConnection(NetworkConnection conn)
    {
        return instance.playerIdToNetConn.FirstOrDefault(x => x.Value == conn).Key;
    }

    public static string GetLocalIPAddress()
    {
        var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());

        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return ip.ToString();
        }

        return "127.0.0.1";
    }

    // This replaces Unity's NetworkServer.SendToAll, since that one doesn't function correctly at all
    public static void SendToAll(short msgType, MessageBase msg)
    {
        if (IsServer)
        {
            for (int i = 0; i < NetworkServer.connections.Count; i++)
            {
                if (NetworkServer.connections[i] != null)
                    NetworkServer.connections[i].Send(msgType, msg);
            }
        }
        else
            Debug.LogError("[CLIENT] NetworkManager : SendToAll - Cannot send to all from client!");
    }

    // These methods allow you to register multiple handlers per MsgType. This function will replace the default handler (if it doesn't exist) for a MsgType by one defined here (InvokeCallbacksForMessage) which calls all registered callbacks in clientHandlers/serverHandlers. It then adds your message to that dictionary
    // By default uNet only allows one handler per message - and will remove the existing handler for each subsequent call to NetworkServer/NetworkClient.RegisterHandler (in NetworkMessageHandlers.cs)

    public static void RegisterClientHandler(short msgType, Action<NetworkMessage> handler)
    {
        instance.RegisterClientHandlerInternal(msgType, handler);
    }

    public static void RegisterServerHandler(short msgType, Action<NetworkMessage> handler)
    {
        instance.RegisterServerHandlerInternal(msgType, handler);
    }

    public static void UnregisterClientHandler(short msgType, Action<NetworkMessage> handler)
    {
        instance.UnregisterClientHandlerInternal(msgType, handler);
    }

    public static void UnregisterServerHandler(short msgType, Action<NetworkMessage> handler)
    {
        instance.UnregisterServerHandlerInternal(msgType, handler);
    }

    // This will be called when a player wins, and will mean all players need to send a ConnectFourMsgType.PlayerReady message before we start another round
    public static void Unready()
    {
        if (!NetworkManager.IsServer)
            return;

        instance.playersReady = 0;
    }

    // Starts the game, with the startingPlayer going first. This is only valid on the server
    // We have to do a delayed start to the game in order to give all clients time to spawn the last player that was added, otherwise references might not be filled correctly
    public static void StartGame()
    {
        if (!NetworkManager.IsServer)
            return;

        Debug.Log("[SERVER] NetworkManager : StartGame - Starting game...");

        // Clear the game board
        ConnectFour.Instance.WipeBoard();

        // CurrentTurnPlayer will be zero if this is the first time starting
        // Otherwise, invert the CurrentTurnPlayer (it will be on the player that won last)
        CurrentTurnPlayer = CurrentTurnPlayer == 0 ? ConnectFour.PLAYER_ONE :
                            GetOtherPlayer(CurrentTurnPlayer);

        // Send a ConnectFourMsgType.StartGame message to all clients. This, like PlayerStartTurn is a ByteMessage with the playerId of the player whose turn it is
        NetworkManager.SendToAll(ConnectFourMsgType.StartGame, new ByteMessage(CurrentTurnPlayer));

        // Start players turn 1 second after starting the game. This is purely for visual timing
        CoroutineHelper.InvokeDelayed(1.0f, () =>
        {
            // Send a PlayerTurn message to all players, starting the players turn
            NetworkManager.SendToAll(ConnectFourMsgType.PlayerStartTurn, new ByteMessage(CurrentTurnPlayer));
        });
    }

    // --- SERVER CALLBACKS ---

    // Built-in Server Callbacks

    // MsgType.Connect handler
    // Called on this server when a client successfully connects to it
    void OnServerConnect(NetworkMessage msg)
    {
        Debug.Log("[SERVER] NetworkManager : OnServerConnect - Client connected with address: " + StripIPv6Formatting(msg.conn.address) + " - Assigning player number " + serverAssignedPlayerId);

        // Add their connection to the playerIdToNetConn lookup table
        playerIdToNetConn[serverAssignedPlayerId] = msg.conn;

        // Give the client that just connected a unique playerId
        msg.conn.Send(ConnectFourMsgType.AssignPlayerNumber, new ByteMessage(serverAssignedPlayerId));

        // Increment serverAssignedPlayerId
        serverAssignedPlayerId++;

        // If we're at the MAX_CONNECTIONS, stop listening for connections and stop broadcasting server availability, then start the game
        if (NetworkServer.connections.Count == MAX_CONNECTIONS)
        {
            Debug.Log("[SERVER] NetworkManager : Server no longer accepting connections");
            NetworkServer.dontListen = true;

            statusHud.SetText("Client connected!");

            // Stop broadcasting
            if (discovery != null && discovery.running)
            {
                Debug.Log("[SERVER] NetworkManager : Stopped broadcasting");
                discovery.StopBroadcast();
            }
        }
    }

    // MsgType.Disconnect handler
    // Called on this server when a client disconnects from it
    void OnServerDisconnect(NetworkMessage msg)
    {
        byte playerId = GetPlayerOfConnection(msg.conn);

        Debug.Log("[SERVER] NetworkMessage : OnServerDisconnect - Client player " + playerId + " disconnected!");
        statusHud.SetTextPersist("Client disconnected, waiting for reconnection...");

        // Decrement serverAssignedPlayerId
        serverAssignedPlayerId--;

        // Decrement playersReady
        playersReady--;

        // If the connection has players or player objects, destroy them
        if ((msg.conn.playerControllers != null && msg.conn.playerControllers.Count > 0) ||
            (msg.conn.clientOwnedObjects != null && msg.conn.clientOwnedObjects.Count > 0))
        {
            NetworkServer.DestroyPlayersForConnection(msg.conn);
        }

        Debug.Log("[SERVER] NetworkManager : OnServerDisconnect - Resuming listening for incoming connections");

        // Resume listening for connection
        NetworkServer.dontListen = false;

        // Start broadcasting
        if (discovery != null)
        {
            discovery.StartAsServer();
        }
    }

    // MsgType.Ready handler
    // Called on the server when a client calls ClientScene.Ready (in ConnectFourBoardSetup)
    void OnServerReadyMessage(NetworkMessage msg)
    {
        Debug.Log("[SERVER] NetworkManager : OnServerReadyMessage - Player " + GetPlayerOfConnection(msg.conn) + " (" + msg.conn.address + ") is ready!");

        // Mark the client as ready
        NetworkServer.SetClientReady(msg.conn);

        // Since uNet doesn't do this automatically, send a MsgType.Ready to all clients, with a ByteMessage value if the unique playerId of the newly-ready client
        NetworkServer.SendToAll(MsgType.Ready, new ByteMessage(GetPlayerOfConnection(msg.conn)));
        //msg.conn.Send(MsgType.Ready, new ByteMessage(GetPlayerOfConnection(msg.conn)));

        // If the number of connections is MAX_CONNECTIONS, all connections are ready (although we don't actually take any action from this)
        if (NetworkServer.connections.Count == MAX_CONNECTIONS &&
            NetworkServer.connections.All(c => c.isReady))
        {
            Debug.Log("[SERVER] NetworkManager : OnServerReadyMessage - All clients ready!");
        }
    }

    // MsgType.AddPlayer handler
    // Called on this server when a client calls ClientScene.AddPlayer. The Player is in this case a prefab with the ConnectFourPlayer script on it
    void OnServerAddPlayerMessage(NetworkMessage msg)
    {
        Debug.Log("[SERVER] NetworkManager : OnServerAddPlayerMessage - Adding player for " + StripIPv6Formatting(msg.conn.address));
        
        AddPlayerMessage addPlayerMsg = msg.ReadMessage<AddPlayerMessage>();

        byte playerId = GetPlayerOfConnection(msg.conn);

        // Spawn player
        var playerObj = GameObject.Instantiate(playerPrefab);
        var player = playerObj.GetComponent<ConnectFourPlayer>();
        player.ownerPlayerId = playerId;

        // Fill some references
        players[playerId] = player;
        
        NetworkServer.AddPlayerForConnection(msg.conn, playerObj, addPlayerMsg.playerControllerId);
    }

    // MsgType.Error handler
    // Called when there are any errors
    void OnServerError(NetworkMessage msg)
    {
        int errorCode = msg.ReadMessage<ErrorMessage>().errorCode;
        NetworkError networkError = (NetworkError)errorCode;

        Debug.Log("[SERVER] NetworkMessage : OnServerError - Error address: " + StripIPv6Formatting(msg.conn.address) + "\n NetworkError." + networkError + " (code " +  errorCode + ")");
    }

    // Custom Server Callbacks

    // ConnectFourMsgType.PrefabSpawnRequest handler
    // Called on the server when the player wants to spawn a registered prefab
    // ! This is no longer used, however, it might be useful for other projects !
    void OnServerPrefabSpawnRequestMessage(NetworkMessage msg)
    {
        var requestMsg = msg.ReadMessage<PrefabSpawnRequestMessage>();

        GameObject prefab;
        GameObject spawn = null;

        bool success = false;
        string errorMessage = "Could not find a prefab with this key";

        if ((prefab = GetPrefabWithName(requestMsg.prefabKey)) != null)
        {
            // Instantiate the object on the server
            spawn = GameObject.Instantiate(prefab, requestMsg.position, requestMsg.rotation);

            // Spawn with client authority
            if (requestMsg.spawnWithClientAuthority)
            {
                if (NetworkServer.SpawnWithClientAuthority(spawn, msg.conn))
                {
                    success = true;
                }
                else
                {
                    success = false;
                    errorMessage = "Failed to spawn with client authority";
                }
            }

            // Spawn with server authority
            else
            {
                NetworkServer.Spawn(spawn);
                success = true;
            }
        }

        if (success)
        {
            Debug.Log("[SERVER] NetworkManager : OnPrefabSpawnMessage - Successfully spawned object using the following message:\n" + requestMsg.ToString());
        }
        else
        {
            Debug.LogError("[SERVER] NetworkManager : OnPrefabSpawnMessage - " + errorMessage + "\n" + requestMsg.ToString());
        }

        // Construct a prefab spawn response message
        var responseMsg = new PrefabSpawnResponseMessage()
        {
            success = success,
            prefabKey = requestMsg.prefabKey,
            spawnedByPlayerId = requestMsg.playerId,
            spawnedWithClientAuthority = requestMsg.spawnWithClientAuthority,
            networkInstanceId = success ? spawn.GetComponent<NetworkIdentity>().netId : default(NetworkInstanceId)
        };

        // Send the response message to all
        NetworkManager.SendToAll(ConnectFourMsgType.PrefabSpawnResponse, responseMsg);
    }

    // ConnectFourMsgType.RestartGame handler
    // Called on the server when a player wants to restart the game
    void OnServerRestartGame(NetworkMessage msg)
    {
        Debug.Log("[SERVER] NetworkManager : OnServerRestartGame - Player " + GetPlayerOfConnection(msg.conn) + " wants to restart the game!");

        // Only allow if we're already in a game
        if (playersReady >= 2)
        {
            StartGame();
        }
        else
        {
            Debug.LogError("[SERVER] NetworkManager : OnServerRestartGame - Can only restart during gameplay!");
            return;
        }
    }

    // ConnectFourMsgType.PlayerReady handler
    // Called on the server when a player is ready to start a new round.  This will occur before the start of every round, and at the end of every round - called after the player has completed their win/end game visuals. The first time this is called will be after the player has been created on the client.
    void OnServerPlayerReady(NetworkMessage msg)
    {
        Debug.Log("[SERVER] NetworkManager : OnServerPlayerReady - Player " + GetPlayerOfConnection(msg.conn) + " is ready!");

        playersReady++;

        if (playersReady >= 2)
        {
            Debug.Log("[SERVER] NetworkManager : OnServerPlayerReady - All players ready!");

            // Start the game
            StartGame();
        }
    }

    // --- CLIENT CALLBACKS ---

    // Built-in Client Callbacks

    // MsgType.Connect handler
    // Called on this client when it successfully connects to the server
    void OnClientConnect(NetworkMessage msg)
    {
        Debug.Log("[CLIENT] NetworkManager : OnClientConnect - Connected to server at address: " + StripIPv6Formatting(msg.conn.address) + ", connectionId " + msg.conn.connectionId);

        if (!IsHost) statusHud.SetText("Connected to server!");
    }

    // MsgType.Disconnect handler
    // Called on this client when it disconnects from the server
    void OnClientDisconnect(NetworkMessage msg)
    {
        Debug.Log("[CLIENT] NetworkManager : OnClientDisconnect - Disconnected from server at address: " + StripIPv6Formatting(msg.conn.address));
        Debug.Log("[CLIENT] NetworkManager : OnClientDisconnect - Attempting reconnection...");

        statusHud.SetText("Disconnected");

        // Stop the client
        ClientScene.DestroyAllClientObjects();

        // Try to reconnect after client objects are destroyed
        CoroutineHelper.InvokeDelayed(1, Open);
    }

    // MsgType.Error handler
    // Called on this client when there was a network error
    void OnClientError(NetworkMessage msg)
    {
        int errorCode = msg.ReadMessage<ErrorMessage>().errorCode;
        NetworkError networkError = (NetworkError)errorCode;

        Debug.Log("[CLIENT] NetworkMessage : OnClientError - Error address: " + StripIPv6Formatting(msg.conn.address) + "\n NetworkError." + networkError + " (code " + errorCode + ")");
    }

    // MsgType.Ready handler
    // Called on the client by the server for when ANY client is ready (via ClientScene.Ready, this is done after setting up the board in ConnectFourBoardSetup). This is manually done, since unet doesn't do this automatically. The MessageBase type is a ByteMessage, containing the playerId of the player now ready
    // Here we ask the server to add a player for us
    void OnClientReady(NetworkMessage msg)
    {
        byte playerId = msg.ReadMessage<ByteMessage>().value;
        Debug.Log("[CLIENT] NetworkManager : OnClientReadyMessage - Player " + playerId + " is ready!");

        // Check to see if we readied, then add a player
        if (NetworkManager.IsMe(playerId))
        {
            Debug.Log("[CLIENT] ConnectFourBoard : OnClientReady - Requesting AddPlayer for " + NetworkManager.PlayerID);

            // Send an AddPlayer message to server
            ClientScene.AddPlayer(0);
        }
    }

    // MsgType.AddPlayer handler
    // Called on the client by the server for when the player of ANY client is created. This is manually called since uNet doesn't do this automatically. The message type is a PrefabSetupResponseMessage
    void OnClientPlayerAdded(NetworkMessage msg)
    {
        Debug.Log("[CLIENT] NetworkManager : OnClientPlayerAdded - Server created player for us!");
    }

    // NetworkDiscoverySimple.OnReceivedBroadcastEvent handler
    // Called when the NetworkDiscovery recieved a broadcast from a server
    void OnClientReceivedBroadcast(string fromAddress, string data)
    {
        if (!client.isConnected)
        {
            Debug.Log("[CLIENT] NetworkManager : OnReceivedBroadcast - fromAddress: " + StripIPv6Formatting(fromAddress) + ", data: " + data + ")");

            // Connect to the first server that broadcasts
            client.Connect(fromAddress, Settings.Current.serverPort);

            // Stop listening to broadcasts
            if (discovery != null && discovery.running)
            {
                discovery.StopBroadcast();
                Debug.Log("[CLIENT] NetworkManager : OnReceivedBroadcast - Stopped listening for broadcasting servers");
            }
        }
    }

    // Custom Client Callbacks

    // ConnectFourMsgType.AssignPlayerNumber handler
    // Called on this client when the server assigns us a player number
    void OnClientPlayerNumberAssigned(NetworkMessage msg)
    {
        clientPlayerId = msg.ReadMessage<ByteMessage>().value;

        Debug.Log("[CLIENT] NetworkManager : OnPlayerNumberAssigned - " + clientPlayerId);
    }

    // --- UTILITY FUNCTIONS ---

    // Strips the ::fffff: notation from an IPv6-formatted IPv4 address
    public static string StripIPv6Formatting(string address)
    {
        const string NOTATION = "::ffff:";

        if (address.StartsWith(NOTATION))
            return address.Substring(NOTATION.Length);
        else
            return address;
    }

    // Converts a MsgType OR ConnectFourMsgType to a string, including the class portion
    public static string MsgTypeToString(short msgType)
    {
        return msgType <= MsgType.Highest ?
            "MsgType." + MsgType.MsgTypeToString(msgType) :
            "ConnectFourMsgType." + ConnectFourMsgType.MsgTypeToString(msgType);
    }
}