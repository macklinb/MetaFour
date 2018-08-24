// Interface for network calls to/from ConnectFour.
// This class uses a mix of NetworkBehaviour callbacks (e.g. Command/ClientRpc) and NetworkMessages, the former is used more for gameplay mechanics, while the latter is used for game state messages such as ending the game.
// Coordinates are inverted in the X axis for player 2, since game logic is done where 0 is on the left, from the servers perspective.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using DG.Tweening;

public class ConnectFourPlayer : NetworkBehaviour
{
    [SyncVar]
    public byte ownerPlayerId;

    // True if it is the turn of this player
    bool isMyTurn;

    int triggerLayer;

    // If true, we enable hover checks (and therefore, allow dropping)
    bool doHoverChecks;

    // Whether the player's main ball is hovering over the top of the board, and which column it is on
    bool isHovering, isHoveringLast;
    int hoverColumn, hoverColumnLast;

    int debugColumn = -1;

    Meta.GrabInteraction grabInteraction;

    [ClientCallback]
    void Awake()
    {
        triggerLayer = LayerMask.NameToLayer("Trigger");
        grabInteraction = GetComponent<Meta.GrabInteraction>();
    }

    [ClientCallback]
    void OnDestroy()
    {
        if (isLocalPlayer)
            NetworkManager.UnregisterClientHandler(ConnectFourMsgType.PlayerStartTurn, OnPlayerStartTurn);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        name = ownerPlayerId + "_Ball";

        // Send ConnectFourBoard references to this object
        if (NetworkManager.IsMe(ownerPlayerId)) // synonymous to isLocalPlayer
            ConnectFourBoard.Instance.SetOurBall(this.gameObject);
        else
        {
            ConnectFourBoard.Instance.SetOtherBall(this.gameObject);

            // Delete the GrabInteraction on this ball, if it is not ours
            Destroy(grabInteraction);
        }
    }

    // Called when our ConnectFourPlayer starts. This should only happen once
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // Register custom callbacks
        NetworkManager.RegisterClientHandler(ConnectFourMsgType.PlayerStartTurn, OnPlayerStartTurn);

