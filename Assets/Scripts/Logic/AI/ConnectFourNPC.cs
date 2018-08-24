using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

// The bot works by simulating every possible move in the next 7 turns (this is the minimum amount of turns it takes to win a game). The simulation occurs in client space
// For each move, we give the move a score - based on the outcome of that move
// The order of the moves is always 0 (root), 1, 2, 1, 2...
public class ConnectFourNPC : MonoBehaviour
{
    public static ConnectFourNPC Instance { get; private set; }

    public bool IsEnabled;
    public NetworkStatusHud statusHud;

    // Represents the top-level state of the board, an empty board
    TreeNode<BoardState> rootNode;

    // Represents the current state of the board (i.e. which node we're currently on). This will always be the board state prior to the NPC's move.
    TreeNode<BoardState> currentNode;

    // The playerId of the starting player for this round. If the starting player is '2', we invert the entirity of the node tree
    byte startingPlayerId;

    float toggleTimer = 0f;
    const float toggleTimerTime = 5f;

    const int READ_AHEAD_TURNS = 4;

    System.Diagnostics.Process headlessProcess;

    void Awake()
    {
        Instance = this;

        // Don't run the headless instance in the editor (mainly because we don't have a solid reference to a build executable)
        if (Settings.Current.gameMode == Settings.GAME_MODE_SINGLEPLAYER)
        {
            if (Application.isEditor)
            {
                Debug.LogError("ConnectFourNPC : Cannot start singleplayer instance in editor!");
                return;
            }

            // Check if we're running in headless/batchmode, and if so, enable the AI immediately
            if (IsHeadlessMode())
            {
                Debug.Log("--- RUNNING IN HEADLESS MODE ---");
                IsEnabled = true;
            }

            // Otherwise start the headless instance
            else
            {
                // The headless instance config is always the opposite of the host config
                string headlessConfig = (Settings.Current.networkConfig == Settings.NETWORK_CONFIG_CLIENT) ?
                                         Settings.NETWORK_CONFIG_HOST : Settings.NETWORK_CONFIG_CLIENT;

                // Log to a different file (otherwise the data is logged to the same file as the host)
                string logFilePath = "\"" + System.IO.Path.GetDirectoryName(Settings.Current.LogFilePath) + "\\output_log_headless.txt\"";
                
                // Construct ProcessStartInfo
                var startInfo = new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = Settings.Current.ExecutablePath,
                    Arguments = string.Format("-batchmode -nographics -logFile {0} -config {1} -mode {2} -address {3} -meta_enabled {4}", logFilePath, headlessConfig, Settings.GAME_MODE_SINGLEPLAYER, "127.0.0.1", false),
                };

                // Start process
                headlessProcess = System.Diagnostics.Process.Start(startInfo);

                Debug.Log("ConnectFourNPC : Started headless process (id: " + headlessProcess.Id + ") using the following arguments:\n" + headlessProcess.StartInfo.FileName + " " + headlessProcess.StartInfo.Arguments);
            }
        }
    }

    // Returns true if the process is running in headless mode
    public static bool IsHeadlessMode()
    {
        return (System.Environment.GetCommandLineArgs().Contains("-batchmode") ||
                System.Environment.GetCommandLineArgs().Contains("-nographics") ||
                SystemInfo.graphicsDeviceID == 0 ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null);
    }

    void OnApplicationQuit()
    {
        if (headlessProcess != null)
        {
            headlessProcess.CloseMainWindow();
            headlessProcess.Close();
        }
    }

    void Start()
    {
        if (NetworkManager.IsClient)
        {
            NetworkManager.RegisterClientHandler(ConnectFourMsgType.StartGame, OnStartGame);
        }

        /*
        // Debug all the winningSequences
        for (int i = 0; i < ConnectFour.winningSequences.Count; i++)
        {
            var connectFour = new ConnectFour();
            var board = ConnectFour.EmptyBoard;

            foreach (var coord in ConnectFour.winningSequences[i])
            {
                board[coord.x, coord.y] = 1;
            }
            
            connectFour.SetBoard(board);
            connectFour.PrintBoard();
        }
        */
    }

    void Update()
    {
        if (toggleTimer <= 0.0f)
        {
            // Push P to toggle AI on or off
            if (Input.GetKeyDown(KeyCode.P))
            {
                IsEnabled = !IsEnabled;

                if (IsEnabled)
                {
                    Debug.Log("ConnectFourNPC : Enabling AI player");
                    statusHud.SetTextPersist("AI enabled");
                }
                else
                {
                    Debug.Log("ConnectFourNPC : Disabling AI player");
                    statusHud.SetText("AI disabled");
                }
                
                NetworkManager.Client.Send(ConnectFourMsgType.RestartGame, new EmptyMessage());
                toggleTimer = toggleTimerTime;
            }
        }
        else
        {
            toggleTimer -= Time.deltaTime;
        }
    }

    void OnStartGame(NetworkMessage msg)
    {
        startingPlayerId = msg.ReadMessage<ByteMessage>().value;
        Debug.Log("ConnectFourNPC : OnStartGame - startingPlayerId is " + startingPlayerId);

        Init(ConnectFour.EmptyBoard);
    }

    public void Init(byte[,] rootBoard)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Debug.Log("ConnectFourNPC : Initializing...");

        // Construct rootData using an empty board as the root
        var rootData = new BoardState();
        rootData.board = new ConnectFour();
        rootData.board.SetBoard(rootBoard);

        // Make a new rootNode using the rootData
        rootNode = new TreeNode<BoardState>(rootData);

        // Simulate the entire board
        //Construct(rootNode);
        //sw.Stop();
        //Debug.Log("ConnectFourNPC : Init took " + sw.ElapsedMilliseconds + "ms");

        ConstructAsync(rootNode, () =>
        {
            sw.Stop();
            Debug.Log("ConnectFourNPC : Init took " + sw.ElapsedMilliseconds + "ms");
        });

        // Set the currentNode to the root
        currentNode = rootNode;
    }
    
    // This will simulate the move that the other (non-NPC) player did, and make that the currentNode
    // Assumes that the currentNode is a move made by the NPC
    public void DoPlayerMove(int column, int row)
    {
        // If the currentNode's move is the other players, ignore (can't do two player turns in a row)
        if (currentNode.Data.move.playerId == NetworkManager.OtherPlayerID)
        {
            Debug.LogError("ConnectFourNPC : DoPlayerMove(" + column + ") - Cannot do two player turns in a row");
            return;
        }

        // Navigate to the currentNode that the player made (effectively discarding all other turns)
        foreach (var child in currentNode.Children)
        {
            if (child.Data != null && child.Data.move.column == column)
            {
                currentNode = child;
                Debug.Log("ConnectFourNPC : DoPlayerMove - Set currentNode to the following BoardState:");
                Debug.Log(currentNode.Data.ToString());
                break;
            }
        }
    }
    
    // This will select the best possible move for the NPC to do, and will make that the currentNode.
    // Assumes that the currentNode is a move made by the player
    public void DoAIPlayerMove(System.Action<int> OnComplete)
    {
        if (currentNode.Data.move.playerId == NetworkManager.PlayerID)
        {
            Debug.LogError("ConnectFourNPC : DoAIPlayerMove - It isn't the AI's turn yet!");
            return;
        }

        // Construct one more level deep, then select the best move
        ConstructAsync(currentNode, () =>
        {
            Debug.Log("ConnectFourNPC : DoAIPlayerMove - Selecting move...");

            Debug.Log("ConnectFourNPC - DoAIPlayerMove - Computers possible moves:\n" + string.Join(" | ", currentNode.Children.Where(n => n.Data != null).Select(n => n.Data.score.ToString()).ToArray()));

            TreeNode<BoardState> selectedNode = null;

            // Select only nodes that have data. This should always result in at least one node (as this function isn't called on a win/loss/stalemate)
            var validNodes = currentNode.Children.Where(n => n.Data != null);

            // Select the best move. Here we get a list of nodes that are the best possible moves.
            // In most cases, there will be only one value, but in the case that there are multiple nodes that result in the same score, we pick a random one.
            int bestScore = validNodes.Max(n => n.Data.score);
            var bestNodes = validNodes.Where(n => n.Data.score == bestScore).ToArray();

            // Select the only node if the bestNodes array has only 1 element
            if (bestNodes.Length == 1)
            {
                selectedNode = bestNodes[0];
            }

            // Pick a random node if we haven't found a bestNode
            // This may occur if the AI turn is the first move, as all moves will result in a score of zero
            else if (bestNodes.Length > 1)
            {
                Debug.Log("ConnectFourNPC : DoAIPlayerMove - No single best move found! Picking random move between (" + string.Join(", ", bestNodes.Select(n => n.Data.move.column.ToString()).ToArray()) + ")");

                selectedNode = bestNodes.ElementAt(new System.Random().Next(0, bestNodes.Count()));
            }
            else if (bestNodes.Length == 0)
            {
                throw new System.InvalidOperationException();
            }

            Debug.Log("ConnectFourNPC : DoAIPlayerMove - Picked column " + selectedNode.Data.move.column);

            // Make the selected move the current
            currentNode = selectedNode;

            // Invoke OnComplete, passing the selected move's column
            OnComplete(selectedNode.Data.move.column);

            // Construct the next level
            ConstructAsync(currentNode);

            Debug.Log("ConnectFourNPC - DoAIPlayerMove - Player's next possible moves:\n" + string.Join(" | ", currentNode.Children.Where(n => n.Data != null).Select(n => n.Data.score.ToString()).ToArray()));
        });
    }

    // Calls Construct, but from inside a new thread. This should NOT be called recursively. OnComplete will be called in the main thread
    void ConstructAsync(TreeNode<BoardState> node, System.Action OnComplete = null)
    {
        new ThreadedTask(() => Construct(node), OnComplete).Start();
    }

    // Simulates all possible moves READ_AHEAD_TURNS ahead of the BoardState in node. Depth is not how many levels deep we should go, but rather the representative depth of node. In most cases if we're calling this externally, depth should be zero
    // Unless called from within another thread, this will be done on the main thread - which might freeze the main thread, depending on the READ_AHEAD_TURNS (4 takes about 200ms, 7 takes about 50,000ms)
    void Construct(TreeNode<BoardState> node, int depth = 0)
	{
        // Don't recurse deeper than READ_AHEAD_TURNS
        if (depth >= READ_AHEAD_TURNS)
			return;

        // Invert the playerId every turn, and start with 'startingPlayerTurn' if it has not been set (i.e. in rootNode)
        byte playerId = node.Data.move.playerId == 0 ? startingPlayerId :
                        NetworkManager.GetOtherPlayer(node.Data.move.playerId);

        // Create all possible moves for this turn
        // Each move is a drop in each column, based from this nodes board, from left to right
		for (int x = 0; x < ConnectFour.BOARD_WIDTH; x++)
		{
            // Child is set to null if one doesn't exist at this index, otherwise the child at this index
            TreeNode<BoardState> child = node.Children.ElementAtOrDefault(x);

            // If this column in the node is occupied/filled in the parent, set the child's Data to null. This signifies that no further children should be created under that child
            if (node.Data.board.IsSpaceOccupied(x, 0))
            {
                if (child == null)
                    node.AddChild(null);
                else
                    child.Data = null;
                
                // Move to the next index
                continue;
            }
            else
            {
                if (child == null || child.Data == null)
                {
                    // Copy the data from the parent
                    var childData = new BoardState(playerId);
                    childData.CloneFrom(node.Data);

                    // Set the data on this child
                    if (child == null) child = node.AddChild(childData);
                    else child.Data = childData;

                    // Do this move. If the move done by this child is the last in the column, the winning move, or a stalemate, do not simulate more moves under this child
                    if (!childData.DoMove(x))
                    {
                        // Simulate all children for this child
                        Construct(child, depth + 1);
                    }
                }
                else
                {
                    // Recalculate heuristic score if we've assigned a new playerId
                    if (child.Data.move.playerId != playerId)
                    {
                        child.Data.move.playerId = playerId;
                        child.Data.UpdateScore();
                    }

                    Construct(child, depth + 1);
                }
            }
		}
	}

    int MiniMax(TreeNode<BoardState> node, bool maximisingPlayer)
    {
        int bestValue;

        if (node.IsLeaf)
        {
            if (node.Data == null)
                return 0;
            else
                return node.Data.score;
        }

        if (maximisingPlayer)
        {
            bestValue = int.MinValue;

            // Recurse for all children of node.
            for (int i = 0; i < node.Children.Count; i++)
            {
                var childValue = MiniMax(node.Children[i], false);
                bestValue = Mathf.Max(bestValue, childValue);
            }
        }
        else
        {
            bestValue = int.MaxValue;

            // Recurse for all children of node.
            for (int i = 0; i < node.Children.Count; i++)
            {
                var childValue = MiniMax(node.Children[i], true);
                bestValue = Mathf.Min(bestValue, childValue);
            }
        }

        return bestValue;
    }

    int Negamax(TreeNode<BoardState> node, int color)
    {
        if (node.IsLeaf)
        {
            if (node.Data == null)
                return 0;
            else
                return color * node.Data.score;
        }

        int bestValue = int.MinValue;

        for (int i = 0; i < node.Children.Count; i++)
        {
            var childValue = -Negamax(node.Children[i], -color);
            bestValue = Mathf.Max(bestValue, childValue);
        }

        return bestValue;
    }

    int Negamax(TreeNode<BoardState> node, out TreeNode<BoardState> childNode)
    {
        if (node.IsLeaf)
        {
            childNode = node;

            if (node.Data == null)
                return 0;
            else
                return node.Data.score;
        }

        childNode = null;
        int bestValue = int.MinValue;

        for (int i = 0; i < node.Children.Count; i++)
        {
            var childValue = -Negamax(node.Children[i], out childNode);
            bestValue = Mathf.Max(bestValue, childValue);
        }

        return bestValue;
    }

    /*
    // ! No longer needed, since we have unique heuristic values that need to be recalculated depending on if the player is a computer or player - not recycles.

    // Converts a ConnectFour player ID to one that is relevant for the current node tree. Will invert the playerId (1->2, 2->1) if the startingPlayerId is 2, and will keep it the same if the startingPlayerId is 1. This is used so that we can recycle the built node tree without inverting all the playerId's already in it
    public static byte FixPlayerId(byte playerId)
    {
        if (Instance.startingPlayerId == default(byte))
        {
            Debug.LogError("ConnectFourNPC : FixPlayerId - startingPlayerId not set!");
            throw new System.NullReferenceException();
        }

        if (Instance.startingPlayerId == 2)
            return NetworkManager.GetOtherPlayer(playerId);
        else
            return playerId;
    }

    // Returns true if the player with the playerId is the maximising player for the current node tree. This will return false if the playerId == startingPlayerId
    // Do NOT pass a "fixed" playerId from FixPlayerId() to IsMaximisingPlayer
    public static bool IsMaximisingPlayer(byte playerId)
    {
        if (Instance.startingPlayerId == default(byte))
        {
            Debug.LogError("ConnectFourNPC : FixPlayerId - startingPlayerId not set!");
            throw new System.NullReferenceException();
        }

        return (playerId != Instance.startingPlayerId);
    }
    */
}
