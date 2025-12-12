using UnityEngine;

public abstract class BaseShape : MonoBehaviour
{
    public GameObject agentPrefab;
    public int numberOfAgents = 4;
    public float radius = 5f;
    public float moveSpeed = 5f;
    public bool manualControl = false;

    protected Vector3 bottomLeft;
    protected Vector3 topRight;
    protected Vector3 topLeft;
    protected Vector3 bottomRight;

    public abstract void Initialize();
    public abstract void ManualUpdate();
    public abstract void Clear();

    protected void CalculateCameraBounds()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float distanceToZero = Mathf.Abs(cam.transform.position.z);
        bottomLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, distanceToZero));
        topRight = cam.ViewportToWorldPoint(new Vector3(1, 1, distanceToZero));

        // Ensure Z is 0
        bottomLeft.z = 0;
        topRight.z = 0;

        topLeft = new Vector3(bottomLeft.x, topRight.y, 0);
        bottomRight = new Vector3(topRight.x, bottomLeft.y, 0);
    }

    protected void DrawBounds()
    {
        Debug.DrawLine(bottomLeft, topLeft, Color.green);
        Debug.DrawLine(topLeft, topRight, Color.green);
        Debug.DrawLine(topRight, bottomRight, Color.green);
        Debug.DrawLine(bottomRight, bottomLeft, Color.green);
    }
}
