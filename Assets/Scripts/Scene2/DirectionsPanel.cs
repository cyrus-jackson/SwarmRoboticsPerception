using UnityEngine;

internal enum SwarmCue { Baseline, Links, Forces, Directions }

/// <summary>Live visual cue selection, independent of the main swarm panel.</summary>
internal sealed class DirectionsPanel
{
    public bool Visible { get; set; }
    private static readonly string[] CueNames = { "Baseline", "Links", "Forces", "Directions" };
    private SwarmCue cue;
    private GUIStyle titleStyle;
    private GUIStyle legendStyle;

    public void Draw(SwarmManager manager)
    {
        const float margin = 10f;
        float height = cue == SwarmCue.Forces ? 128f : 93f;
        float width = Mathf.Min(252f, Screen.width - 2f * margin);
        var panel = new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);

        Color previousColor = GUI.color;
        GUI.color = new Color(0.09f, 0.10f, 0.12f, 0.94f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = previousColor;

        // The main swarm UI uses dark text on a light background. Keep our styles local.
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            legendStyle = new GUIStyle(GUI.skin.label);
            legendStyle.normal.textColor = Color.white;
        }

        GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, width - 24f, 23f), "Swarm visual cues (O)", titleStyle);
        var selected = (SwarmCue)GUI.SelectionGrid(new Rect(panel.x + 12f, panel.y + 33f, width - 24f, 48f),
            (int)cue, CueNames, 2);
        if (cue == SwarmCue.Forces)
            GUI.Label(new Rect(panel.x + 12f, panel.y + 84f, width - 24f, 40f),
                "Red: attraction    Blue: repulsion\nGreen: alignment    White: none", legendStyle);
        if (selected == cue) return;

        cue = selected;
        DirectionArrowVisualizer.SetMode(manager.AgentScripts, cue);
        InteractionLinksVisualizer.SetVisible(manager, cue == SwarmCue.Links);
    }

    public void ApplyToNewSwarm(SwarmManager manager)
    {
        if (cue == SwarmCue.Forces || cue == SwarmCue.Directions)
            DirectionArrowVisualizer.SetMode(manager.AgentScripts, cue);
    }
}
