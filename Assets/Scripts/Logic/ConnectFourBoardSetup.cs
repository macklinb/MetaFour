using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using DG.Tweening;
using System.Collections.Generic;
using Meta.Interop.Buttons;
using Meta.Buttons;

// On the client, is used to setup the game board offset. This involves prompting the player to move a ball the desired position of the board (in real life), and then adjusting the size of the board using one of three other handles.
// Once this step has completed, we take the offset and apply it to the main camera - while zeroing the board at the same time. This way we can have the board at 0,0,0 - with no rotation - for both users. (note that this doesn't occur if META_CENTERED_PLAYER symbol is present)
// On the server, we send clients a OnBoardSizeMessage when a client sets up their board size, and if a new client connects

// ! Finish correcting fade sequence
public class ConnectFourBoardSetup : MonoBehaviour, IOnMetaButtonEvent
{
    [Header("Handles")]
    public Transform initialOriginHandle;
    public Transform originHandle;
    public Transform heightHandle;
    public Transform widthHandle;
    public Transform boardShape;
    public float handleMoveSpeed = 0.2f;

    [Header("Lines")]
    public LineRenderer xLineRenderer;
    public LineRenderer zLineRenderer;
    public LineRenderer boxLineRenderer;

    [Header("Misc")]

    // MetaCameraRig cannot be moved, unless it is parented. cameraRig should be a reference to the parent of MetaCameraRig
    public Transform cameraRig;

    public CanvasGroup boardSetupCanvas;
    public Transform plane;

    // This is the offset of the board from zero. 
    public Vector3 defaultOffset = new Vector3(0f, -0.2f, 0.5f);

    public GameObject[] translateArrows;
    public GameObject[] rotateArrows;
    public GameObject[] sizeArrows;

    // World position of the center of the board
    Vector3 origin;

    // Rotation of the board, only in the Y axis
    Quaternion rotation;

    // Difference between origin and rotation handles (z direction)
    Vector3 bottom;

    // Difference between origin and width handles (x direction)
    Vector3 right;

    // Set when another client already set the board size
    byte fixedSizeSetByPlayerId;
    bool hasFixedBoardSize;
    Vector2 fixedBoardSize;

    bool doingSetup, doingPlacement;
    bool completedSetup = false;

    // Server-side store for the offset messages sent from player 1 and player 2
    BoardOffsetMessage p1_offset, p2_offset;

    MeshRenderer planeRenderer;
    Material boardShapeMaterial;
    float planeAlpha, boardShapeAlpha;

    MeshRenderer initialHandleRenderer, originHandleRenderer, heightHandleRenderer, widthHandleRenderer;

    SpriteRenderer[] originHandleSprites, heightHandleSprites, widthHandleSprites;
    float[] originHandleSpriteAlphas, heightHandleSpriteAlphas, widthHandleSpriteAlphas;
    
    float xLineAlpha;
    float zLineAlpha;
    float boxLineAlpha;

    Meta.MetaReconstruction metaReconstruction;

    GameObject reconstructionMesh;

    void Awake()
    {
        // Fetch references to renderers
        planeRenderer =
            plane.GetComponent<MeshRenderer>();
        boardShapeMaterial = boardShape.GetComponentInChildren<Renderer>().sharedMaterial;

        initialHandleRenderer = 
            initialOriginHandle.GetComponentInChildren<MeshRenderer>();
        originHandleRenderer =
            originHandle.GetComponentInChildren<MeshRenderer>();
        heightHandleRenderer =
            heightHandle.GetComponentInChildren<MeshRenderer>();
        widthHandleRenderer =
            widthHandle.GetComponentInChildren<MeshRenderer>();

        originHandleSprites = originHandle.GetComponentsInChildren<SpriteRenderer>();
        heightHandleSprites = heightHandle.GetComponentsInChildren<SpriteRenderer>();
        widthHandleSprites = widthHandle.GetComponentsInChildren<SpriteRenderer>();

        // Get all base alphas (this is used to ensure that when we fade an object to it's opaque value, it retains whatever alpha we've set, instead of going directly to 1.0)
        planeAlpha = planeRenderer.sharedMaterial.color.a;
        boardShapeAlpha = boardShapeMaterial.color.a;

        originHandleSpriteAlphas = originHandleSprites.Select(s => s.color.a).ToArray();
        heightHandleSpriteAlphas = heightHandleSprites.Select(s => s.color.a).ToArray();
        widthHandleSpriteAlphas = widthHandleSprites.Select(s => s.color.a).ToArray();
        
        xLineAlpha = xLineRenderer.material.color.a;
        zLineAlpha = zLineRenderer.material.color.a;
        boxLineAlpha = boxLineRenderer.material.color.a;
    }

