using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class DottedLine : MonoBehaviour
{
    public float scale;
    public float offset;

    public bool scroll;
    [Range(-10f, 10f)]public float scrollSpeed = 0.001f;

    LineRenderer line;
    float dist;

    // Will update the material if changed is set to true
    bool changed;

    // Used mainly for determining length of lines with more than 2 segments
    Vector3[] positions;

    void Start()
    {
        line = GetComponent<LineRenderer>();
        positions = new Vector3[line.positionCount];
    }

    void Update()
    {
        // Re-allocate the positions array if the line positionCount changes
        if (positions.Length != line.positionCount)
            positions = new Vector3[line.positionCount];

        UpdateMaterial();
    }
    
    void UpdateMaterial()
    {
        // Calculate distance for lines with 2 positions
        if (line.positionCount == 2)
        {
            dist = Vector3.Distance(line.GetPosition(0),
                                    line.GetPosition(line.positionCount - 1));
        }
        // Calculate distance for lines with more than 2 positions
        else
        {
            dist = 0.0f;

            for (int i = 0; i < line.GetPositions(positions) - 1; i++)
                dist += Vector3.Distance(positions[i], positions[i + 1]);
        }

        // Set texture scale
        line.material.mainTextureScale = new Vector3(dist * scale, 1, 1);

        // Set texture offset + scroll
        line.material.mainTextureOffset = new Vector3(
            offset + (scroll ? Mathf.Repeat(Time.time * scrollSpeed, 10f) : 0f), 0f, 0f);
    }
}
