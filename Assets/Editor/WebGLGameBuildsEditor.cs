using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public class WebGLGameBuildsEditor : EditorWindow
{
    private Vector2 _scrollPosition;
    private List<GameBuildConfig> _configs = new List<GameBuildConfig>();
    private const string ConfigsRoot = "Assets/BuildConfigs/WebGL";
    private const string GamesRoot = "Assets/games";

    [MenuItem("Tools/WebGL Builds/Open Builder")]
    private static void OpenWindow()
    {
        var window = GetWindow<WebGLGameBuildsEditor>("WebGL Builds");
        window.minSize = new Vector2(640f, 320f);
        window.RefreshGameBuilds();
        window.Show();
    }

    [MenuItem("Tools/WebGL Builds/Build All Games")]
    private static void BuildAllGamesMenuItem()
    {
        BuildAllConfiguredGames();
    }

    [MenuItem("Tools/WebGL Builds/Create Missing Build Configs")]
    private static void CreateMissingBuildConfigsMenuItem()
    {
        CreateMissingBuildConfigs();
    }

    [MenuItem("Tools/WebGL Builds/Open Build Folder")]
    private static void OpenBuildFolder()
    {
        var buildsRoot = GetBuildsRoot();
        Directory.CreateDirectory(buildsRoot);
        EditorUtility.RevealInFinder(buildsRoot);
    }

    private void OnEnable()
    {
        RefreshGameBuilds();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("WebGL Game Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds are driven by GameBuildConfig assets. Discovery from Assets/games is only used to bootstrap missing configs.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh", GUILayout.Width(100f)))
            {
                RefreshGameBuilds();
            }

            if (GUILayout.Button("Create Missing Configs", GUILayout.Width(160f)))
            {
                QueueEditorAction(() =>
                {
                    CreateMissingBuildConfigs();
                    RefreshGameBuilds();
                });
            }

            using (new EditorGUI.DisabledScope(_configs.Count == 0))
            {
                if (GUILayout.Button("Build All Games", GUILayout.Height(24f)))
                {
                    QueueEditorAction(() =>
                    {
                        BuildAllConfiguredGames();
                        RefreshGameBuilds();
                    });
                }
            }

            if (GUILayout.Button("Open Build Folder", GUILayout.Width(140f)))
            {
                OpenBuildFolder();
            }
        }

        EditorGUILayout.Space(10f);

        if (_configs.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No GameBuildConfig assets were found. Click Create Missing Configs to bootstrap them from Assets/games.",
                MessageType.Warning);
            return;
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        foreach (var config in _configs)
        {
            DrawGameBuildCard(config);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawGameBuildCard(GameBuildConfig config)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            var scenePath = GetEntryScenePath(config);
            var includeLabel = config.IncludeInBuildAll ? "Included in Build All" : "Excluded from Build All";

            EditorGUILayout.LabelField(config.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Game ID", config.GameId);
            EditorGUILayout.LabelField("Scene", string.IsNullOrWhiteSpace(scenePath) ? "<missing>" : scenePath);
            EditorGUILayout.LabelField("Zip", NormalizeZipFileName(config.ZipFileName));
            EditorGUILayout.LabelField("Batch", includeLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build Zip", GUILayout.Width(100f)))
                {
                    QueueEditorAction(() => BuildSingleGame(config));
                }

                if (GUILayout.Button("Open Folder", GUILayout.Width(100f)))
                {
                    var buildFolder = Path.Combine(GetBuildsRoot(), SanitizeFileName(config.OutputFolderName));
                    Directory.CreateDirectory(buildFolder);
                    EditorUtility.RevealInFinder(buildFolder);
                }
            }
        }
    }

    private void RefreshGameBuilds()
    {
        _configs = LoadConfigs();
        Repaint();
    }

    private static void BuildSingleGame(GameBuildConfig config)
    {
        if (!SaveOpenScenes())
        {
            return;
        }

        if (!EnsureWebGLBuildTarget())
        {
            return;
        }

        var buildResult = BuildGameZip(config);
        AssetDatabase.Refresh();

        if (!buildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("WebGL Build Failed", buildResult.Message, "OK");
            return;
        }

        Debug.Log(buildResult.Message);
        EditorUtility.RevealInFinder(buildResult.ZipPath);
    }

    private static void BuildAllConfiguredGames()
    {
        var configs = LoadConfigs()
            .Where(config => config != null && config.IncludeInBuildAll)
            .ToList();

        if (configs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "No Build Configs",
                "No GameBuildConfig assets were found that are enabled for Build All.",
                "OK");
            return;
        }

        if (!SaveOpenScenes())
        {
            return;
        }

        if (!EnsureWebGLBuildTarget())
        {
            return;
        }

        var results = new List<BuildRunResult>();

        try
        {
            for (var index = 0; index < configs.Count; index++)
            {
                var config = configs[index];
                EditorUtility.DisplayProgressBar(
                    "Building WebGL Games",
                    $"Building {config.DisplayName} ({index + 1}/{configs.Count})",
                    (index + 1f) / configs.Count);

                results.Add(BuildGameZip(config));
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        var successCount = results.Count(result => result.Succeeded);
        var failureCount = results.Count - successCount;
        var summaryLines = new List<string>
        {
            $"Finished building {results.Count} game(s).",
            $"Succeeded: {successCount}",
            $"Failed: {failureCount}",
            $"Output: {GetBuildsRoot()}"
        };

        foreach (var result in results)
        {
            var status = result.Succeeded ? "OK" : "FAILED";
            summaryLines.Add($"{status}: {result.GameName}");
            Debug.Log(result.Message);
        }

        EditorUtility.DisplayDialog(
            "WebGL Build Summary",
            string.Join("\n", summaryLines),
            "OK");

        EditorUtility.RevealInFinder(GetBuildsRoot());
    }

    private static List<GameBuildConfig> LoadConfigs()
    {
        var searchFolders = AssetDatabase.IsValidFolder(ConfigsRoot)
            ? new[] { ConfigsRoot }
            : Array.Empty<string>();

        var guids = searchFolders.Length > 0
            ? AssetDatabase.FindAssets("t:GameBuildConfig", searchFolders)
            : AssetDatabase.FindAssets("t:GameBuildConfig");

        return guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameBuildConfig>)
            .Where(config => config != null)
            .OrderBy(config => config.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CreateMissingBuildConfigs()
    {
        var discoveredGames = DiscoverGameCandidates();
        if (discoveredGames.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "No Games Found",
                "No buildable game scenes were found under Assets/games.",
                "OK");
            return;
        }

        EnsureFolderPathExists(ConfigsRoot);

        var existingById = LoadConfigs()
            .Where(config => !string.IsNullOrWhiteSpace(config.GameId))
            .ToDictionary(config => config.GameId, StringComparer.OrdinalIgnoreCase);

        var createdCount = 0;
        foreach (var candidate in discoveredGames)
        {
            if (existingById.ContainsKey(candidate.GameName))
            {
                continue;
            }

            var config = CreateInstance<GameBuildConfig>();
            config.Initialize(
                candidate.GameName,
                candidate.GameName,
                AssetDatabase.LoadMainAssetAtPath(candidate.ScenePath),
                candidate.GameName,
                $"{candidate.GameName}.zip",
                true);

            var configPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{ConfigsRoot}/{candidate.GameName}BuildConfig.asset");
            AssetDatabase.CreateAsset(config, configPath);
            createdCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Build Config Bootstrap",
            $"Created {createdCount} build config(s) in {ConfigsRoot}.",
            "OK");
    }

    private static List<DiscoveredGame> DiscoverGameCandidates()
    {
        if (!AssetDatabase.IsValidFolder(GamesRoot))
        {
            return new List<DiscoveredGame>();
        }

        var gameFolders = AssetDatabase.GetSubFolders(GamesRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        var discoveredGames = new List<DiscoveredGame>();
        foreach (var gameFolder in gameFolders)
        {
            var gameName = Path.GetFileName(gameFolder);
            var sceneCandidates = AssetDatabase.FindAssets("t:Scene", new[] { gameFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (sceneCandidates.Length == 0)
            {
                continue;
            }

            discoveredGames.Add(new DiscoveredGame
            {
                GameName = gameName,
                ScenePath = PickPrimaryScene(gameName, sceneCandidates)
            });
        }

        return discoveredGames;
    }

    private static string PickPrimaryScene(string gameName, IReadOnlyList<string> sceneCandidates)
    {
        foreach (var candidate in sceneCandidates)
        {
            var sceneName = Path.GetFileNameWithoutExtension(candidate);
            if (string.Equals(sceneName, gameName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        var preferredFolderMatch = sceneCandidates.FirstOrDefault(candidate =>
            candidate.IndexOf("/Scene/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            candidate.IndexOf("/Scenes/", StringComparison.OrdinalIgnoreCase) >= 0);

        return preferredFolderMatch ?? sceneCandidates[0];
    }

    private static BuildRunResult BuildGameZip(GameBuildConfig config)
    {
        if (config == null)
        {
            return BuildRunResult.Failure("Unknown", "Cannot build a null config.");
        }

        if (string.IsNullOrWhiteSpace(config.GameId))
        {
            return BuildRunResult.Failure(config.name, $"Build config {config.name} is missing a game ID.");
        }

        var scenePath = GetEntryScenePath(config);
        if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
        {
            return BuildRunResult.Failure(
                config.DisplayName,
                $"Cannot build {config.DisplayName}. Entry scene is missing or invalid.");
        }

        var buildsRoot = GetBuildsRoot();
        var outputFolderName = SanitizeFileName(config.OutputFolderName);
        var zipFileName = NormalizeZipFileName(config.ZipFileName);
        var buildFolder = Path.Combine(buildsRoot, outputFolderName);
        var zipPath = Path.Combine(buildsRoot, zipFileName);

        Directory.CreateDirectory(buildsRoot);

        if (Directory.Exists(buildFolder))
        {
            Directory.Delete(buildFolder, true);
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = buildFolder,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        if (report.summary.result != BuildResult.Succeeded)
        {
            return BuildRunResult.Failure(
                config.DisplayName,
                $"WebGL build failed for {config.DisplayName}. Result: {report.summary.result}");
        }

        System.IO.Compression.ZipFile.CreateFromDirectory(
            buildFolder,
            zipPath,
            System.IO.Compression.CompressionLevel.Optimal,
            false);

        return BuildRunResult.Success(
            config.DisplayName,
            zipPath,
            $"WebGL build ready for {config.DisplayName}.\nFolder: {buildFolder}\nZip: {zipPath}");
    }

    private static bool SaveOpenScenes()
    {
        return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
    }

    private static void QueueEditorAction(Action action)
    {
        EditorApplication.delayCall += () =>
        {
            action?.Invoke();
        };
    }

    private static bool EnsureWebGLBuildTarget()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
        {
            return true;
        }

        return EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
    }

    private static string GetBuildsRoot()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "Builds", "WebGL");
    }

    private static string GetEntryScenePath(GameBuildConfig config)
    {
        if (config == null || config.EntryScene == null)
        {
            return string.Empty;
        }

        var scenePath = AssetDatabase.GetAssetPath(config.EntryScene);
        return scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
            ? scenePath
            : string.Empty;
    }

    private static void EnsureFolderPathExists(string assetFolderPath)
    {
        var parts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var currentPath = parts[0];
        for (var index = 1; index < parts.Length; index++)
        {
            var nextPath = $"{currentPath}/{parts[index]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, parts[index]);
            }

            currentPath = nextPath;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? "GameBuild" : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalidCharacter, '_');
        }

        return candidate;
    }

    private static string NormalizeZipFileName(string value)
    {
        var fileName = SanitizeFileName(value);
        return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.zip";
    }

    private sealed class DiscoveredGame
    {
        public string GameName;
        public string ScenePath;
    }

    private readonly struct BuildRunResult
    {
        public string GameName { get; }
        public string ZipPath { get; }
        public string Message { get; }
        public bool Succeeded { get; }

        private BuildRunResult(string gameName, string zipPath, string message, bool succeeded)
        {
            GameName = gameName;
            ZipPath = zipPath;
            Message = message;
            Succeeded = succeeded;
        }

        public static BuildRunResult Success(string gameName, string zipPath, string message)
        {
            return new BuildRunResult(gameName, zipPath, message, true);
        }

        public static BuildRunResult Failure(string gameName, string message)
        {
            return new BuildRunResult(gameName, string.Empty, message, false);
        }
    }
}
