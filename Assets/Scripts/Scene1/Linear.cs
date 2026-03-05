using System;
using UnityEngine;

public class Linear : MonoBehaviour
{
    public GameObject agent;
    public float speed;

    private Vector3 startingPosition;
    private Camera cam;

    private void OnCollisionEnter2D(Collision2D other)
    {
        this.transform.position = startingPosition;
        Debug.Log("Collision Detected");
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cam = Camera.main;
        // Store the position value, not the transform reference
        if (agent != null)
        {
            startingPosition = agent.transform.position;
        }
        else
        {
            startingPosition = transform.position;
        }
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.position += new Vector3(speed, 0, 0);

        if (cam != null)
        {
            Vector3 viewPos = cam.WorldToViewportPoint(this.transform.position);
            Debug.Log($"Viewport Coordinates: {viewPos}");
        }
    }
}
