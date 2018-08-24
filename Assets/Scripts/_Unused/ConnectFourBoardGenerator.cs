using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// Dynamic generation of the actual visual representation of a ConnectFour board. In this case, we're going for something holographic-y, so the board is represented by a set of dotted lines floating in space
public class ConnectFourBoardGenerator : MonoBehaviour
{
    // The visual cube that makes up a space in the ConnectFour board
    public class Space
    {
        public GameObject root { get; private set; }
        public GameObject leftFace, rightFace, topFace, bottomFace, frontFace, backFace;

        SpriteRenderer[] dots;

        public Space() {}

        public Space(GameObject root)
        {
            this.root = root;
        }

        // Amasses the collection of SpriteRenderers from all connected faces
        public void CollectDots()
        {
            var temp = new List<SpriteRenderer>();

            temp.AddRange(GetSpriteRenderersInObject(leftFace));
            temp.AddRange(GetSpriteRenderersInObject(rightFace));
            temp.AddRange(GetSpriteRenderersInObject(topFace));
            temp.AddRange(GetSpriteRenderersInObject(bottomFace));
            temp.AddRange(GetSpriteRenderersInObject(frontFace));
            temp.AddRange(GetSpriteRenderersInObject(backFace));

            dots = temp.ToArray();
        }

        SpriteRenderer[] GetSpriteRenderersInObject(GameObject gameObject)
        {
            if (gameObject != null)
                return gameObject.GetComponentsInChildren<SpriteRenderer>(true);
            else
                return new SpriteRenderer[0];
        }
    }

    public float edgeLength = 1.0f;
    public int dotsPerEdge;
    public float dotScale;
    public Sprite dotSprite;
    public Material dotMaterial;

    float edgeLengthHalf;

    // Every vert in a cube, with repeats for faces that have different normals
    static readonly Vector3Int[] cube_verts = new Vector3Int[]
    {
        // Top face
        new Vector3Int(-1,+1,+1),  // -X,+Y,+Z
        new Vector3Int(+1,+1,+1),  // +X,+Y,+Z
        new Vector3Int(+1,+1,-1),  // +X,+Y,-Z
        new Vector3Int(-1,+1,-1),  // -X,+Y,-Z
        // Bottom face (invert y)
        new Vector3Int(-1,-1,+1),  // -X,-Y,+Z
        new Vector3Int(+1,-1,+1),  // +X,-Y,+Z
        new Vector3Int(+1,-1,-1),  // +X,-Y,-Z
        new Vector3Int(-1,-1,-1),  // -X,-Y,-Z
        // Left face
        new Vector3Int(-1,+1,+1),  // -X,+Y,+Z
        new Vector3Int(-1,+1,-1),  // -X,+Y,-Z
        new Vector3Int(-1,-1,-1),  // -X,-Y,-Z
        new Vector3Int(-1,-1,+1),  // -X,-Y,+Z
        // Right face (invert x)
        new Vector3Int(+1,+1,+1),  // +X,+Y,+Z
        new Vector3Int(+1,+1,-1),  // +X,+Y,-Z
        new Vector3Int(+1,-1,-1),  // +X,-Y,-Z
        new Vector3Int(+1,-1,+1),  // +X,-Y,+Z
        // Front face
        new Vector3Int(-1,+1,+1),  // -X,+Y,-Z
        new Vector3Int(+1,+1,+1),  // +X,+Y,-Z
        new Vector3Int(+1,-1,+1),  // +X,-Y,-Z
        new Vector3Int(-1,-1,+1),  // -X,-Y,-Z
        // Back face (invert z)
        new Vector3Int(-1,+1,-1),  // -X,+Y,-Z
        new Vector3Int(+1,+1,-1),  // +X,+Y,-Z
        new Vector3Int(+1,-1,-1),  // +X,-Y,-Z
        new Vector3Int(-1,-1,-1),  // -X,-Y,-Z
    };

    // Top face edges (no shared edges)
    static readonly Vector2Int[] cube_edges_top = new Vector2Int[]
    {
        new Vector2Int(0, 1), new Vector2Int(1, 2), new Vector2Int(2, 3), new Vector2Int(3, 0)
    };

    // Bottom face edges (no shared edges)
    static readonly Vector2Int[] cube_edges_bottom = new Vector2Int[]
    {
        new Vector2Int(4, 5), new Vector2Int(5, 6), new Vector2Int(6, 7), new Vector2Int(7, 4)
    };

