using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using DG.Tweening;

// Handles things to do with the visual representation of the Connect Four board, and its players. This class is run on the client only, and uses NetworkMessages instead of NetworkBehaviour callbacks.
public class ConnectFourBoard : MonoBehaviour
{
    [Header("Transforms")]
    public Transform offsetRoot;

    public Transform localSpawner;
    public Transform localSpawnPosition;
    public Transform remoteSpawner;
    public Transform remoteSpawnPosition;

    [Header("Visuals")]
    public Color playerOneColour;
    public Color playerTwoColour;
    public Material baseBallMaterial;
    public Mesh baseBallMesh;
    public TMPro.TextMeshProUGUI winLoseText;

    [Header("Misc")]

    // The overall scale of the board
    public float boardScale = 0.05f;

    // Axes to invert the otherBallActual about
    public Vector3 inversionFactor;

    // Y-Z size of the column hover detection area
    public Vector2 hoverSize;
    
    // Overarching collider which contains all the hoverBounds
    public BoxCollider hoverCollider;

    // Array of lists of the line renderers used when highlighting a column
    public List<LineRenderer>[] columnRenderers;

    public float explosionForce = 1.0f;
    public float explosionRadius = 0.55f;
    public float upwardModifier = 0.1f;
    public float gravityScale = -9.81f;

    // Bit mask for if a specific column is occupied. The index of the bit is the column index in client coordinates
    [System.NonSerialized] public int occupiedColumnsFlags;

    // Reference to ConnectFourBoardSetup
    ConnectFourBoardSetup boardSetup;

    // This is a reference to our and the other players board offset
    Transform board;
    Transform otherBoard;

    // This is a reference to the synchronised NetworkTransform of our ball
    GameObject ball;
    Renderer ballRenderer;
    
    // This is a reference to the local-only Transform/GameObject of the other ball, this is what we see, and is an inversion of the otherBallRemote. 
    GameObject otherBall;
    Renderer otherBallRenderer;
    
    // This is a reference to the synchronized NetworkTransform of the other players ball. While the visuals for this are hidden, it is used as a reference point for the otherBall
    GameObject otherBallActual;

    // Collection of ball visuals which will exist in the board itself. Every time is it our turn, we get another ball from this pool. This is NOT the ball that the player moves, this ball appears when the player hovers their player ball above a column - and will be dropped into the column and exist there for the rest of the game.
    GameObject[] ballPool, otherBallPool;

    int ballPoolIndex, otherBallPoolIndex;
    Material ballPoolMaterial, otherBallPoolMaterial;
    float initBallAlpha, initBallBrightness;

    // The local-space bounds of the hoverCollider. This is used instead of hoverCollider.bounds, as that bounds is in world space
    Bounds hoverColliderBounds;

    // This array of bounds is used as an alternative to colliders to detect when the player is hovering a ball above a column. This is done for performance, as well as to only register the hover if the center of the ball is within the bounds (a majority). These bounds are LOCAL, so they are not accurate to the scale of the board and have to be translated into a world-space coordnate with Transform.TransformPoint
    Bounds[] hoverBounds = new Bounds[ConnectFour.BOARD_WIDTH];

    // Multidimentional array used to cache LOCAL positions of the board given a specific index
    Vector3[,] positions = new Vector3[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];

    // Multidimentional array used to store references to the visual balls (from the ballPool) that have been placed in specific coordinates for BOTH players
    GameObject[,] placedVisuals;

    IEnumerable<GameObject> placedVisualsFlat
    {
        get
        {
            for (int row = 0; row < placedVisuals.GetLength(0); row++)
            {
                for (int col = 0; col < placedVisuals.GetLength(1); col++)
                {
                    yield return placedVisuals[row,col];
                }
            }
        }
    }

    bool updateOtherBall = true;

    bool completedWinVisuals;
    public bool completedEndVisuals;
    
    int endVisualsIndex = 0;

    // Properties

    public Color PlayerColour
    {
        get { return NetworkManager.PlayerID == 1 ? playerOneColour : playerTwoColour; }
    }

    public Color OtherPlayerColour
    {
        get { return NetworkManager.OtherPlayerID == 1 ? playerOneColour : playerTwoColour; }
    }

    public static ConnectFourBoard Instance { get; private set; }

    // Consts

    const float DROP_BALL_VISUAL_DURATION = 1.0f;

    public const string SHADER_MAIN_COLOR = "_MainColor";
    public const string SHADER_ALPHA = "_Alpha";
    public const string SHADER_BRIGHTNESS = "_Brightness";
    public const string SHADER_RIM_COLOR = "_RimColor";

    public const string SHADER_SCAN_ON_KWD = "_SCAN_ON";
    public const string SHADER_SCAN_TILING = "_ScanTiling";

    // --- UNITY CALLBACKS ---

    void Awake()
    {
        Instance = this;
        boardSetup = FindObjectOfType<ConnectFourBoardSetup>();
    }

