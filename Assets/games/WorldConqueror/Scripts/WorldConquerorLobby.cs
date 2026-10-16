using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace WorldConqueror
{
    [DisallowMultipleComponent]
    public class WorldConquerorLobby : MonoBehaviour
    {
        private WorldConquerorMapBootstrap bootstrap;
        private WorldConquerorGameManager manager;
        private readonly List<TextAsset> maps = new();
        private readonly List<Button> mapButtons = new();
        private GameObject lobbyRoot;
        private GameObject matchRoot;
        private TMP_Text selectionLabel;
        private Button startButton;
        private Button difficultyButton;
        private Button personalityButton;
        private readonly Button[] allocationButtons = new Button[3];
        private TMP_FontAsset font;
        private int selectedIndex;

        public void Configure(WorldConquerorMapBootstrap mapBootstrap, TextAsset[] choices, TextAsset current)
        {
            bootstrap = mapBootstrap;
            manager = FindFirstObjectByType<WorldConquerorGameManager>();
            manager.StopMatch();
            foreach (TextAsset map in choices)
                if (map != null && !maps.Contains(map)) maps.Add(map);
            if (current != null && !maps.Contains(current)) maps.Insert(0, current);
            selectedIndex = Mathf.Max(0, maps.IndexOf(current));
            BuildUI();
            manager.SendPercentChanged += UpdateAllocationButtons;
            UpdateAllocationButtons(manager.SendPercent);
            SelectMap(selectedIndex);
        }

        private void OnDestroy()
        {
            if (manager != null) manager.SendPercentChanged -= UpdateAllocationButtons;
        }

        private void BuildUI()
        {
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("WorldConquerorEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            GameObject canvasObject = new("WorldConquerorUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.pixelPerfect = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            lobbyRoot = Panel("Lobby", canvasObject.transform, 0.055f);
            Stretch(lobbyRoot.GetComponent<RectTransform>());
            GameObject card = Panel("LobbyCard", lobbyRoot.transform, 0.075f);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(1200, 650);
            HorizontalLayoutGroup layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 24, 24);
            layout.spacing = 32;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            GameObject globeColumn = Column("GlobeOverview", card.transform, 650);
            Label("WORLD CONQUEROR", globeColumn.transform, 32, 50, 0.96f);
            Label("G L O B A L   C O N F L I C T", globeColumn.transform, 13, 24, 0.52f);
            GameObject globe = new("RotatingEarth", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement), typeof(WorldConquerorLobbyGlobe));
            globe.transform.SetParent(globeColumn.transform, false);
            globe.GetComponent<LayoutElement>().flexibleHeight = 1;
            globe.GetComponent<LayoutElement>().minHeight = 360;
            Label("ORBITAL VIEW   /   COMBAT SIMULATION", globeColumn.transform, 12, 24, 0.5f);

            GameObject mapColumn = Column("MissionControls", card.transform, 410);
            mapColumn.GetComponent<VerticalLayoutGroup>().spacing = 6;
            Label("DEPLOYMENT", mapColumn.transform, 28, 44, 0.96f);
            Label("Choose your battlefield", mapColumn.transform, 17, 32, 0.64f);
            Label("SELECT MAP", mapColumn.transform, 12, 24, 0.5f);
            for (int i = 0; i < maps.Count; i++)
            {
                int index = i;
                mapButtons.Add(MakeButton(maps[i].name, mapColumn.transform, 42, () => SelectMap(index)));
            }
            selectionLabel = Label("", mapColumn.transform, 16, 32, 0.72f);
            difficultyButton = MakeButton("", mapColumn.transform, 38, CycleDifficulty);
            personalityButton = MakeButton("", mapColumn.transform, 38, CyclePersonality);
            UpdateBotButtons();
            startButton = MakeButton("START GAME", mapColumn.transform, 58, () => StartCoroutine(StartMatch()));
            Label("Drag one base to another to send troops", mapColumn.transform, 13, 26, 0.52f);
            matchRoot = new GameObject("MatchControls", typeof(RectTransform));
            matchRoot.transform.SetParent(canvasObject.transform, false);
            Stretch(matchRoot.GetComponent<RectTransform>());
            Button back = MakeButton("LOBBY", matchRoot.transform, 44, ShowLobby);
            RectTransform backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = backRect.anchorMax = new Vector2(0, 1);
            backRect.pivot = new Vector2(0, 1);
            backRect.anchoredPosition = new Vector2(20, -20);
            backRect.sizeDelta = new Vector2(120, 44);
            GameObject allocationBar = new("TroopAllocation", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            allocationBar.transform.SetParent(matchRoot.transform, false);
            RectTransform barRect = allocationBar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -20f);
            barRect.sizeDelta = new Vector2(330f, 44f);
            HorizontalLayoutGroup barLayout = allocationBar.GetComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 8f;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            for (int i = 0; i < allocationButtons.Length; i++)
            {
                int percent = i == 0 ? 25 : i == 1 ? 50 : 100;
                allocationButtons[i] = MakeButton(percent + "%", allocationBar.transform, 44, () => manager.SetSendPercent(percent));
                allocationButtons[i].GetComponent<LayoutElement>().preferredWidth = 100f;
            }
            matchRoot.SetActive(false);
        }

        private void UpdateAllocationButtons(int selectedPercent)
        {
            for (int i = 0; i < allocationButtons.Length; i++)
            {
                if (allocationButtons[i] == null) continue;
                int percent = i == 0 ? 25 : i == 1 ? 50 : 100;
                allocationButtons[i].GetComponent<Image>().color = Gray(percent == selectedPercent ? 0.55f : 0.24f);
                allocationButtons[i].GetComponentInChildren<TMP_Text>().text =
                    (percent == selectedPercent ? "• " : "") + percent + "%";
            }
        }

        private void CycleDifficulty()
        {
            manager.SetBotDifficulty((WorldConquerorGameManager.BotDifficulty)(((int)manager.Difficulty + 1) % 3));
            UpdateBotButtons();
        }

        private void CyclePersonality()
        {
            manager.SetBotPersonality((WorldConquerorGameManager.BotPersonality)(((int)manager.Personality + 1) % 4));
            UpdateBotButtons();
        }

        private void UpdateBotButtons()
        {
            if (difficultyButton != null) difficultyButton.GetComponentInChildren<TMP_Text>().text = "BOT: " + manager.Difficulty;
            if (personalityButton != null) personalityButton.GetComponentInChildren<TMP_Text>().text = "STYLE: " + manager.Personality;
        }

        private void SelectMap(int index)
        {
            selectedIndex = index;
            for (int i = 0; i < mapButtons.Count; i++)
            {
                mapButtons[i].GetComponent<Image>().color = Gray(i == index ? 0.43f : 0.24f);
                mapButtons[i].GetComponentInChildren<TMP_Text>().text = (i == index ? "•  " : "") + maps[i].name;
            }
            selectionLabel.text = maps.Count > 0 ? maps[index].name + "  /  You vs. bot" : "Assign maps on the map bootstrap to play";
            startButton.interactable = maps.Count > 0;
        }

        private IEnumerator StartMatch()
        {
            startButton.interactable = false;
            manager.StopMatch();
            bootstrap.LoadMap(maps[selectedIndex]);
            // Allow the previous generated map to finish being destroyed before initialization.
            yield return null;
            manager.BeginMatch();
            lobbyRoot.SetActive(false);
            matchRoot.SetActive(true);
            startButton.interactable = true;
        }

        private void ShowLobby()
        {
            manager.StopMatch();
            matchRoot.SetActive(false);
            lobbyRoot.SetActive(true);
        }

        private GameObject Column(string name, Transform parent, float width)
        {
            GameObject column = new(name, typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            column.transform.SetParent(parent, false);
            LayoutElement sizing = column.GetComponent<LayoutElement>();
            sizing.preferredWidth = width;
            sizing.flexibleWidth = 1;
            VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return column;
        }
        private GameObject Panel(string name, Transform parent, float shade)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = Gray(shade);
            return panel;
        }

        private TMP_Text Label(string value, Transform parent, int size, float height, float shade)
        {
            GameObject label = new("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            label.transform.SetParent(parent, false);
            TMP_Text text = label.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.text = value;
            text.color = Gray(shade);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            label.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        private Button MakeButton(string title, Transform parent, float height, UnityEngine.Events.UnityAction action)
        {
            GameObject control = Panel(title, parent, 0.28f);
            control.AddComponent<LayoutElement>().preferredHeight = height;
            Button button = control.AddComponent<Button>();
            button.targetGraphic = control.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Gray(0.85f);
            colors.pressedColor = Gray(0.65f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Gray(0.4f);
            button.colors = colors;
            button.onClick.AddListener(action);
            TMP_Text text = Label(title, control.transform, 18, height, 0.96f);
            Stretch(text.rectTransform);
            return button;
        }

        private static Color Gray(float value) => new(value, value, value, 1);

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