    void OnDestroy()
    {
        // Return the boardShapeMaterial to the original color
        Color boardShapeColor = boardShapeMaterial.color;
        boardShapeColor.a = boardShapeAlpha;
        boardShapeMaterial.color = boardShapeColor;
    }

    void Start()
    {
        if (NetworkManager.IsClient)
        {
            NetworkManager.RegisterClientHandler(MsgType.Connect, OnClientConnect);
            NetworkManager.RegisterClientHandler(ConnectFourMsgType.BoardSize, OnClientBoardSizeMessage);

            // Set initial default values (assumes originHandle and heightHandle are on the same y plane)
            origin = originHandle.position;
            bottom = heightHandle.position - originHandle.position; bottom.y = 0f;

            rotation = Quaternion.LookRotation(-bottom, Vector3.up);
            right = Vector3.Project(widthHandle.position - originHandle.position, rotation * Vector3.right); right.y = 0f;
            
            // Immediately hide the gizmo
            FadeGizmo(false, 0.0f);
            FadeInitialHandle(false, 0.0f);

            // Get a reference to the MetaReconstruction object
            metaReconstruction = FindObjectOfType<Meta.MetaReconstruction>();
        }
        
        if (NetworkManager.IsServer)
        {
            NetworkManager.RegisterServerHandler(MsgType.Connect, OnServerConnect);
            NetworkManager.RegisterServerHandler(ConnectFourMsgType.BoardSize, OnServerBoardSizeMessage);
            NetworkManager.RegisterServerHandler(ConnectFourMsgType.BoardOffset, OnServerBoardOffsetMessage);

            // Disable gizmos if we're not a host
            if (!NetworkManager.IsClient)
                SetGizmoComponentsActive(false);
        }
    }