    // At start, we do all the things that only need to be done once
    void Start()
    {
        board = transform;

        // Disable board visuals
        offsetRoot.gameObject.SetActive(false);
        localSpawner.gameObject.SetActive(false);
        remoteSpawner.gameObject.SetActive(false);
        winLoseText.gameObject.SetActive(false);

        transform.localScale = Vector3.one * boardScale;

        // Create the local hoverColliderBounds
        hoverColliderBounds = new Bounds(hoverCollider.transform.localPosition, hoverCollider.size);

        // The topLeft-most world position, unscaled
        Vector3 topLeft = (Vector3.up * (ConnectFour.BOARD_HEIGHT / 2f - 0.5f)) +
                          (Vector3.left * (ConnectFour.BOARD_WIDTH / 2f - 0.5f)) +
                          offsetRoot.localPosition;
        
        // Size of the hoverBounds box, unscaled
        Vector3 boxSize = new Vector3(1.0f, hoverSize.x, hoverSize.y);

        // Generate hoverBounds array
        for (int i = 0; i < hoverBounds.Length; i++)
        {
            Vector3 center = (topLeft + Vector3.up + (Vector3.right * i));
            hoverBounds[i] = new Bounds(center, boxSize);
        }

        // Generate board positions (left-right, top-bottom)
        for (int x = 0; x < ConnectFour.BOARD_WIDTH; x++)
        {
            Vector3 start = (topLeft + (Vector3.right * x));

            for (int y = 0; y < ConnectFour.BOARD_HEIGHT; y++)
                positions[x, y] = (start + Vector3.down * y);
        }

        initBallAlpha = baseBallMaterial.GetFloat(SHADER_ALPHA);
        initBallBrightness = baseBallMaterial.GetFloat(SHADER_BRIGHTNESS);

        if (NetworkManager.IsClient)
        {
            // Custom message handlers
            NetworkManager.RegisterClientHandler(ConnectFourMsgType.AssignPlayerNumber, OnAssignPlayerNumber);
            NetworkManager.RegisterClientHandler(ConnectFourMsgType.BoardSize, OnBoardSizeMessage);
            NetworkManager.RegisterClientHandler(ConnectFourMsgType.BoardOffset, OnBoardOffsetMessage);
            NetworkManager.RegisterClientHandler(ConnectFourMsgType.StartGame, OnStartGame);
            NetworkManager.RegisterClientHandler(ConnectFourMsgType.PlayerStartTurn, OnPlayerStartTurn);
        }
    }

    void Update()
    {
        // Update the position of otherBall, to an inverted otherBallActual position.
        if (otherBallActual != null && otherBall != null && updateOtherBall)
        {
#if META_CENTERED_PLAYER
            // Convert the worlds-space position of the networked ball to one that is local to the other players board 
            Vector3 pos = otherBoard.InverseTransformPoint(otherBallActual.transform.position);

            // Invert the local position using the inversion factor (Vector3.Scale is effectively Vector3 *= Vector3
            pos = Vector3.Scale(pos, inversionFactor);

            // Convert that local position to a world position using our board origin 
            otherBall.transform.position = board.TransformPoint(pos);
#else
            // Set the otherBall position to an inversion of the otherBallActual position
            otherBall.transform.position =
                new Vector3(otherBallActual.transform.position.x * inversionFactor.x,
                            otherBallActual.transform.position.y * inversionFactor.y, 
                            otherBallActual.transform.position.z * inversionFactor.z);
#endif
        }

#if UNITY_EDITOR
        // ! Fills the board up with balls
        if (Input.GetKeyDown(KeyCode.F))
        {
            ResetBallPool(1);
            ResetBallPool(2);

            ballPoolIndex = 0;
            otherBallPoolIndex = 0;

            for (int x = 0; x < ConnectFour.BOARD_WIDTH; x++)
            {
                for (int y = 0; y < ConnectFour.BOARD_HEIGHT; y++)
                {
                    byte playerId = (byte)(Random.value > 0.5f ? 1 : 2);

                    // Don't use this player if they're out of balls
                    if ((NetworkManager.IsMe(playerId) && ballPoolIndex >= ballPool.Length) ||
                        (!NetworkManager.IsMe(playerId) && otherBallPoolIndex >= otherBallPool.Length))
                        playerId = NetworkManager.GetOtherPlayer(playerId);

                    ConnectFourBoard.Instance.DropBallVisual(playerId, x, y);
                }
            }
        }

        // ! Triggers the end-game visuals
        if (Input.GetKeyDown(KeyCode.Equals))
        {
            endVisualsIndex++;
            Debug.Log(endVisualsIndex);
        }
        if (Input.GetKeyDown(KeyCode.Minus))
        {
            endVisualsIndex--;
            Debug.Log(endVisualsIndex);
        }
        if (Input.GetKeyDown(KeyCode.Return))
            EndGameVisuals(endVisualsIndex);
#endif
    }

    // --- CLIENT CALLBACKS ---

    // OnAssignPlayerNumber is the earliest time that a script can get the player number, as it only becomes available when the client connects to the server.
    void OnAssignPlayerNumber(NetworkMessage msg)
    {
        byte playerId = msg.ReadMessage<ByteMessage>().value;

        // Half the number of spaces in the board, the max amount of balls each player can place before running out of space
        int numSpaces = (int)(ConnectFour.BOARD_WIDTH * ConnectFour.BOARD_HEIGHT / 2.0f);

        if (ballPool == null || otherBallPool == null)
        {
            ballPool = new GameObject[numSpaces];
            otherBallPool = new GameObject[numSpaces];

            // Create the ball pool for each player
            CreateBallPool(ballPool, ref ballPoolMaterial, playerId);
            CreateBallPool(otherBallPool, ref otherBallPoolMaterial, NetworkManager.GetOtherPlayer(playerId));
        }

        // Set the color of the spawning pad
        localSpawner.GetComponentInChildren<SpriteRenderer>().color = PlayerColour;
        remoteSpawner.GetComponentInChildren<SpriteRenderer>().color = OtherPlayerColour;
    }

