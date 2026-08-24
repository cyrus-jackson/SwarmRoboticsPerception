using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// A fixed set of agent starting positions, saved so several runs can begin from exactly the same
/// arrangement.
///
/// Spawning is random, so two runs that differ only in a parameter also differ in where every
/// agent happened to start. That spawn luck lands in the measurements and cannot be separated from
/// the parameter's effect. Recording the layout once and replaying it turns the comparison into a
/// paired one: the only thing that differs between the runs is the parameter.
///
/// A seeded RNG would not do the same job. The spawn routines reject positions using the safety
/// distance and obstacle clearance, so the sequence of accepted positions depends on parameters
/// that a sweep changes. Storing the accepted positions is independent of all of that.
/// </summary>
[Serializable]
public class SpawnLayout
{
    /// <summary>Short identifier, recorded with every clip that used this layout.</summary>
    public string layoutId;

    public string createdUtc;
    public string sceneName;
    public string swarmType;
    public string spawnType;
    public string spawnAreaName;
    public int agentCount;

    /// <summary>Flattened world positions, x and y per agent.</summary>
    public List<float> positions = new List<float>();

    public int Count => positions.Count / 2;

    public Vector2 GetPosition(int index)
    {
        return new Vector2(positions[index * 2], positions[index * 2 + 1]);
    }

    public static SpawnLayout FromAgents(IList<GameObject> agents, string sceneName, string swarmType,
                                         string spawnType, string spawnAreaName, string layoutId)
    {
        SpawnLayout layout = new SpawnLayout
        {
            layoutId = layoutId,
            createdUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            sceneName = sceneName,
            swarmType = swarmType,
            spawnType = spawnType,
            spawnAreaName = spawnAreaName,
        };

        foreach (GameObject agent in agents)
        {
            if (agent == null) continue;
            Vector3 p = agent.transform.position;
            layout.positions.Add(p.x);
            layout.positions.Add(p.y);
        }

        layout.agentCount = layout.Count;
        return layout;
    }

    public void Save(string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonUtility.ToJson(this, true));
    }

    public static SpawnLayout Load(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"[SpawnLayout] No file at {path}");
            return null;
        }

        SpawnLayout layout = JsonUtility.FromJson<SpawnLayout>(File.ReadAllText(path));
        if (layout == null || layout.Count == 0)
        {
            Debug.LogError($"[SpawnLayout] {path} holds no positions");
            return null;
        }

        return layout;
    }

    /// <summary>Default folder for saved layouts, under Assets.</summary>
    public static string DefaultFolder(string saveFolder)
    {
        return Path.Combine(Application.dataPath, saveFolder, "SpawnLayouts");
    }

    public override string ToString()
    {
        return $"{layoutId} ({Count} agents, {swarmType}, {spawnType})";
    }

    /// <summary>
    /// Rough fingerprint of the positions, so two clips can be confirmed to have started from the
    /// same arrangement without comparing every coordinate.
    /// </summary>
    public string Fingerprint()
    {
        unchecked
        {
            int hash = 17;
            foreach (float v in positions)
            {
                hash = hash * 31 + Mathf.RoundToInt(v * 1000f);
            }
            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}
