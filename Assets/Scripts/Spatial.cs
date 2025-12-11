using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Spatial : MonoBehaviour
{
    public int columns = 2;
    public int rows = 2;

    public GameObject agent;
    public int numberOfAgents = 4;

    public Camera cam;

    [System.Serializable]
    public class AgentData
    {
        public GameObject agent;
        public int forceAdded;
    }

    private List<AgentData> agents;

     // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        agents = new List<AgentData>();
        for(int i = 1; i < numberOfAgents; i++) 
        {
            GameObject newAgent = (GameObject)Instantiate(agent, agent.transform.position, agent.transform.rotation);
            agents.Add(new AgentData { agent = newAgent, forceAdded = -1 });
        }

        if (cam == null)
        {
            cam = Camera.main;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (cam == null) return;

        foreach (var agentData in agents)
        {
            if (agentData.agent == null) continue;

            Vector3 viewportPos = cam.WorldToViewportPoint(agentData.agent.transform.position);

            if (viewportPos.z > 0 && viewportPos.x >= 0 && viewportPos.x <= 1 && viewportPos.y >= 0 && viewportPos.y <= 1)
            {
                int x = Mathf.FloorToInt(viewportPos.x * columns);
                int y = Mathf.FloorToInt(viewportPos.y * rows);

                x = Mathf.Clamp(x, 0, columns - 1);
                y = Mathf.Clamp(y, 0, rows - 1);

                Debug.Log($"Agent {agentData.agent.name} is in grid coordinate: ({x}, {y})");
                foreach(AgentData agentdata in agents)
                {
                    if(x == 0 && y == 0 && agentData.forceAdded != 0) 
                    {
                        Vector2 forceDirection = new Vector2(0.0f,1.0f).normalized;
                        Rigidbody2D rb = agentdata.agent.GetComponent<Rigidbody2D>();
                        rb.AddForce(1 * forceDirection);
                        // agentData.forceAdded = 0;
                        Debug.Log("Force added now");
                    }
                    else if(x == 0 && y == 1 && agentData.forceAdded != 1) 
                    {
                        Vector2 forceDirection = new Vector2(1.0f,0.0f).normalized;
                        Rigidbody2D rb = agentdata.agent.GetComponent<Rigidbody2D>();
                        rb.AddForce(1 * forceDirection);
                        // agentData.forceAdded = 0;
                        Debug.Log("Force added now");
                    }   
                }
            }
            else
            {
                Debug.Log($"Agent {agentData.agent.name} is outside the camera view.");
            }
        }
    }
}