    // Called by ConnectFourBoardSetup.OnServerBoardSizeMessage (a ConnectFourMsgType.BoardSize) when the other player sets their board size
    void OnBoardSizeMessage(NetworkMessage msg)
    {
        Vector2 size = msg.ReadMessage<BoardSizeMessage>().size;

        // Instead of basing the size of the board on the setup size (as originally intended), only base the spawner offset on the size
        Vector3 spawnerOffset = (Vector3.back * (size.y / 2.0f)) * (1.0f / boardScale);

        localSpawner.localPosition = spawnerOffset;
        remoteSpawner.localPosition = -localSpawner.localPosition;

        // Create a new plane for the base of the board
        var colliderObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
        colliderObj.name = "Collider";
        colliderObj.transform.parent = transform;
        colliderObj.transform.localPosition = Vector3.zero;
        colliderObj.transform.localRotation = Quaternion.identity;
        colliderObj.transform.localScale = new Vector3(size.x * 2.0f, 1.0f, size.y * 2.0f);
        DestroyImmediate(colliderObj.GetComponent<MeshRenderer>());
        DestroyImmediate(colliderObj.GetComponent<MeshFilter>());

        // ! Do other stuff to do with the board size here
    }

    // Called by ConnectFourBoardSetup.OnServerBoardOffsetMessage (a ConnectFourMsgType.BoardOffset) when the other player sets their board offset
    void OnBoardOffsetMessage(NetworkMessage msg)
    {
        var boardOffsetMsg = msg.ReadMessage<BoardOffsetMessage>();

        // Set the board offset for this player
        SetBoardOffset(boardOffsetMsg.playerId, boardOffsetMsg.offset, boardOffsetMsg.rotation);

        Debug.Log("[CLIENT] ConnectFourBoard : OnBoardOffsetMessage - Player " + boardOffsetMsg.playerId + " board offset is " + boardOffsetMsg.offset + ", at rotation " + boardOffsetMsg.rotation.eulerAngles);
    }

    // Callback for when the game starts. This gets called after every win, and before the first game
    // We use OnStartGame to clear/reset variables that would have changed during gameplay
    void OnStartGame(NetworkMessage msg)
    {
        Debug.Log("[CLIENT] ConnectFourBoard : OnStartGame - Resetting board...");

        // Reset occupiedColumns bitmask
        occupiedColumnsFlags = 0;

        // Reset ballPool indexes
        ballPoolIndex = otherBallPoolIndex = 0;

        // Reset ball pool objects to default
        ResetBallPool(NetworkManager.PlayerID);
        ResetBallPool(NetworkManager.OtherPlayerID);

        // (Re)initialize placedVisuals array
        placedVisuals = new GameObject[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];

        // Move both player's main ball back to their spawner
        MoveBallToSpawner(NetworkManager.PlayerID);
        MoveBallToSpawner(NetworkManager.OtherPlayerID);

        // Hide both player's main ball
        HideBall(NetworkManager.PlayerID);
        HideBall(NetworkManager.OtherPlayerID);
    }

    // Callback for when it is now a specific players turn, so that we can do visuals relating to that player.
    void OnPlayerStartTurn(NetworkMessage msg)
    {
        var byteMsg = msg.ReadMessage<ByteMessage>();
        byte playerId = byteMsg.value;

        Debug.Log("[CLIENT] ConnectFourBoard : OnPlayerStartTurn - Player " + byteMsg.value + "'s turn");

        // Show the players ball, with visuals
        ShowBall(byteMsg.value, true);
    }

    // --- PRIVATE METHODS ---

    void CreateBallPool(GameObject[] pool, ref Material material, byte playerId)
    {
        string name = "Player_" + playerId + "_BallPool";

        var parent = new GameObject(name).transform;
        parent.parent = transform;
        parent.localPosition = Vector3.zero;
        parent.localRotation = Quaternion.identity;
        parent.localScale = Vector3.one;

        // Make another instance of the base ball material - for use with this pool
        material = Instantiate(baseBallMaterial);
        material.name = name;
        material.SetColor(SHADER_MAIN_COLOR, GetColorForPlayer(playerId));
        material.SetColor(SHADER_RIM_COLOR, GetColorForPlayer(playerId));

        // Create each ball using the baseBallMesh and created material
        for (int i = 0; i < pool.Length; i++)
        {
            var obj = new GameObject("P" + playerId + "_Ball_" + i);

            obj.transform.parent = parent;
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            // Add rendering components
            var meshFilter = obj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = baseBallMesh;
            var meshRenderer = obj.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;

            // Add rigidbody
            //var rigidbody = obj.AddComponent<Rigidbody>();
            //rigidbody.isKinematic = true;
            //rigidbody.useGravity = false;

            // Save the ball to the array
            pool[i] = obj;

            // Disable the ball
            obj.SetActive(false);
        }
    }

