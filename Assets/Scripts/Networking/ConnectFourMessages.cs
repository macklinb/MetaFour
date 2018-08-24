using System;
using UnityEngine;
using UnityEngine.Networking;

public static class ConnectFourMsgType
{
    public const short AssignPlayerNumber = MsgType.Highest + 1;
    public const short BoardSize = MsgType.Highest + 2;
    public const short BoardOffset = MsgType.Highest + 3;
    public const short PrefabSpawnRequest = MsgType.Highest + 4;
    public const short PrefabSpawnResponse = MsgType.Highest + 5;
    public const short StartGame = MsgType.Highest + 6;
    public const short RestartGame = MsgType.Highest + 7;
    public const short PlayerStartTurn = MsgType.Highest + 8;
    public const short PlayerReady = MsgType.Highest + 9;

    public static string MsgTypeToString(short msgType)
    {
        msgType -= MsgType.Highest;

        switch (msgType)
        {
            case 1: return "AssignPlayerNumber";
            case 2: return "BoardSize";
            case 3: return "BoardOffset";
            case 4: return "PrefabSpawnRequest";
            case 5: return "PrefabSpawnResponse";
            case 6: return "StartGame";
            case 7: return "RestartGame";
            case 8: return "PlayerStartTurn";
            case 9: return "PlayerReady";
            default: return "Unknown";
        }
    }
};

public class ByteMessage : MessageBase
{
    public byte value;

    public ByteMessage() : base() { }

    public ByteMessage(byte value) : base()
    {
        this.value = value;
    }
}

public class BoardSizeMessage : MessageBase
{
    public byte playerId;
    public Vector2 size;

    public BoardSizeMessage() { }

    public BoardSizeMessage(byte playerId, Vector2 size) : base()
    {
        this.playerId = playerId;
        this.size = size;
    }
}

// This message is only used when we are using an offset board, and zeroed headset (define META_NO_OFFSET)
public class BoardOffsetMessage : MessageBase
{
    public byte playerId;
    public Vector3 offset;
    public Quaternion rotation;

    public BoardOffsetMessage() { }

    public BoardOffsetMessage(byte playerId, Vector3 offset, Quaternion rotation) : base()
    {
        this.playerId = playerId;
        this.offset = offset;
        this.rotation = rotation;
    }
}

public class PrefabSpawnRequestMessage : MessageBase
{
    // The player requesting this spawn
    public byte playerId;

    // The name of the prefab to spawn
    public string prefabKey;

    // The server world position/rotation of the spawn
    public Vector3 position;
    public Quaternion rotation;

    // If true, the spawned object will give authority to the player requesting the spawn
    public bool spawnWithClientAuthority;

    public override string ToString()
    {
        return string.Format(" playerId: {0}\n prefabKey: {1}\n position: {2}\n rotation: {3}\n spawnWithClientAuthority: {4}", playerId, prefabKey, position, rotation, spawnWithClientAuthority);
    }
}

public class PrefabSpawnResponseMessage : MessageBase
{
    // Whether this spawn was successful or not
    public bool success;

    // The prefabKey of the spawn
    public string prefabKey;

    // Identifies the playerId of the player who requested the spawn
    public byte spawnedByPlayerId;

    // True if the spawn gave authority of the object to the requester
    public bool spawnedWithClientAuthority;

    // The NetworkInstanceId of the spawned object. This will be the same across the server and clients. To get a reference to the locally-spawned object using this id:
    // On clients, you can use ClientScene.FindLocalObject
    // On server, you can use NetworkServer.FindLocalObject
    public NetworkInstanceId networkInstanceId;

    // Property shortcut to compare spawnedByPlayerId to the current NetworkManager's playerId
    public bool SpawnedByMe
    {
        get { return spawnedByPlayerId == NetworkManager.PlayerID; }
    }

    // Property shortcut to retrieve a reference to the local object.
    public GameObject spawnedObject
    {
        get
        {
            if (success == false)
                return null;
            
            if (NetworkManager.IsClient)
                return ClientScene.FindLocalObject(networkInstanceId);
            else if (NetworkManager.IsServer)
                return NetworkServer.FindLocalObject(networkInstanceId);
            else
                return null;
        }
    }

    public override string ToString()
    {
        return string.Format(" success: {0}\n prefabKey: {1}\n spawnedByPlayerId: {2}\n spawnedWithClientAuthority: {3}\n networkInstanceId: {4}\n SpawnedByMe: {5}", success, prefabKey, spawnedByPlayerId, spawnedWithClientAuthority, networkInstanceId, SpawnedByMe);
    }
}