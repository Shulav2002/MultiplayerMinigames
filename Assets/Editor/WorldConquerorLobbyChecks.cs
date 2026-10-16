using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WorldConqueror;

// Runs the actual lobby transition in Play Mode, including deferred destruction.
public static class WorldConquerorLobbyChecks
{
    private const string PendingKey = "WorldConqueror.LobbyChecks.Pending";
    private const string Output = "Temp/WorldConquerorLobbyCheck";
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [InitializeOnLoadMethod]
    private static void Register() => EditorApplication.playModeStateChanged += OnPlayMode;

    [MenuItem("Tools/World Conqueror/Run Lobby Regression Check %&w")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/regression.txt", "Starting lobby checks\n");
        if (EditorApplication.isPlaying) Launch();
        else
        {
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
        }
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);
        Launch();
    }

    private static void Launch()
    {
        var lobby = UnityEngine.Object.FindFirstObjectByType<WorldConquerorLobby>();
        if (lobby == null) { Record("FAIL: Open WorldConqueror scene first."); return; }
        lobby.StartCoroutine(Check(lobby));
    }

    private static IEnumerator Check(WorldConquerorLobby lobby)
    {
        yield return null;
        var maps = (System.Collections.Generic.List<TextAsset>)typeof(WorldConquerorLobby).GetField("maps", Fields).GetValue(lobby);
        foreach (string name in new[] { "Nepal", "Nepal", "India", "Nepal" })
        {
            Call(lobby, "ShowLobby");
            int index = maps.FindIndex(map => map.name == name);
            if (index < 0) { Record("FAIL: Missing " + name); continue; }
            Call(lobby, "SelectMap", index);
            yield return lobby.StartCoroutine((IEnumerator)Call(lobby, "StartMatch"));
            yield return null;
            var fragments = UnityEngine.Object.FindObjectsByType<WorldConquerorPolygonState>(FindObjectsSortMode.None);
            int expected = JsonUtility.FromJson<WorldConquerorMapDefinition>(maps[index].text).regions.Length;
            int actual = fragments.Select(fragment => fragment.StateName).Distinct().Count();
            Record($"{name}: {(actual == expected ? "PASS" : "FAIL")} active territories={actual}, expected={expected}");
            foreach (var label in fragments.SelectMany(fragment => fragment.GetComponentsInChildren<TextMesh>()).Where(label => !string.IsNullOrEmpty(label.text)))
            {
                var renderer = label.GetComponent<MeshRenderer>();
                var marker = label.transform.parent.Find("TargetPoint").GetComponent<MeshRenderer>();
                Vector3 labelSize = renderer.bounds.size;
                Vector3 markerSize = marker.bounds.size;
                Record($"  {label.transform.parent.parent.name}: text={label.text} characterSize={label.characterSize:F3}, worldSize={labelSize}, marker={markerSize}, {(labelSize.x <= markerSize.x && labelSize.y <= markerSize.y ? "PASS" : "FAIL: oversized")}");
            }
            ScreenCapture.CaptureScreenshot(Output + "/" + name + "-check.png");
            yield return new WaitForSecondsRealtime(0.3f);
        }
        Call(lobby, "ShowLobby");
        Record("Checks complete; returned to lobby.");
    }

    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    private static void Record(string text) => File.AppendAllText(Output + "/regression.txt", text + "\n");
}
