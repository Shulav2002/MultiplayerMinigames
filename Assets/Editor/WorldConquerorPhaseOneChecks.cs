using System;
using System.Collections;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldConqueror;

// Scene-level regression checks for an isolated Unity batch project.
public static class WorldConquerorPhaseOneChecks
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/games/WorldConqueror/Scene/WorldConqueror.unity");
        WorldConquerorGameManager manager = UnityEngine.Object.FindFirstObjectByType<WorldConquerorGameManager>();
        Require(manager != null, "manager exists");
        manager.BeginMatch();
        IDictionary territories = (IDictionary)typeof(WorldConquerorGameManager)
            .GetField("territories", InstanceFields).GetValue(manager);
        object owned = null, neutral = null;
        foreach (DictionaryEntry entry in territories)
        {
            object territory = entry.Value;
            if (!(bool)Read(territory, "Active")) continue;
            int owner = (int)Read(territory, "OwnerId");
            if (owner == 0) owned = territory;
            if (owner == -1 && neutral == null) neutral = territory;
        }
        Require(owned != null && neutral != null, "owned and neutral bases exist");
        string targetName = (string)Read(neutral, "Name");

        Write(neutral, "Garrison", 10f);
        manager.ApplyIncomingTroops(targetName, 0, 10);
        Require((int)Read(neutral, "OwnerId") == -1 && Near((float)Read(neutral, "Garrison"), 0f),
            "10 arrivals deplete neutral defense without capture");
        manager.ApplyIncomingTroops(targetName, 0, 1);
        Require((int)Read(neutral, "OwnerId") == 0 && Near((float)Read(neutral, "Garrison"), 1f),
            "11th arrival captures with one stationed troop");

        Write(neutral, "OwnerId", -1);
        Write(neutral, "Garrison", 12.5f);
        manager.ApplyIncomingTroops(targetName, 0, 13);
        Require((int)Read(neutral, "OwnerId") == -1 && Near((float)Read(neutral, "Garrison"), 0f),
            "13 arrivals deplete fractional 12.5 defense");
        manager.ApplyIncomingTroops(targetName, 0, 1);
        Require((int)Read(neutral, "OwnerId") == 0, "14th arrival captures fractional defense");

        Write(neutral, "OwnerId", -1);
        Write(neutral, "Garrison", 5f);
        typeof(WorldConquerorGameManager).GetMethod("AdvanceEconomy", InstanceFields).Invoke(manager, new object[] { 1f });
        Require(Near((float)Read(neutral, "Garrison"), 6f), "neutral regenerates one defender in one second");

        Write(owned, "Garrison", 0f);
        Require((int)Read(owned, "OwnerId") == 0, "zero-defense owned base retains its owner");

        Write(owned, "Garrison", 99f);
        typeof(WorldConquerorGameManager).GetMethod("AdvanceEconomy", InstanceFields).Invoke(manager, new object[] { 2f });
        Require((float)Read(owned, "Garrison") >= 101f, "owned production continues above 99 troops");

        Write(owned, "Garrison", 11f);
        manager.SetSendPercent(25);
        bool sent = (bool)typeof(WorldConquerorGameManager).GetMethod("SendTroops", InstanceFields)
            .Invoke(manager, new[] { owned, neutral, (object)25 });
        Require(sent && Near((float)Read(owned, "Garrison"), 9f),
            "25 percent sends floor(11 x 0.25) and deducts immediately");
        IList arrivals = (IList)typeof(WorldConquerorGameManager).GetField("arrivals", InstanceFields).GetValue(manager);
        Require(arrivals.Count == 2, "two troop arrivals scheduled");
        float first = (float)Read(arrivals[0], "Time");
        float second = (float)Read(arrivals[1], "Time");
        Require(Near(second - first, 0.10f), "departures are spaced 0.10 seconds");
        Debug.Log("World Conqueror phase one rule checks passed.");
    }

    private static object Read(object target, string property) =>
        target.GetType().GetProperty(property, PublicInstance).GetValue(target);

    private static void Write(object target, string property, object value) =>
        target.GetType().GetProperty(property, PublicInstance).SetValue(target, value);

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("World Conqueror rule check failed: " + message);
    }
}