    // Reset all of the ball pool objects to their default state based on things that might have been changed during gameplay. This will always be faster than instantiating whole new objects
    void ResetBallPool(byte playerId)
    {
        var pool = NetworkManager.IsMe(playerId) ? ballPool : otherBallPool;
        Material mat = NetworkManager.IsMe(playerId) ? ballPoolMaterial : otherBallPoolMaterial;

        for (int i = 0; i < ballPool.Length; i++)
        {
            pool[i].SetActive(false);

            // Zero the position
            pool[i].transform.position = Vector3.zero;

            // Zero the rotation
            pool[i].transform.rotation = Quaternion.identity;

            // Reset the scale
            pool[i].transform.localScale = Vector3.one;

            // Delete components that may have been added at runtime
            if (pool[i].GetComponent<Rigidbody>())
                DestroyImmediate(pool[i].GetComponent<Rigidbody>());
            if (pool[i].GetComponent<SphereCollider>())
                DestroyImmediate(pool[i].GetComponent<SphereCollider>());

            // Remove the instanced material, and revert to the sharedMaterial
            var r = pool[i].GetComponent<Renderer>();
            Destroy(r.material);
            r.sharedMaterial = mat;
        }

        // !
        Physics.gravity = Vector3.up * -9.81f;
    }

    // --- PUBLIC METHODS ---

    // Used to initialize our ball. This is called by ConnectFourPlayer when it spawns
    public void SetOurBall(GameObject obj)
    {
        ball = obj;
        ballRenderer = obj.GetComponentInChildren<Renderer>(true);

        // Make an instance of the base ballMaterial for the players main ball
        ballRenderer.material = Instantiate(baseBallMaterial);
        ballRenderer.material.name = "Player_" + NetworkManager.PlayerID + "_MainBall";
        ballRenderer.material.SetColor(SHADER_MAIN_COLOR, PlayerColour);
        ballRenderer.material.SetColor(SHADER_RIM_COLOR, PlayerColour);
        ballRenderer.material.SetFloat(SHADER_ALPHA, 0.0f);

        // Disable the ball renderer
        ballRenderer.gameObject.SetActive(false);
    }

    // Used to initialize the other players ball.
    public void SetOtherBall(GameObject obj)
    {
        otherBallActual = obj;

        // Disable collision and rendering on otherBallActual
        otherBallActual.GetComponent<Collider>().enabled = false;
        otherBallActual.GetComponentInChildren<Renderer>().enabled = false;

        // Create the local otherBall (which is what we see), a positionally-inverted representation of the otherBallActual
        // Don't create one if it already exists (occurs if we reconnected)
        if (otherBall == null)
        {
            otherBall = new GameObject(otherBallActual.name + "_Local");
            otherBall.transform.position = remoteSpawnPosition.position;
            otherBall.transform.rotation = remoteSpawnPosition.rotation;
            otherBall.transform.localScale = otherBallActual.transform.localScale;

            var otherBallVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            otherBallVisual.name = "Visuals";
            otherBallVisual.transform.parent = otherBall.transform;
            otherBallVisual.transform.localPosition = Vector3.zero;
            otherBallVisual.transform.localRotation = Quaternion.identity;
            otherBallVisual.transform.localScale = Vector3.one;

            // Make an instance of the base ballMaterial for the other players main ball
            otherBallRenderer = otherBall.GetComponentInChildren<Renderer>();
            otherBallRenderer.material = Instantiate(baseBallMaterial);
            otherBallRenderer.material.name = "Player_" + NetworkManager.OtherPlayerID + "_MainBall";
            otherBallRenderer.material.SetColor(SHADER_MAIN_COLOR, OtherPlayerColour);
            otherBallRenderer.material.SetColor(SHADER_RIM_COLOR, OtherPlayerColour);
            otherBallRenderer.material.SetFloat(SHADER_ALPHA, 0.0f);
        }

        // Disable the renderer
        otherBallRenderer.gameObject.SetActive(false);
    }

    public void ShowBoard()
    {
        // Enable the board visuals
        offsetRoot.gameObject.SetActive(true);
        localSpawner.gameObject.SetActive(true);
        remoteSpawner.gameObject.SetActive(true);
    }

    // Fades the ball owned by playerId
    // This is used for showing the ball for a specific player when it is their turn
    public void ShowBall(byte playerId, bool doVisuals = false)
    {
        var ball = GetBallForPlayer(playerId).transform;
        Renderer r = GetBallRendererForPlayer(playerId);

        // Enable the renderer
        r.gameObject.SetActive(true);

        if (doVisuals)
        {
            // Don't update the other players ball over the network while animating it
            updateOtherBall = false;

            r.material.SetFloat(SHADER_ALPHA, 1.0f);
            r.material.SetFloat(SHADER_BRIGHTNESS, 1.0f);
            r.material.SetColor(SHADER_MAIN_COLOR, Color.white);

            // Fade the alpha, brightness and color to the normal value after .5s, over 1.5s
            r.material.DOFloat(initBallAlpha, SHADER_ALPHA, 1.5f).SetDelay(0.5f);
            r.material.DOFloat(initBallBrightness, SHADER_BRIGHTNESS, 1.5f).SetDelay(0.5f);
            r.material.DOColor(GetColorForPlayer(playerId), SHADER_MAIN_COLOR, 1.5f).SetDelay(0.5f);
            
            /*
            r.material.SetFloat(SHADER_ALPHA, 0.0f);
            r.material.SetFloat(SHADER_BRIGHTNESS, 6.0f);

            // Fade the "Alpha" property from 0 to its original value
            r.material.DOFloat(initBallAlpha, SHADER_ALPHA, 1.0f);

            // Fade the "Brightness" property from 6 to its original value
            r.material.DOFloat(initBallBrightness, SHADER_BRIGHTNESS, 1.0f);
            */

            // Scale/move the transform in
            ball.localScale = Vector3.zero;
            ball.DOScale(boardScale, 1f);

            ball.position = GetSpawnerPositionOfPlayer(playerId);
            ball.DOMove(GetSpawnPositionOfPlayer(playerId), 1f)
                .SetDelay(0.01f).SetEase(Ease.OutBack).OnComplete(() =>
                    updateOtherBall = true);
        }
        else
        {
            r.material.SetFloat(SHADER_ALPHA, initBallAlpha);
            r.material.SetFloat(SHADER_BRIGHTNESS, initBallBrightness);
        }
    }