    void LateUpdate()
    {
        if (doingPlacement)
        {
            // Default board position for editor
            if (Input.GetKeyDown(KeyCode.D))
            {
                OnInitialHandlePlaced(true);
            }
        }

        if (doingSetup)
        {
            // Hold shift for x/z translation
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                if (Input.GetKey(KeyCode.LeftArrow))
                    originHandle.position += (rotation * Vector3.left) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.RightArrow))
                    originHandle.position += (rotation * Vector3.right) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.UpArrow))
                    originHandle.position += (rotation * Vector3.forward) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.DownArrow))
                    originHandle.position += (rotation * Vector3.back) * Time.deltaTime * handleMoveSpeed;
            }

            // Hold alt for rotation + y translation
            else if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                if (Input.GetKey(KeyCode.LeftArrow))
                    heightHandle.position += (rotation * Vector3.right) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.RightArrow))
                    heightHandle.position += (rotation * Vector3.left) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.UpArrow))
                    originHandle.position += Vector3.up * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.DownArrow))
                    originHandle.position += Vector3.down * Time.deltaTime * handleMoveSpeed;
            }
            
            // Hold control for size (x/z)
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKey(KeyCode.LeftArrow))
                    widthHandle.position += (rotation * Vector3.left) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.RightArrow))
                    widthHandle.position += (rotation * Vector3.right) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.UpArrow))
                    heightHandle.position += (rotation * Vector3.forward) * Time.deltaTime * handleMoveSpeed;
                if (Input.GetKey(KeyCode.DownArrow))
                    heightHandle.position += (rotation * Vector3.back) * Time.deltaTime * handleMoveSpeed;
            }

            UpdateSetupGizmo();
            UpdateArrowVisuals();

            if (Input.GetKeyDown(KeyCode.Return))
                SubmitBoardSetup();
        }
    }

    void SubmitBoardSetup()
    {
        if (doingSetup == false)
            return;

        doingSetup = false;

        // Finalize board position (this will change origin and rotation)
        FinalizeSetup();

        // Send a board size message to the server
        SendBoardSizeMessage();

#if META_CENTERED_PLAYER
        // Send a board offset message to the server
        SendBoardOffsetMessage();
#endif

        completedSetup = true;

        // Set client state to ready on the server
        ClientScene.Ready(NetworkManager.Client.connection);

        // Show the board visuals
        ConnectFourBoard.Instance.ShowBoard();
    }

    // Called when we've finished setting up the board. This just sets the appropriate positions of the cameraRig, updates the gizmos and fades it out. Generally just cleaning up
    void FinalizeSetup()
    {
        if (cameraRig == null) cameraRig = Camera.main.transform;

#if !META_CENTERED_PLAYER
        // Offset the camera position so that the board appears in the same position as before
        cameraRig.position -= origin;
        
        // Rotate the camera position around the inverse rotation of the board
        cameraRig.position = Quaternion.Inverse(rotation) * cameraRig.position;

        // Subtract the rotation of the board from the rotation of the camera, so the board appears in the same rotation as before. Note the order of operation (as *= will incorrectly use the <rotation> as the second/rightmost parameter)
        cameraRig.rotation = Quaternion.Inverse(rotation) * cameraRig.rotation;

        // Reset the gizmo to it's original position, and axis-aligned rotation
        originHandle.position = Vector3.zero;
        widthHandle.position = originHandle.position + Vector3.right * (GetBoardWidth() / 2f);
        heightHandle.position = originHandle.position + Vector3.back * (GetBoardHeight() / 2f); 
#endif

        // Destroy the reconstruction mesh
        if (reconstructionMesh != null)
            DestroyImmediate(reconstructionMesh);

        // Update the gizmo one last time
        UpdateSetupGizmo(true);

        // Fade out the rendering elements of gizmo
        FadeGizmo(false);
    }

    // Send BoardSizeMessage to server, only if the board size isn't fixed (i.e. the other player already set the board size)
    void SendBoardSizeMessage()
    {
        // Never send board size if we already have one, or if this is a headless instance
        if (hasFixedBoardSize || ConnectFourNPC.IsHeadlessMode())
            return;

        var boardSizeMsg = new BoardSizeMessage()
        {
            playerId = NetworkManager.PlayerID,
            size = new Vector2(GetBoardWidth(), GetBoardHeight()),
        };

        Debug.Log("[CLIENT] ConnectFourBoardSetup : SendBoardSizeMessage - Sending board size of " + boardSizeMsg.size);

        NetworkManager.Client.Send(ConnectFourMsgType.BoardSize, boardSizeMsg);
    }

    // Send BoardOffsetMessage to the server (which sends it to all players)
    void SendBoardOffsetMessage()
    {
        // Move the board visuals to the placement of the board gizmo (this is also done again in ConnectFourBoard.OnBoardOffsetMessage)
        ConnectFourBoard.Instance.SetBoardOffset(NetworkManager.PlayerID, origin, rotation);
        
        var boardOffsetMsg = new BoardOffsetMessage()
        {
            playerId = NetworkManager.PlayerID,
            offset = this.origin,
            rotation = this.rotation
        };

        Debug.Log("[CLIENT] ConnectFourBoardSetup : SendBoardSizeMessage - Sending board offset of offset=" + boardOffsetMsg.offset + ", rotation=" + rotation.eulerAngles);

        NetworkManager.Client.Send(ConnectFourMsgType.BoardOffset, boardOffsetMsg);
    }

    // Sets the center of the gizmo based on a meta surface reconstruction mesh. obj should be a GameObject containing the constructed mesh
    // The bulk of this is used to get rid of outliers in the data. This is done by removing values that fall outside the lower and upper range, and then calculating the average of the remaining vectors.
    void SetGizmoToCenterOfMesh(Transform obj)
    {
        Debug.Log("ConnectFourBoardSetup : SetGizmoToCenterOfMesh - Centering gizmo on " + obj.name);

        reconstructionMesh = obj.gameObject;

        var meshFilter = obj.GetComponent<MeshFilter>();
        var vertices = meshFilter.sharedMesh.vertices;
        int vertexCount = meshFilter.sharedMesh.vertexCount;

        Vector3 center = Vector3.zero;
        int count = 0;

        // Generate 3 separate sorted arrays for all the x, y and z values
        float[] x = vertices.Select(v => v.x).OrderBy(f => f).ToArray();
        float[] y = vertices.Select(v => v.y).OrderBy(f => f).ToArray();
        float[] z = vertices.Select(v => v.z).OrderBy(f => f).ToArray();

        // Get q1,q2 and q3
        int q1_index = (int)Mathf.Floor(vertexCount * 0.25f);   // <- q1 is the point at a quarter through the array
        int q2_index = (int)Mathf.Floor(vertexCount * 0.50f);   // <- q2 is the point at half way
        int q3_index = (int)Mathf.Floor(vertexCount * 0.75f);   // <- q3 is the point at three-quarters

        Vector3 q1 = new Vector3(x[q1_index], y[q1_index], z[q1_index]);
        Vector3 q2 = new Vector3(x[q2_index], y[q2_index], z[q2_index]);
        Vector3 q3 = new Vector3(x[q3_index], y[q3_index], z[q3_index]);

        // Find the IQR (interquartile range) - the difference between the two quarter points
        Vector3 iqr = q3 - q1;

        // Find the lower and upper range
        Vector3 lower = q1 - Vector3.Scale(Vector3.one * 1.5f, iqr);
        Vector3 upper = q3 + Vector3.Scale(Vector3.one * 1.5f, iqr);

        // Get the center point of the mesh, the average vertex position
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 v = vertices[i];

            // Skip outliers
            if (v.x < lower.x || v.y < lower.y || v.z < lower.z ||
                v.x > upper.x || v.y > upper.y || v.z > upper.z)
            {
                Debug.DrawRay(v, Vector3.up * 0.01f, Color.red, 25f);
                continue;
            }
            else
            {
                Debug.DrawRay(v, Vector3.up * 0.01f, Color.green, 25f);
                count++;
                center += v;
            }
        }

        center /= count;

        // Place initial origin handle
        if (doingPlacement)
        {
            initialOriginHandle.position = center;
            OnInitialHandlePlaced();
        }

        // Place originHandle
        else if (doingSetup)
        {
            originHandle.position = center;
            UpdateSetupGizmo(true);
        }
    }

    // --- GIZMO VISUALS METHODS ---

    void UpdateSetupGizmo(bool force = false)
    {
        if (originHandle.hasChanged || heightHandle.hasChanged || widthHandle.hasChanged || force)
        {
            // If we're moving the originHandle
            if (originHandle.hasChanged && !force)
            {
                origin = originHandle.position;
                AdjustHeightHandlePosition();
                AdjustWidthHandlePosition();
            }

            // If we're moving the height/rotation or width handle - update the values that use the positions of these handles
            else if (heightHandle.hasChanged || widthHandle.hasChanged || force)
            {
                origin = originHandle.position;

                // Calculate bottom vector
                if (hasFixedBoardSize)
                {
                    bottom = (heightHandle.position - originHandle.position).normalized * (fixedBoardSize.y / 2f);
                    bottom.y = 0f;
                    AdjustHeightHandlePosition();
                }
                else
                {
                    // The difference between the heightHandle and the origin handle. Note that we don't need to project it on a particular non axis-aligned vector, because it is determining the rotation of the entire gizmo. Since we need x and z, we can zero y
                    bottom = heightHandle.position - originHandle.position;
                    bottom.y = 0f;
                }

                // Get origin and rotation from handles
                rotation = Quaternion.LookRotation(-bottom, Vector3.up);
                originHandle.rotation = rotation;

                // Calculate the right vector
                if (hasFixedBoardSize)
                {
                    right = rotation * (Vector3.right * (fixedBoardSize.x / 2f));
                    right.y = 0.0f;
                }
                else
                {
                    // Right vector is a projection of the difference between the widthHandle and the originHandle positions, but only on the local X axis
                    right = Vector3.Project(widthHandle.position - originHandle.position, rotation * Vector3.right);
                    right.y = 0f;
                }

                // Fix the height handle on the same Y axis as the origin
                AdjustHeightHandlePosition(true);

                // Move the width handle to it's proper position
                AdjustWidthHandlePosition();
            }

            plane.position = originHandle.position + Vector3.down * 0.001f;
            plane.rotation = rotation;
            plane.localScale = new Vector3(GetBoardWidth() / 10.0f, 1.0f, GetBoardHeight() / 10.0f);
            planeRenderer.sharedMaterial.mainTextureScale = new Vector2(GetBoardWidth() * 10f, GetBoardHeight() * 10f);

            boardShape.position = originHandle.position + Vector3.up * 0.15f;
            boardShape.rotation = rotation;

            UpdateLines();

            // Set hasChanged flag to false
            originHandle.hasChanged = heightHandle.hasChanged = widthHandle.hasChanged = false;
        }
    }

    void UpdateLines()
    {
        // Set position of all lines
        zLineRenderer.SetPosition(0, origin);
        zLineRenderer.SetPosition(1, origin + bottom);
        xLineRenderer.SetPosition(0, origin - right);
        xLineRenderer.SetPosition(1, origin + right);

        // Box
        boxLineRenderer.positionCount = 5;
        boxLineRenderer.SetPositions
        (
            new Vector3[]
            {
                // Top left
                (origin - bottom) - right,

                // Top right
                (origin - bottom) + right,

                // Bottom right
                (origin + bottom) + right,

                // Bottom left
                (origin + bottom) - right,

                // Top left
                (origin - bottom) - right
            }
        );
    }

    // Changes what arrows are shown depending on what keys are pressed
    void UpdateArrowVisuals()
    {
        bool showTranslateArrows = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool showRotateArrows = (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
        bool showSizeArrows = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));

        if (translateArrows[0].activeSelf != showTranslateArrows)
        {
            foreach (var a in translateArrows)
                a.SetActive(showTranslateArrows);
        }

        if (rotateArrows[0].activeSelf != showRotateArrows)
        {
            foreach (var a in rotateArrows)
                a.SetActive(showRotateArrows);
        }

        if (sizeArrows[0].activeSelf != showSizeArrows)
        {
            foreach (var a in sizeArrows)
                a.SetActive(showSizeArrows);
        }
    }

    void AdjustHeightHandlePosition(bool onlyAdjustY = false)
    {
        if (onlyAdjustY)
            heightHandle.position = new Vector3(heightHandle.position.x, originHandle.position.y, heightHandle.position.z);
        else
            heightHandle.position = origin + bottom;
        
        heightHandle.rotation = rotation;

        heightHandle.hasChanged = false;
    }

    void AdjustWidthHandlePosition()
    {
        widthHandle.position = origin + right;
        widthHandle.rotation = rotation;
        widthHandle.hasChanged = false;
    }

    void SetGizmoComponentsActive(bool value)
    {
        foreach (Transform child in transform)
        {
            // Never set activity on initialOriginHandle
            if (child == initialOriginHandle)
                continue;

            child.gameObject.SetActive(value);
        }
    }

    // Fades every visual component of the setup gizmo in or out. If fading out, disable the child GameObjects after completed, if fading in it enables them immediately
    void FadeGizmo(bool fadeIn, float speed = 0.5f)
    {
        float alpha = fadeIn ? 1.0f : 0.0f;

        // Fade all the MeshRenderers (except for initialOriginHandle)
        planeRenderer.sharedMaterial.DOFade(planeAlpha * alpha, speed);
        boardShapeMaterial.DOFade(boardShapeAlpha * alpha, speed);
        //boardShapeRenderer.sharedMaterial.DOFade(boardShapeAlpha * alpha, speed);

        // Handle alpha doesn't matter, since it is always 1.0 at faded in
        originHandleRenderer.sharedMaterial.DOFade(alpha, speed);
        widthHandleRenderer.sharedMaterial.DOFade(alpha, speed);
        heightHandleRenderer.sharedMaterial.DOFade(alpha, speed);

        // Fade all sprites
        for (int i = 0; i < originHandleSprites.Length; i++)
            originHandleSprites[i].DOFade(originHandleSpriteAlphas[i] * alpha, speed);
        for (int i = 0; i < heightHandleSprites.Length; i++)
            heightHandleSprites[i].DOFade(heightHandleSpriteAlphas[i] * alpha, speed);
        for (int i = 0; i < widthHandleSprites.Length; i++)
            widthHandleSprites[i].DOFade(widthHandleSpriteAlphas[i] * alpha, speed);

        // Fade all lines
        xLineRenderer.material.DOFade(xLineAlpha * alpha, speed);
        zLineRenderer.material.DOFade(zLineAlpha * alpha, speed);
        boxLineRenderer.material.DOFade(boxLineAlpha * alpha, speed);

        // If fading out, disable components after fading out
        if (fadeIn == false)
        {
            CoroutineHelper.InvokeDelayed(speed, () => SetGizmoComponentsActive(false));
        }

        // If fading in, enable components immediately (before fading)
        else
        {
            SetGizmoComponentsActive(true);
        }
    }

    // Fades the initial placement handle in or out
    void FadeInitialHandle(bool fadeIn, float speed = 0.5f)
    {
        if (fadeIn)
            initialOriginHandle.gameObject.SetActive(true);

        var renderer = initialOriginHandle.GetComponent<Renderer>();
        renderer.sharedMaterial.DOFloat(fadeIn ? 0.75f : 0.0f, ConnectFourBoard.SHADER_ALPHA, speed)
        .OnComplete(() =>
        {
            if (fadeIn == false)
                initialOriginHandle.gameObject.SetActive(false);
        });

        boardSetupCanvas.DOFade(fadeIn ? 1.0f : 0.0f, speed);
    }

    // --- PUBLIC FUNCTIONS ---

    // This is the same as pressing 'D' then 'Return'
    public void SkipSetup()
    {
        OnInitialHandlePlaced(true);
        SubmitBoardSetup();
    }

    public float GetBoardWidth()
    {
        if (hasFixedBoardSize)
            return fixedBoardSize.x;
        else
            return Vector3.Distance(-right, right);
    }

    public float GetBoardHeight()
    {
        if (hasFixedBoardSize)
            return fixedBoardSize.y;
        else
            return Vector3.Distance(-bottom, bottom);
    }

    // Fired when the player presses a Meta button. The MetaButtonEventConnector class was slightly modified to fetch references to all IOnMetaButtonEvent interfaces across the scene, rather than those just under a specific GameObject. 
    public void OnMetaButtonEvent(MetaButton button)
    {
        // If the camera button is short-pressed (and there is a metaReconstruction object)
        if (button.Type == ButtonType.ButtonCamera && button.State == ButtonState.ButtonShortPress)
        {
            if (metaReconstruction != null && metaReconstruction.ReconstructionRoot != null && metaReconstruction.ReconstructionRoot.transform.childCount > 0)
            {
                SetGizmoToCenterOfMesh(metaReconstruction.ReconstructionRoot.transform.GetChild(0));
            }

            Debug.LogWarning("ConnectFourBoardSetup : OnMetaButtonPressed - Camera button pressed, but there is no meshReconstruction object! Scan with Alt-I/Alt-S first!");
        }
    }

    public void OnInitialHandleHover(bool value)
    {
        Debug.Log("ConnectFourBoardSetup : OnInitialHandleHover - " + value);

        if (value)
        {
            initialHandleRenderer.sharedMaterial.EnableKeyword(ConnectFourBoard.SHADER_SCAN_ON_KWD);
        }
        else
        {
            initialHandleRenderer.sharedMaterial.DisableKeyword(ConnectFourBoard.SHADER_SCAN_ON_KWD);
        }
    }

    // When the initial handle gets placed
    public void OnInitialHandlePlaced(bool useDefaults = false)
    {
        if (useDefaults)
            initialOriginHandle.position = defaultOffset;

        float height = GetBoardHeight();
        
        // Set the originHandle of the gizmo
        originHandle.position = initialOriginHandle.position;

        // Point the board towards the MainCamera
        heightHandle.position = originHandle.position + ((rotation * Vector3.back) * height);

        Debug.Log("ConnectFourBoardSetup : OnInitialHandlePlaced - Placed at " + originHandle.position);

        // No longer doing placement
        doingSetup = true;
        doingPlacement = false;

        FadeInitialHandle(false);
        FadeGizmo(true);
    }

    // Called by GrabInteraction on the X/Z and origin handles
    public void OnHandleHover(int key)
    {
        float alpha = key >= 0 ? 1.0f : 0.2f;

        switch (Mathf.Abs(key))
        {
            case 1:
            {
                originHandleRenderer.sharedMaterial.DOFade(alpha, 0.25f);
                break;
            }
            case 2:
            {
                heightHandleRenderer.sharedMaterial.DOFade(alpha, 0.25f);
                break;
            }
            case 3:
            {
                widthHandleRenderer.sharedMaterial.DOFade(alpha, 0.25f);
                break;
            }
        }
    }

    // --- CLIENT CALLBACKS ---

    // Called on client when we connect to the server.
    void OnClientConnect(NetworkMessage msg)
    {
        // Immediately skip setup on connect if we're running in headless mode
        if (ConnectFourNPC.IsHeadlessMode())
            SkipSetup();

        // If we've already completed setup (this happens if this connect is a re-connect after the setup), immediately ready-up
        if (completedSetup)
        {
            // Re-send our board offset/size messages
            SendBoardSizeMessage();
            SendBoardOffsetMessage();

            // Set client state to ready on the server
            ClientScene.Ready(NetworkManager.Client.connection);

            // Show the board visuals
            ConnectFourBoard.Instance.ShowBoard();
        }

        // Begin setup
        else
        {
            Debug.Log("[CLIENT] ConnectFourBoardSetup : OnClientConnect - Starting board setup...");

            doingPlacement = true;
            FadeInitialHandle(true);
            boardSetupCanvas.DOFade(1.0f, 0.5f);
        }
    }

    // Called on client when server sends ConnectFourMsgType.BoardSize. Prevents us from changing the board size if it has already been set on the server
    void OnClientBoardSizeMessage(NetworkMessage msg)
    {
        var boardSizeMsg = msg.ReadMessage<BoardSizeMessage>();

        hasFixedBoardSize = true;
        fixedBoardSize = boardSizeMsg.size;
        UpdateSetupGizmo(true);
    }

    // --- SERVER CALLBACKS ---

    void OnServerConnect(NetworkMessage msg)
    {
        // If we already have a fixed board size, send the newly-connected player the board size
        if (hasFixedBoardSize)
        {
            Debug.Log("[SERVER] ConnectFourBoardSetup : OnServerConnect - Sending connected client fixed board size of " + fixedBoardSize);

            var boardSizeMsg = new BoardSizeMessage()
            {
                playerId = fixedSizeSetByPlayerId,
                size = fixedBoardSize
            };

            msg.conn.Send(ConnectFourMsgType.BoardSize, boardSizeMsg);
        }

        // If we have an offset set by either player, send it to the newly-connected player
        if (p1_offset != null)
        {
            Debug.Log("[SERVER] ConnectFourBoardSetup : OnServerConnect - Sending connected client player 1's board offset");
            msg.conn.Send(ConnectFourMsgType.BoardOffset, p1_offset);
        }
        
        if (p2_offset != null)
        {
            Debug.Log("[SERVER] ConnectFourBoardSetup : OnServerConnect - Sending connected client player 2's board offset");
            msg.conn.Send(ConnectFourMsgType.BoardOffset, p2_offset);
        }
    }

    // Called on the server when the client sends a message of type ConnectFourMsgType.BoardSize to it
    void OnServerBoardSizeMessage(NetworkMessage msg)
    {
        // Ignore if the server has already received a board size (this should never happen, as the check is done client side - so that the client doesn't send it's finalized board size if it is sourcing it from the server)
        if (hasFixedBoardSize)
        {
            Debug.LogError("[SERVER] ConnectFourBoardSetup - OnServerBoardSizeMessage - Recieved a BoardSizeMessage, but the server already has one - ignoring...");
            return;
        }
        
        var boardSizeMsg = msg.ReadMessage<BoardSizeMessage>();

        // Set server-side vars
        hasFixedBoardSize = true;
        fixedSizeSetByPlayerId = boardSizeMsg.playerId;
        fixedBoardSize = boardSizeMsg.size;

        Debug.Log("[SERVER] ConnectFourBoardSetup : OnServerBoardSizeMessage - Player " + boardSizeMsg.playerId + " finished setup. Size = " + boardSizeMsg.size);

        // Send a BoardSize message to all clients
        NetworkManager.SendToAll(ConnectFourMsgType.BoardSize, boardSizeMsg);
    }

    // Called on the server when the client sends a message of type ConnectFourMsgType.BoardOffset to it
    void OnServerBoardOffsetMessage(NetworkMessage msg)
    {
        // Forward directly to the other player
        var boardOffsetMsg = msg.ReadMessage<BoardOffsetMessage>();
        
        // Save the offset's sent from the player (this is done so that if a player completes the setup before the other player has connected, we can send the newly-connected player the offsets)
        if (boardOffsetMsg.playerId == ConnectFour.PLAYER_ONE)
            p1_offset = boardOffsetMsg;
        else if (boardOffsetMsg.playerId == ConnectFour.PLAYER_TWO)
            p2_offset = boardOffsetMsg;

        // Send a BoardOffset message to all clients
        NetworkManager.SendToAll(ConnectFourMsgType.BoardOffset, boardOffsetMsg);
    }
}