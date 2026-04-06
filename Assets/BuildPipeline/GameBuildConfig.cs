using System.IO;
using UnityEngine;

[CreateAssetMenu(menuName = "Builds/Game Build Config", fileName = "NewGameBuildConfig")]
public class GameBuildConfig : ScriptableObject
{
    [SerializeField] private string gameId = "new-game";
    [SerializeField] private string displayName = "New Game";
    [SerializeField] private Object entryScene;
    [SerializeField] private string outputFolderName = "NewGame";
    [SerializeField] private string zipFileName = "NewGame.zip";
    [SerializeField] private bool includeInBuildAll = true;

    public string GameId => gameId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Object EntryScene => entryScene;
    public string OutputFolderName => string.IsNullOrWhiteSpace(outputFolderName) ? name : outputFolderName;
    public string ZipFileName => string.IsNullOrWhiteSpace(zipFileName) ? $"{OutputFolderName}.zip" : zipFileName;
    public bool IncludeInBuildAll => includeInBuildAll;

#if UNITY_EDITOR
    public void Initialize(
        string newGameId,
        string newDisplayName,
        Object newEntryScene,
        string newOutputFolderName,
        string newZipFileName,
        bool newIncludeInBuildAll)
    {
        gameId = newGameId;
        displayName = newDisplayName;
        entryScene = newEntryScene;
        outputFolderName = newOutputFolderName;
        zipFileName = newZipFileName;
        includeInBuildAll = newIncludeInBuildAll;
    }
#endif

    private void OnValidate()
    {
        if (!string.IsNullOrWhiteSpace(zipFileName))
        {
            zipFileName = Path.ChangeExtension(zipFileName, ".zip");
        }
    }
}