    // Hides the ball owned by playerId immediately
    // This is used for when the player hovers the ball over an empty spot on the board
    public void HideBall(byte playerId)
    {
        Renderer r = GetBallRendererForPlayer(playerId);

        // Don't hide if already hidden
        if (r.gameObject.activeInHierarchy == false)
            return;
        
        r.material.SetFloat(SHADER_ALPHA, 0.0f);
        r.material.SetFloat(SHADER_BRIGHTNESS, 6.0f);
        r.gameObject.SetActive(false);
    }

    // Increases the brightness by a bit, in response to the player hovering their hand over it
    public void SelectBall(byte playerId, bool value)
    {
        Renderer r = GetBallRendererForPlayer(playerId);

        float brightness = initBallBrightness + (value ? 1.0f : 0.0f);
        r.material.DOFloat(brightness, SHADER_BRIGHTNESS, 0.25f);
    }

    // This will show a visual ball in a specific column (client side) for the player of playerId
    public void ShowVisualBallAboveColumn(byte playerId, int column)
    {
        // Get a reference to the current visualBall
        var visualBall = GetCurrentVisualBall(playerId);
        var r = visualBall.GetComponent<Renderer>();

        // Show the visual ball
        visualBall.SetActive(true);

        // Hide the main ball
        HideBall(playerId);

        // Move the position of the visualBall to the column hover position
        visualBall.transform.position = GetHoverPositionOfColumn(column);
    }

    public void HideVisualBall(byte playerId)
    {
        // Get a reference to the current visualBall
        var visualBall = GetCurrentVisualBall(playerId);

        // Hide the visual ball
        visualBall.SetActive(false);

        // Show the main ball
        ShowBall(playerId);

        // Move the position of the visualBall to zero
        visualBall.transform.position = Vector3.zero;
    }

    // Moves the ball owned by playerId to the spawner immediately
    // This is used both when starting and ending our turn
    public void MoveBallToSpawner(byte playerId)
    {
        var ball = GetBallForPlayer(playerId);
        ball.transform.position = GetSpawnPositionOfPlayer(playerId);
    }

    // Animates a visualBall to a position in the board
    public void DropBallVisual(byte playerId, int x, int y)
    {
        var visualBall = GetCurrentVisualBall(playerId);
        var r = visualBall.GetComponent<Renderer>();

        r.sharedMaterial = NetworkManager.IsMe(playerId) ? ballPoolMaterial : otherBallPoolMaterial;

        // Save this visualBall
        placedVisuals[x, y] = visualBall;

        // Advance the ballPool index for this player
        NextVisualBall(playerId);

        // Enable this visualBall (although it should already be)
        if (!visualBall.activeInHierarchy)
            visualBall.SetActive(true);
        
        // Make sure the ball is in the starting position first
        visualBall.transform.position = GetHoverPositionOfColumn(x);
        
        // Bounce-tween the visualBall to its target
        Vector3 target = GetPositionOfCoord(x, y);
        visualBall.transform.DOMove(target, DROP_BALL_VISUAL_DURATION).SetEase(Ease.OutBounce);
    }

    // Triggers the winning visuals for the player
    public void WinVisuals(byte playerId, Vector2Int start, Vector2Int end, string seqType)
    {
        var delta = new Vector2Int(end.x > start.x ? 1 : end.x < start.x ? -1 : 0,
                                   end.y > start.y ? 1 : end.y < start.y ? -1 : 0);

        StartCoroutine(WinVisualsRoutine(playerId, start, delta));
    }

    // Triggers stalemate visuals. This just sets playerId to zero, and omits start and offset variables
    public void StalemateVisuals()
    {
        StartCoroutine(WinVisualsRoutine(0, Vector2Int.zero, Vector2Int.zero));
    }

    IEnumerator WinVisualsRoutine(byte playerId, Vector2Int start, Vector2Int delta)
    {
        // Wait for the ball drop visual to complete before continuing
        yield return new WaitForSeconds(1.0f);

        // Show the "You Win", "You Lose" or "Stalemate" text
        StartCoroutine(WinVisualsTextRoutine(playerId));

        // Highlight winning sequence
        if (ConnectFour.IsPlayerValid(playerId))
        {
            // Three times
            for (int count = 0; count < 3; count++)
            {
                // For each ball in the winning sequence
                for (int i = 0; i < ConnectFour.SEQ_COUNT; i++)
                {
                    // Get the placed ball at an offset from the start of the winning sequence
                    var obj = placedVisuals[start.x + (delta.x * i), start.y + (delta.y * i)];
                    var r = obj.GetComponent<Renderer>();

                    // Wait 0.5 seconds between each item
                    yield return new WaitForSeconds(0.5f);

                    StartCoroutine(WinVisualsItemRoutine(playerId, r));
                }
            }
        }

        // Wait 1 second for padding
        yield return new WaitForSeconds(1.0f);

        completedWinVisuals = true;
    }