        // Tell the server that we're ready for the first round
        NetworkManager.Client.Send(ConnectFourMsgType.PlayerReady, new EmptyMessage());
    }

    // Client callback for when any player starts their turn, the NetworkMessage is a ByteMessage containing the value of the playerId whose turn it is
    void OnPlayerStartTurn(NetworkMessage msg)
    {
        byte playerId = msg.ReadMessage<ByteMessage>().value;
        isMyTurn = NetworkManager.IsMe(playerId);

        // Allow hover checks when it's my turn
        doHoverChecks = isMyTurn;

        // Allow grab interaction when it's my turn
        grabInteraction.enabled = isMyTurn;

        // Do the AI turn
        if (isMyTurn && ConnectFourNPC.Instance.IsEnabled)
        {
            // We need to pass it an OnComplete delegate, as the AI calculation might take a while
            ConnectFourNPC.Instance.DoAIPlayerMove((move) =>
            {
                var pos = ConnectFourBoard.Instance.GetHoverPositionOfColumn(move);
                transform.DOMove(pos, 2.0f).SetEase(Ease.InOutSine).SetDelay(1.5f).OnComplete(() =>
                {
                    Invoke("Drop", 0.5f);
                });
            });
        }

        debugColumn = -1;
    }

    

    [ClientCallback]
    void Update()
    {
        // If this ball doesn't belong to us, ignore this. We only do Update for our ball
        if (!NetworkManager.IsMe(ownerPlayerId))
            return;

        // Don't continue if it's not this players turn
        if (!isMyTurn)
            return;

        if (doHoverChecks)
        {
            // While the player has their main ball above the columns
            if (isHovering = ConnectFourBoard.Instance.WithinHoverBounds(transform.position))
            {
                isHoveringLast = true;

                // Get the column index of the column nearest to the position (returns -1 if not near)
                hoverColumn = ConnectFourBoard.Instance.CheckHoverBounds(transform.position);

                // Check if the column is within range, and not occupied, show a visualBall above the column
                if (hoverColumn != hoverColumnLast && ConnectFour.IsColumnInRange(hoverColumn) && !IsColumnFull(hoverColumn))
                {
                    hoverColumnLast = hoverColumn;

                    // Send CmdHoverAboveColumn to server, with the column index
                    // This effectively "snaps" the visualBall to the column - disable this effect if we're an AI player
                    if (!ConnectFourNPC.Instance.IsEnabled)
                        CmdHoverAboveColumn(NetworkManager.PlayerID, hoverColumn);
                }

                // Drop with space
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    Drop();
                }
            }
            else if (isHoveringLast)
            {
                StopHovering();
            }
        }
        
        // Manually move the player with left/right arrow
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                debugColumn--;
                if (debugColumn < 0) debugColumn = ConnectFour.BOARD_WIDTH - 1;
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                debugColumn++;
                if (debugColumn >= ConnectFour.BOARD_WIDTH) debugColumn = 0;
            }

            transform.position = ConnectFourBoard.Instance.GetHoverPositionOfColumn(debugColumn);
            hoverColumn = hoverColumnLast = debugColumn;
            CmdHoverAboveColumn(ownerPlayerId, debugColumn);
        }
    }

    // --- NETWORKING COMMANDS/RPC ---

    // Called by the client (and run on the server)
    // Sent when this players main ball is hovered over a specific column. This is called even when a column is fully occupied. Since the server side command does no logic, the column is the client index
    [Command]
    void CmdHoverAboveColumn(byte playerId, int column)
    {
        // Invert column index based on the playerId. serverColumn is the server-side index of the column
        int serverColumn = ClientToServerColumn(playerId, column);

        // Send one ClientRpc to each client, always invert the column for the other player
        Debug.Log("[SERVER] ConnectFourPlayer : CmdHoverAboveColumn - Player " + playerId + " hovering above column " + column + " (server column " + serverColumn + ")");

        // Send to all clients
        RpcHoverAboveColumn(playerId, column);
    }

    // Called by the server (and run on all clients, on the object it was called from)
    // Here we take action on a player hovering their ball above a column
    [ClientRpc]
    void RpcHoverAboveColumn(byte playerId, int column)
    {
        // Invert the column if from the other player
        int clientColumn = ClientToLocalColumn(playerId, column);

        Debug.Log("[CLIENT] ConnectFourPlayer : TargetHoverAboveColumn - Player " + playerId + " hovering above column " + column + " (" + clientColumn + " from our perspective)");

        // Show visual ball above column
        ConnectFourBoard.Instance.ShowVisualBallAboveColumn(playerId, clientColumn);
    }

    // Hides the currently hovering visualBall, resetting the hoverColumn and setting isHovering to false
    void StopHovering()
    {
        CmdStopHovering(ownerPlayerId);
        hoverColumn = hoverColumnLast = -1;
        isHovering = isHoveringLast = false;
    }

    [Command]
    void CmdStopHovering(byte playerId)
    {
        // Send one ClientRpc to each client, always invert the column for the other player
        Debug.Log("[SERVER] ConnectFourPlayer : CmdStopHovering - Player " + playerId + " stopped hovering");

        // Send to all clients
        RpcStopHovering(playerId);
    }

    [ClientRpc]
    void RpcStopHovering(byte playerId)
    {
        Debug.Log("[CLIENT] ConnectFourPlayer : RpcStopHovering - Player " + playerId + " stopped hovering");

        // Hide visual ball
        ConnectFourBoard.Instance.HideVisualBall(playerId);
    }

    // Returns true if we can drop a ball
    bool CanDrop()
    {
        return isMyTurn && isHovering && ConnectFour.IsColumnInRange(hoverColumn) && !IsColumnFull(hoverColumn);
    }

    // Drops the ball in the current hoverColumn
    void Drop()
    {
        // Return if we're not allowed to drop a ball at the moment
        if (!CanDrop()) return;

        // We're now no longer hovering or doing hover checks
        isHovering = isHoveringLast = false;
        doHoverChecks = false;

        // Call CmdDrop
        CmdDrop(ownerPlayerId, hoverColumn);

        // Reset hoverColumn
        hoverColumn = hoverColumnLast = -1;

        // Disallow further grabInteraction
        grabInteraction.enabled = false;
    }

    // Called by the client (and run on the server)
    // When the client drops a ball into a specific column on the board. <column> is in client coordinates, and will be translated to server coordinates
    [Command]
    void CmdDrop(byte playerId, int column)
    {
        int row = -1;

        // Invert the column based on the player that dropped it. This converts it to a server coordinate
        int serverColumn = ClientToServerColumn(playerId, column);

        // Call drop in the ConnectFour instance
        bool success = ConnectFour.Instance.Drop(playerId, serverColumn, out row);

        Debug.Log("[SERVER] ConnectFourPlayer : CmdDrop - Player " + playerId  + " dropped a ball in column " + column + (success ? ", landing in row " + row : " unsuccessfully"));

        // Call RpcDrop on all clients to do the visuals. Here we send the server coordinate
        RpcDrop(playerId, success, serverColumn, row);

        // Check for win condition if the ball was dropped
        if (success)
        {
            // Print out the current board
            ConnectFour.Instance.PrintBoard();

            Vector2Int coord = new Vector2Int(serverColumn, row);
            Vector2Int start, end;
            string seqType;

            // Check for a win condition only in the area around the coordinate
            if (ConnectFour.Instance.CheckForWinIncremental(playerId, coord, out start, out end, out seqType))
            {
                Debug.Log("[SERVER] ConnectFourPlayer : CmdDrop - Player " + playerId + " wins!");

                // Unready all the players in preperation for a new round
                NetworkManager.Unready();

                // Call RpcWin on all clients. This triggers win/end visuals.
                RpcWin(playerId, start.x, start.y, end.x, end.y, seqType, System.Guid.NewGuid().GetHashCode());
            }

            // Check for a stalemate
            else if (ConnectFour.Instance.CheckForStalemate())
            {
                Debug.Log("[SERVER] ConnectFourPlayer : CmdDrop - Stalemate!");

                // Unready all players
                NetworkManager.Unready();

                // Call RpcStalemate on all clients. This triggers stalemate visuals
                RpcStalemate(new System.Random().Next(int.MinValue, int.MaxValue));
            }

            // Continue game
            else
            {
                var otherPlayerId = NetworkManager.GetOtherPlayer(playerId);
                NetworkManager.CurrentTurnPlayer = otherPlayerId;

                // Tell all clients it's the other players turn
                NetworkManager.SendToAll(ConnectFourMsgType.PlayerStartTurn, new ByteMessage(otherPlayerId));
            }
        }
    }

    // Called by the server (and run on all clients, on the object it was called from)
    // Called as a direct result of CmdDrop, containing the player that dropped the ball - and if that drop was successful, as well as the x/y index of the space that the ball now resides (in server coordinates) 
    [ClientRpc]
    void RpcDrop(byte playerId, bool success, int x, int y)
    {
        int serverX = x;

        // Convert the x coord (column) from a server to a client column
        x = ServerToLocalColumn(serverX);

        Debug.LogFormat("[CLIENT] ConnectFourPlayer : RpcDrop - Player {0}{1} dropped a ball at coord {2}, {3} (server coord {4}, {3}){5}", playerId, (NetworkManager.IsMe(playerId) ? " (me!)" : ""), x, y, serverX, (success ? "" : " unsuccessfully"));

        if (success)
        {
            // Check to see if the column is now occupied (an occupied column would be if y == 0)
            if (y == 0)
            {
                ConnectFourBoard.Instance.occupiedColumnsFlags |= ColumnToFlag(x);

                Debug.LogFormat("[CLIENT] ConnectFourPlayer : RpcSetColumnOccupied - Column {0} (server column {1}) is now occupied! Mask is {2}", x, serverX, ConnectFourBoard.Instance.occupiedColumnsFlags, System.Convert.ToString(ConnectFourBoard.Instance.occupiedColumnsFlags, 2).PadLeft(ConnectFour.BOARD_WIDTH, '0'));
            }

            // Drop the visual ball
            ConnectFourBoard.Instance.DropBallVisual(playerId, x, y);

            // Disable the player ball, and move it to the spawner
            ConnectFourBoard.Instance.HideBall(playerId);
            ConnectFourBoard.Instance.MoveBallToSpawner(playerId);

            // If the client running on this instance has enable AI player, and the other player did their move, simulate their move
            if (ConnectFourNPC.Instance.IsEnabled && playerId == NetworkManager.OtherPlayerID)
            {
                ConnectFourNPC.Instance.DoPlayerMove(x, y);
            }
        }
        else
        {
            // If this was us - re-allow ball placement, as the last drop failed
            if (NetworkManager.IsMe(playerId))
            {
                doHoverChecks = true;
                grabInteraction.enabled = true;
            }
        }
    }

    // Called by the server (and run on all clients, on the object it was called from)
    // Notifies clients that a player has won, and the sequence that was won. All the coordinates will be in server coordinates.
    [ClientRpc]
    void RpcWin(byte playerId, int startX, int startY, int endX, int endY, string seqType, int randomSeed)
    {
        Debug.LogFormat("[CLIENT] ConnectFourPlayer : RpcWin - Player {0} wins! Win is from x{1}y{2} to x{3}y{4} (server coords)", playerId, startX, startY, endX, endY);

        // Convert the x coordinates to a client coordinate
        startX = ServerToLocalColumn(startX);
        endX = ServerToLocalColumn(endX);

        // Do both the Win and EndGame visuals
        ConnectFourBoard.Instance.WinVisuals(playerId, new Vector2Int(startX, startY), new Vector2Int(endX, endY), seqType);
        ConnectFourBoard.Instance.EndGameVisuals(randomSeed);

        // ! Previously this function was inline
        ReadyAfterEndVisuals();
    }

    // Called by the server (and run on all clients, on the object it was called from)
    // Notifies clients that the game is a stalemate
    [ClientRpc]
    void RpcStalemate(int randomSeed)
    {
        Debug.Log("[CLIENT] ConnectFourPlayer : RpcStalemate - Stalemate!");

        // Do stalemate and end game visuals
        ConnectFourBoard.Instance.StalemateVisuals();
        ConnectFourBoard.Instance.EndGameVisuals(randomSeed);

        ReadyAfterEndVisuals();
    }

    // Waits for win and end game visuals to complete, then messages the server that we're done. Note that we don't do a command here, since we might be running this routine on the other player object, which we don't have authority for.
    void ReadyAfterEndVisuals()
    {
        if (!NetworkManager.IsClient) return;

        CoroutineHelper.InvokeCondition(() => ConnectFourBoard.Instance.completedEndVisuals, () =>
        {
            ConnectFourBoard.Instance.completedEndVisuals = false;
            NetworkManager.Client.Send(ConnectFourMsgType.PlayerReady, new EmptyMessage());
        });
    }

    // --- MONOBEHAVIOUR CALLBACKS ---
    
    /*
    [ClientCallback]
    void OnTriggerEnter(Collider other)
    {
        // If this ball doesn't belong to us, ignore this. We only do physics checks for our ball
        if (!NetworkManager.IsMe(ownerPlayerId))
            return;

        if (other == ConnectFourBoard.Instance.hoverCollider)
        {
            Debug.Log("ConnectFourPlayer : OnTriggerEnter - Started hovering");

            // Start hovering column checks
            isHovering = true;
        }
    }

    [ClientCallback]
    void OnTriggerExit(Collider other)
    {
        // If this ball doesn't belong to us, ignore this. We only do physics checks for our ball
        if (!NetworkManager.IsMe(ownerPlayerId))
            return;

        if (other == ConnectFourBoard.Instance.hoverCollider && isHovering)
        {
            Debug.Log("ConnectFourPlayer : OnTriggerExit - Stopped hovering");
            CmdStopHovering(ownerPlayerId);

            // Stop hovering column checks
            isHovering = false;
            hoverColumn = -1;
            hoverColumnLast = -1;
        }
    }
    */

    // --- PUBLIC FUNCTIONS ---

    // Called by Meta GrabInteraction for when the player drops the ball
    public void OnDisengaged()
    {
        // Don't do anything if it's not our turn
        if (!isMyTurn) return;

        // Don't do anything if we're not doing hover checks
        if (!doHoverChecks) return;

        // Drop the ball in the column, if we're hovering above one
        if (CanDrop())
        {
            Drop();
        }

        // Stop hovering and return the ball to it's original position
        else
        {
            StopHovering();
            transform.position = ConnectFourBoard.Instance.GetSpawnPositionOfPlayer(ownerPlayerId);
        }
    }

    // Called by Meta GrabInteraction HoverStart/End - and will change the brightness of the player ball in reaction to the hover. This is only done locally, as it's unlikely the other player will notice it.
    public void OnHandHover(bool value)
    {
        // Highlight the main ball
        ConnectFourBoard.Instance.SelectBall(ownerPlayerId, value);
    }

    // --- UTILITY FUNCTIONS ---

    // A server column is one that matches up with the logic being done on the server
    // A client column is one that matches up with a specific client. Player 1's columns are always the same as the server, and player 2's are always inverted
    // A local column is one that matches with the client on this running instance. It is the same as a client column, but the term is used for when the column is referring to one that is in the space of the client on the running instance
    
    bool IsColumnFull(int localColumn)
    {
        // Return false if out of range
        if (localColumn < 0 || localColumn >= ConnectFour.BOARD_WIDTH)
            return false;

        return (ConnectFourBoard.Instance.occupiedColumnsFlags & ColumnToFlag(localColumn)) != 0;
    }

    int ColumnToFlag(int column)
    {
        return 1 << column;
    }

    // Returns ServerToClientColumn, but passes our playerId (as in the player of the current instance, NOT the player id of this object)
    int ServerToLocalColumn(int serverColumn)
    {
        return ServerToClientColumn(NetworkManager.PlayerID, serverColumn);
    }

    // Inverts the column if the playerId is 2, otherwise returns the same value. Used when retrieving a value from the server which has been used in server processing.
    int ServerToClientColumn(byte playerId, int serverColumn)
    {
        if (playerId == ConnectFour.PLAYER_TWO)
            return ConnectFour.InvertColumn(serverColumn);

        return serverColumn;
    }

    // Returns ClientToServerColumn, but passes our playerId (as in the player of the current instance, NOT the player id of this object)
    int LocalToServerColumn(int localColumn)
    {
        return ClientToServerColumn(NetworkManager.PlayerID, localColumn);
    }
    
    // Inverts the column, only if playerId is player 2. This is mostly use in Command functions, where we are sent a client coordinate and have to convert it to a server one
    int ClientToServerColumn(byte playerId, int clientColumn)
    {
        if (playerId == ConnectFour.PLAYER_TWO)
            return ConnectFour.InvertColumn(clientColumn);
        else
            return clientColumn;
    }

    // Converts a column of the playerId to a column in our space
    // Inverts the column if playerId is of the other player, otherwise returns the same value. This is used when we get the client coordinate of a player and have to convert it to our own.
    int ClientToLocalColumn(byte playerId, int clientColumn)
    {
        if (NetworkManager.IsMe(playerId))
            return clientColumn;
        else
            return ConnectFour.InvertColumn(clientColumn);
    }
}