    // Left face edges (no shared edges)
    static readonly Vector2Int[] cube_edges_left = new Vector2Int[]
    {
        new Vector2Int(8, 9), new Vector2Int(9, 10), new Vector2Int(10, 11), new Vector2Int(11, 8)
    };

    // Right face edges (no shared edges)
    static readonly Vector2Int[] cube_edges_right = new Vector2Int[]
    {
        new Vector2Int(12, 13), new Vector2Int(13, 14), new Vector2Int(14, 15), new Vector2Int(15, 12)
    };

    // Front face edges (no shared edges)
    static readonly Vector2Int[] cube_edges_front = new Vector2Int[]
    {
        new Vector2Int(16, 17), new Vector2Int(17, 18), new Vector2Int(18, 19), new Vector2Int(19, 16)
    };

    // Back face edges (no shared edges)
    static readonly Vector2Int[] cube_edges_back = new Vector2Int[]
    {
        new Vector2Int(20, 21), new Vector2Int(21, 22), new Vector2Int(22, 23), new Vector2Int(23, 20)
    };

    void Start()
    {
        GenerateBoard();
    }

    void GenerateBoard()
    {
        dotMaterial.SetFloat("Scale", dotScale);
        edgeLengthHalf = edgeLength / 2.0f;
        
        var spaces = new Space[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];

        Vector3 topLeft = new Vector3(-(ConnectFour.BOARD_WIDTH * edgeLength / 2.0f) + edgeLengthHalf, (ConnectFour.BOARD_HEIGHT * edgeLength / 2.0f) - edgeLengthHalf,
                                      0f);

        // From left to right
        for (int x = 0; x < ConnectFour.BOARD_WIDTH; x++)
        {
            Vector3 top = topLeft + Vector3.right * edgeLength * x;

            // From top to bottom
            for (int y = 0; y < ConnectFour.BOARD_HEIGHT; y++)
            {
                var obj = new GameObject("x" + x + "y" + y);
                obj.transform.parent = transform;
                obj.transform.localPosition = top + Vector3.down * edgeLength * y;

                var space = new Space
                {
                    topFace = GenerateCubeEdges(cube_edges_top, obj.transform),
                    bottomFace = GenerateCubeEdges(cube_edges_bottom, obj.transform),
                    leftFace = GenerateCubeEdges(cube_edges_left, obj.transform),
                    rightFace = GenerateCubeEdges(cube_edges_right, obj.transform),
                    frontFace = GenerateCubeEdges(cube_edges_front, obj.transform),
                    backFace = GenerateCubeEdges(cube_edges_back, obj.transform)
                };

                // Collect dots for this space (this pretty much just accumulates all the SpriteRenderer's)
                space.CollectDots();

                spaces[x, y] = space;
            }
        }
    }

    // Generates cube edges for a specific face, given an array of Vector2Ints (see cube_edges_x). Each Vector2Int will be used to look up the associated vertex in cube_verts.
    GameObject GenerateCubeEdges(Vector2Int[] vertIndexes, Transform parent)
    {
        Vector3Int[] startVerts = new Vector3Int[vertIndexes.Length];
        Vector3Int[] endVerts = new Vector3Int[vertIndexes.Length];

        for (int i = 0; i < vertIndexes.Length; i++)
        {
            startVerts[i] = cube_verts[vertIndexes[i].x];
            endVerts[i] = cube_verts[vertIndexes[i].y];
        }

        return GenerateCubeEdges(startVerts, endVerts, parent);
    }

    // Generates cube edges for a specific face, given an array of start and end vertices. Lines will be drawn from start[i] to end[i], therefore the arrays need to be the same size
    GameObject GenerateCubeEdges(Vector3Int[] startVerts, Vector3Int[] endVerts, Transform parent)
    {
        if (startVerts.Length != endVerts.Length)
            return null;

        string faceName = "";
        Vector3 facePos = Vector3.zero;
        
        // Create sub-object for these edges, based on whatever axis is shared across all verts

        // As faces of a cube can't face in two directions, we do an else-if
        if (startVerts.Union(endVerts).All(v => v.x == startVerts[0].x))
        {
            if (startVerts[0].x < 0.0f)
            {
                faceName = "Left";
                facePos = Vector3.left;
            }
            else
            {
                faceName = "Right";
                facePos = Vector3.right;
            }
        }
        else if (startVerts.Union(endVerts).All(v => v.y == startVerts[0].y))
        {
            if (startVerts[0].y < 0.0f)
            {
                faceName = "Bottom";
                facePos = Vector3.down;
            }
            else
            {
                faceName = "Top";
                facePos = Vector3.up;
            }
        }
        else if (startVerts.Union(endVerts).All(v => v.z == startVerts[0].z))
        {
            if (startVerts[0].z < 0.0f)
            {
                faceName = "Back";
                facePos = Vector3.back;
            }
            else
            {
                faceName = "Forward";
                facePos = Vector3.forward;
            }
        }

        // Create the face object
        var face = new GameObject(faceName + "Face").transform;
        face.parent = parent;
        face.localPosition = facePos * edgeLengthHalf;
        
        // Create all the edges
        for (int i = 0; i < startVerts.Length; i++)
        {
            var edge = GenerateCubeEdge(startVerts[i], endVerts[i], face);
        }

        return face.gameObject;
    }