    IEnumerator WinVisualsItemRoutine(byte playerId, Renderer r)
    {
        const float rampDuration = 0.5f;
        const float slideDuration = 1.5f;

        // Ramp alpha to 1 and color to white
        r.material.DOFloat(1.0f, SHADER_ALPHA, rampDuration).SetEase(Ease.OutBack);
        r.material.DOColor(Color.white, SHADER_MAIN_COLOR, rampDuration).SetEase(Ease.OutBack);

        // Wait for ramp to complete
        yield return new WaitForSeconds(rampDuration);

        // Slide the alpha to the initial, and color to the normal
        r.material.DOFloat(initBallAlpha, SHADER_ALPHA, slideDuration);
        r.material.DOColor(GetColorForPlayer(playerId), SHADER_MAIN_COLOR, slideDuration);
    }

    IEnumerator WinVisualsTextRoutine(byte playerId)
    {
        var canvasGroup = winLoseText.GetComponent<CanvasGroup>();
        
        // Enable the text
        winLoseText.gameObject.SetActive(true);

        // Start fade in
        canvasGroup.DOFade(1.0f, 1.0f);

        // If is is us who won
        if (NetworkManager.IsMe(playerId))
        {
            winLoseText.text = "YOU WIN";

            float timer = 0.0f;
            float hue = 0.0f;

            // Randomize the glow color for 5 seconds
            while (timer < 5.0f)
            {
                timer += Time.deltaTime;
                hue = Mathf.Repeat(hue += Time.deltaTime, 1.0f);

                winLoseText.materialForRendering.SetColor("_FaceColor", Color.HSVToRGB(hue, 1.0f, 1.0f));
                yield return null;
            }
        }

        // If the other player won (we lost)
        else if (NetworkManager.IsOther(playerId))
        {
            winLoseText.text = "YOU LOSE";
            winLoseText.materialForRendering.SetColor("_FaceColor", Color.red);
            yield return new WaitForSeconds(5.0f);
        }

        // If this coroutine was sent a playerId of zero, it was a stalemate
        else
        {
            winLoseText.text = "STALEMATE!";
            winLoseText.materialForRendering.SetColor("_FaceColor", Color.red);
            yield return new WaitForSeconds(5.0f);
        }

        // Fade out and yield
        yield return canvasGroup.DOFade(0.0f, 1.0f).WaitForCompletion();

        // Disable the text
        winLoseText.gameObject.SetActive(false);
    }

    // Waits for win visuals to complete, then does End Game visuals
    public void EndGameVisuals(int randomSeed)
    {
        // Invoke the EndGameVisuals when the win visuals have completed
        CoroutineHelper.InvokeCondition(() => completedWinVisuals == true, () =>
        {
            EndGameVisualsInternal(randomSeed);

            // Reset completedWinVisuals
            completedWinVisuals = false;
        });
    }

    // Triggers end game visuals
    void EndGameVisualsInternal(int? randomSeed = null)
    {
        Random.State oldState = Random.state;
        System.Guid seededGuid = System.Guid.NewGuid();

        // If we're seeding this EndGameVisuals, it means it will be exactly the same across both players. We need to make sure to reset the old state however
        if (randomSeed != null)
        {
            Random.InitState((int)randomSeed);
            oldState = Random.state;

            // Generate a seeded GUID, which will be the same if we use the same seed across players
            var guidBytes = new byte[16];
            new System.Random((int)randomSeed).NextBytes(guidBytes);
            seededGuid = new System.Guid(guidBytes);
        }

        int visualType = Random.Range(1, 8);

        // Time before ending the visuals routine
        float visualsDuration = 0.0f;
        
        // Counters, used for delaying visuals
        int counter = 0;
        int reverseCounter = placedVisualsFlat.Count(x => x != null);
        int count = reverseCounter;

        // Some fixed random values
        float fixedFloat = Random.value; // <- Always positive
        Vector3 fixedVector = Random.insideUnitSphere; // <- XYZ can be negative or positive

        // The center of the board
        Vector3 center = (Vector3.up * (ConnectFour.BOARD_HEIGHT / 2.0f)) * boardScale;
        center += Vector3.forward * (fixedFloat > 0.33f ? fixedFloat > 0.66f ? 0.5f : -0.5f : 0.0f) ;

        // Set the gravity, and save the old one
        Vector3 gravityPrev = Physics.gravity;
        Physics.gravity = Vector3.up * gravityScale;

        // Determines whether to shuffle the balls or not
        bool shuffleBalls = Random.value > 0.5f;
        
        Debug.LogFormat("ConnectFourBoard : EndGameVisuals - visualType = {0}, randomSeed = {1}, shuffleBalls = {2}, fixedFloat = {3}, fixedVector = {4}, seededGuid = {5}", visualType, randomSeed, shuffleBalls, fixedFloat, fixedVector, seededGuid);

        var balls = shuffleBalls ? placedVisualsFlat.OrderBy(x => seededGuid) : placedVisualsFlat;

        foreach (var obj in balls)
        {
            if (obj == null) continue;
            
            switch (visualType)
            {
                // Shoot up, with gravity, with collision
                case 1:
                {
                    var rb = obj.AddComponent<Rigidbody>();
                    var coll = obj.AddComponent<SphereCollider>();
                    rb.isKinematic = false;
                    rb.useGravity = true;

                    // Add a very small amount of downward force
                    rb.AddForce(Vector3.up * (explosionForce + obj.transform.position.y), ForceMode.Impulse);
                    visualsDuration = 5.0f;
                    break;
                }

                // Fall with gravity, with collision, all at once
                case 2:
                {
                    var rb = obj.AddComponent<Rigidbody>();
                    var coll = obj.AddComponent<SphereCollider>();
                    rb.isKinematic = false;
                    rb.useGravity = true;

                    // 0 - Adding a random-strength, random-direction force on each
                    if (fixedFloat < 0.25f)
                        rb.AddForce(Random.insideUnitSphere * Random.value, ForceMode.Impulse);
                    // 0.25 - Adding a fixed-strength, fixed direction force on each
                    else if (fixedFloat < 0.5f)
                        rb.AddForce(fixedVector * fixedFloat, ForceMode.Impulse);
                    // 0.5 - Adding a fixed-strength, random-direction force on each
                    else if (fixedFloat < 0.75f)
                        rb.AddForce(Random.insideUnitSphere * fixedFloat * 2.0f, ForceMode.Impulse);
                    // 0.75 - Adding a random-strength, fixed-direction force on each
                    else if (fixedFloat <= 1.00f)
                        rb.AddForce(fixedVector * Random.value, ForceMode.Impulse);

                    visualsDuration = 5.0f;

                    break;
                }

                // Fall with gravity, without collision, one by one. This starts in the top left and goes from top to bottom, left to right
                case 3:
                {
                    var rb = obj.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;

                    // Reverse the effect half the time
                    int index = fixedFloat > 0.5f ? counter : reverseCounter;
                    float delay = (0.15f * fixedFloat);

                    CoroutineHelper.InvokeDelayed(index * delay, () =>
                    {
                        rb.isKinematic = false;
                        rb.useGravity = true;
                    });

                    visualsDuration += delay + (0.5f / count);

                    break;
                }

                // Tween the scale of the object using Ease.InBack, all at once
                case 4:
                {
                    obj.transform.DOScale(0.0f, 0.5f).SetEase(Ease.InBack);
                    visualsDuration = 1.0f + (0.5f / count);
                    break;
                }

                // Tween the scale of the object using Ease.InBack, one by one
                case 5:
                {
                    // Reverse the effect half the time
                    int index = fixedFloat > 0.5f ? counter : reverseCounter;
                    float delay = (0.15f * fixedFloat);

                    obj.transform.DOScale(0.0f, 0.5f).SetEase(Ease.InBack).SetDelay(index * delay);
                    
                    // Add the delay to the visualsDuration
                    visualsDuration += delay + (0.5f / count);
                    break;
                }

                // Fade alpha out, one by one
                case 6:
                {
                    var r = obj.GetComponent<Renderer>();

                    // Reverse the effect half the time
                    int index = fixedFloat > 0.5f ? counter : reverseCounter;
                    float delay = (0.15f * fixedFloat);

                    r.material.DOFloat(0.0f, SHADER_ALPHA, 0.25f).SetDelay(index * delay)
                    .OnComplete(() => r.gameObject.SetActive(false));

                    // Add the delay+duration to the visualsDuration
                    visualsDuration += delay + (0.25f / count);

                    break;
                }

                // Ramp brightness then fade brightness+alpha
                case 7:
                {
                    // Reverse the effect half the time
                    int index = fixedFloat > 0.5f ? counter : reverseCounter;
                    float delay = (0.15f * fixedFloat);

                    var r = obj.GetComponent<Renderer>();
                    r.material.DOFloat(3.0f, SHADER_BRIGHTNESS, 0.5f).SetDelay(index * delay);
                    r.material.DOFloat(0.0f, SHADER_BRIGHTNESS, 1f).SetDelay((index * delay) + 0.5f);
                    r.material.DOFloat(0.0f, SHADER_ALPHA, 1f).SetDelay((index * delay) + 0.5f)
                    .OnComplete(() => r.gameObject.SetActive(false));

                    visualsDuration += delay + (1.5f / count);
                    break;
                }

                default:
                {
                    Debug.LogError("ConnectFourBoard : EndGameVisuals - Effect for visual type " + visualType + " doesn't exist!");
                    return;
                }
            }

            counter++;
            reverseCounter--;
        }

        // Reset random state
        if (randomSeed != null)
        {
            Random.state = oldState;
        }

        // Reset all the objects that were affected by the end game visuals. This occurs both after the delay before doing the visuals, plus the duration of the visuals themselves
        CoroutineHelper.InvokeDelayed(visualsDuration, () =>
        {
            // Fade out every ball
            foreach (var obj in placedVisuals)
            {
                if (obj == null) continue;

                var r = obj.GetComponent<Renderer>();

                if (r.material != null && r.material.GetFloat(SHADER_ALPHA) > 0.0f)
                {
                    r.material.DOFloat(0.0f, SHADER_ALPHA, 0.5f)
                    .OnComplete(() => r.gameObject.SetActive(false));
                }
            }

            // Set the completedEndVisuals flag to true
            CoroutineHelper.InvokeDelayed(0.5f, () => completedEndVisuals = true);
        });
    }