    // Generate a single dotted line of SpriteRenderers. This overload will fetch two Vector3Ints vertices from the cube_verts array, given their indexes in vertIndexes.x and vertIndexes.y
    GameObject GenerateCubeEdge(Vector2Int vertIndexes, Transform parent, bool center = true)
    {
        return GenerateCubeEdge(vertIndexes.x, vertIndexes.y, parent, center);
    }

    // Generate a single dotted line of SpriteRenderers. This overload will fetch two Vector3Ints vertices from the cube_verts array, given their indexes 
    GameObject GenerateCubeEdge(int vertIndex1, int vertIndex2, Transform parent, bool center = true)
    {
        return GenerateCubeEdge(cube_verts[vertIndex1], cube_verts[vertIndex2], parent, center);
    }

    // Generate a single dotted line of SpriteRenderers, given 2 Vector3Int offsets (verts). p1 and p2 should be no more than 1 and no less than -1
    GameObject GenerateCubeEdge(Vector3Int p1, Vector3Int p2, Transform parent, bool center = true)
    {
        string edgeName = "";
        Vector3 edgePos = Vector3.zero;

        // If an axis has the same value across the two points
        // - Add the name of that axis direction to the parentName
        // - Offset the edge object (this causes the dots to be centered)

        if (p1.x == p2.x)
        {
            edgeName += (p1.x < 0.0f) ? "_Left (-X)" : "_Right (+X)";

            // If the offset of the edge is the same offset as the face it is parented to, zero the offset in that direction
            if (parent.localPosition.x > 0f && p1.x > 0f || parent.localPosition.x < 0f && p1.x < 0f)
            { }
            else
                edgePos += Vector3.right * p1.x;

            // Zero the offset, so that we are only generating dots centered to the parent
            if (center) p1.x = p2.x = 0;
        }
        if (p1.y == p2.y)
        {
            edgeName += (p1.y < 0.0f) ? "_Bottom (-Y)" : "_Top (+Y)";
            if (parent.localPosition.y > 0f && p1.y > 0f || parent.localPosition.y < 0f && p1.y < 0f)
            { }
            else
                edgePos += Vector3.up * p1.y;
            if (center) p1.y = p2.y = 0;
        }
        if (p1.z == p2.z)
        {
            edgeName += (p1.z < 0.0f) ? "_Front (-Z)" : "_Back (+Z)";

            if (parent.localPosition.z > 0f && p1.z > 0f || parent.localPosition.z < 0f && p1.z < 0f)
            { }
            else
                edgePos += Vector3.forward * p1.z;

            if (center) p1.z = p2.z = 0;
        }

        // Create the edge object, parenting it to the passed <parent>
        var edge = new GameObject(edgeName).transform;
        edge.parent = parent;
        edge.localPosition = edgePos * edgeLengthHalf;

        Vector3 deltaNormalized = ((Vector3)(p2 - p1)).normalized;
        Vector3 delta = deltaNormalized * edgeLength;
        Vector3 start = ((Vector3)p1 * edgeLengthHalf);

        var ray = new Ray((Vector3)p1 * edgeLengthHalf, deltaNormalized);
        Debug.DrawRay(ray.origin, ray.direction, Color.red, 5f);

        // Create all the dots
        for (int i = 0; i < dotsPerEdge; i++)
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            //var dot = new GameObject(i.ToString()).transform;
            dot.name = i.ToString();
            dot.parent = edge;

            // Set the local position to the start, plus an offset in the direction of the delta
            dot.localPosition = start + (delta * ((float)i / dotsPerEdge));
            dot.localScale = Vector3.one * dotScale;

            var meshRenderer = dot.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = dotMaterial;
            //var renderer = dot.gameObject.AddComponent<SpriteRenderer>();
            //renderer.sprite = dotSprite;
            //renderer.sharedMaterial = dotMaterial;
        }

        return edge.gameObject;
    }
}