    // Utility methods

    // Returns an inverted Vector3, if the playerId is the id of the other player
    public Vector3 InvertVector(byte playerId, Vector3 value)
    {
        if (NetworkManager.IsMe(playerId))
            return value;
        else
            return InvertVector(value);
    }

    // Returns a Vector3 inverted by the inversionFactor
    public Vector3 InvertVector(Vector3 value)
    {
        return new Vector3(value.x * inversionFactor.x,
                           value.y * inversionFactor.y,
                           value.z * inversionFactor.z);
    }

    // Checks to see if the position is contained within the hoverColliderBounds
    public bool WithinHoverBounds(Vector3 position, bool isLocalPosition = false)
    {
        if (isLocalPosition)
            return hoverColliderBounds.Contains(position);
        else
            return hoverColliderBounds.Contains(transform.InverseTransformPoint(position));
    }

    // Returns the column index of the column that contains a position, using the hoverBounds
    // If none of the hoverBounds contains the position, returns -1
    public int CheckHoverBounds(Vector3 position)
    {
        // Convert the position to one that is local to the board. This way we can still use axis-aligned bounding boxes to check for collisions, since the local position will be axis aligned
        position = transform.InverseTransformPoint(position);

        // First, check if we're in the overarching area
        if (WithinHoverBounds(position, true))
        {
            for (int i = 0; i < hoverBounds.Length; i++)
            {
                if (hoverBounds[i].Contains(position))
                    return i;
            }
        }

        return -1;
    }

    // Sets the transform position and rotation of either our board, or the representation of the other players board
    public void SetBoardOffset(byte playerId, Vector3 offset, Quaternion rotation)
    {
        if (NetworkManager.IsMe(playerId))
        {
            // Set the position of our board
            board.position = offset;
            board.rotation = rotation;
        }
        else
        {
            // Create a new transform to represent the position/rotation of the other players board, if it doesn't exist
            if (otherBoard == null)
            {
                otherBoard = new GameObject("Player_" + playerId + "_Board").transform;
                otherBoard.parent = this.transform.parent;
                otherBoard.localScale = this.transform.localScale;
            }

            otherBoard.position = offset;
            otherBoard.rotation = rotation;
        }
    }

    // Returns the world position of the coordinate on the board (this is client-side, so 0 is always on the left from both players perspectives)
    public Vector3 GetPositionOfCoord(int x, int y)
    {
        if (ConnectFour.IsCoordInRange(x, y))
            return transform.TransformPoint(positions[x, y]);
        
        else return Vector3.zero;
    }

    // Returns the world position of start of the column (would be at y index -1)
    public Vector3 GetHoverPositionOfColumn(int index)
    {
        if (ConnectFour.IsColumnInRange(index))
            return transform.TransformPoint(hoverBounds[index].center);

        return Vector3.zero;
    }

    public Vector3 GetSpawnPositionOfPlayer(byte playerId)
    {
        if (NetworkManager.IsMe(playerId))
            return localSpawnPosition.position;
        else
            return remoteSpawnPosition.position;
    }

    public Vector3 GetSpawnerPositionOfPlayer(byte playerId)
    {
        if (NetworkManager.IsMe(playerId))
            return localSpawner.position;
        else
            return remoteSpawner.position;
    }

    public GameObject GetBallForPlayer(byte playerId)
    {
        if (NetworkManager.IsMe(playerId))
            return ball;
        else
            return otherBall;
    }

    // Returns the main ball renderer for a playerId
    public Renderer GetBallRendererForPlayer(byte playerId)
    {
        if (NetworkManager.IsMe(playerId))
            return ballRenderer;
        else
            return otherBallRenderer;
    }

    // Get a ball from the pool. If this is a new ball, fade 
    public GameObject GetCurrentVisualBall(byte playerId)
    {
        if (NetworkManager.IsMe(playerId))
        {
            if (ballPoolIndex >= ballPool.Length)
                Debug.LogError("ConnectFourBoard : GetCurrentVisualBall - No more balls in ballPool!");

            return ballPool[ballPoolIndex];
        }
        else
        {
            if (otherBallPoolIndex >= otherBallPool.Length)
                Debug.LogError("ConnectFourBoard : GetCurrentVisualBall - No more balls in otherBallPool!");

            return otherBallPool[otherBallPoolIndex];
        }
    }

    public void NextVisualBall(byte playerId)
    {
        if (NetworkManager.IsMe(playerId))
        {
            ballPoolIndex++;

            if (ballPoolIndex >= ballPool.Length)
                Debug.LogError("ConnectFourBoard : NextVisualBall - No more balls in ballPool!");
        }
        else
        {
            otherBallPoolIndex++;

            if (otherBallPoolIndex >= otherBallPool.Length)
                Debug.LogError("ConnectFourBoard : NextVisualBall - No more balls in otherBallPool!");
        }
    }

    public Color GetColorForPlayer(byte playerId)
    {
        return playerId == 1 ? playerOneColour : playerTwoColour;
    }

    void OnDrawGizmosSelected()
    {
        // Gizmo for hoverBounds
        if (hoverBounds != null && hoverBounds.Length > 0)
        {
            Gizmos.color = Color.green;
            Gizmos.matrix = transform.localToWorldMatrix;

            float scale = 1.0f / boardScale;

            foreach (Bounds b in hoverBounds)
            {
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }
    }
}