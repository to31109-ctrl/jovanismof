using Assets.Scripts.Settings___Saves.SaveFiles;
using Microsoft.Extensions.DependencyInjection;
using MegabonkTogether.Services;
using System.Collections.Generic;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Configuration;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Button;
using MegabonkTogether.Scripts.Modal;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace MegabonkTogether.Scripts
{
    internal class NetworkMenuTab : ModalBase
    {
        private CustomButton randomButton;
        private CustomButton friendliesButton;
        private CustomButton closeButton;
        private CustomButton stopButton;
        private TMP_InputField playerNameInput;
        private MainMenu mainMenu;
        private GameObject label;
        private bool wasInputFocused;
        private Coroutine connectionCoroutine;
        private ProfanityFilter.ProfanityFilter filter;

        private GameObject friendliesTitle;
        private CustomButton hostButton;
        private CustomButton joinButton;
        private CustomButton friendliesBackButton;
        private TMP_InputField codeInput;
        private GameObject codeLabel;

        private GameObject saveToggleSetting;
        private CustomButton saveToggleLeftButton;
        private CustomButton saveToggleRightButton;
        private TextMeshProUGUI saveToggleStatusText;

        private CustomButton netplayOptionsButton;
        private GameObject netplayOptionsTitle;
        private CustomButton netplayOptionsBackButton;
        private GameObject sharedExpToggleSetting;
        private GameObject resumeWorldToggleSetting;
        private CustomButton worldChooseButton;
        private TextMeshProUGUI worldChooseLabel;
        private CustomButton worldDoneButton;
        private GameObject worldScreenTitle;
        private bool worldScreenOpen;
        private readonly List<System.Guid> worldChoices = new();
        private TMP_InputField worldNameInput;
        private GameObject worldNameRow;
        private GameObject worldListRoot;
        private GameObject worldScrollUp;
        private GameObject worldScrollDown;
        private int worldListOffset;
        private readonly List<string> worldTitles = new();
        private readonly List<WorldRow> worldRows = new();

        private const float WorldRowWidth = 760f;
        private const float WorldRowHeight = 56f;
        private const int VisibleWorldRows = 5;
        private static readonly Color SelectedRow = new(0.18f, 0.34f, 0.24f, 1f);
        private static readonly Color UnselectedRow = new(0.16f, 0.17f, 0.21f, 1f);

        /// <summary>One line in the world list.</summary>
        private sealed class WorldRow
        {
            public GameObject Root;
            public TextMeshProUGUI Label;
            public GameObject Delete;
            public TextMeshProUGUI DeleteText;
        }
        // Deleting a world throws away a run for everyone who played it, so the first press
        // only arms it and the second carries it out.
        private System.Guid worldPendingDelete = System.Guid.Empty;
        private readonly List<string> worldLabels = new();
        private int worldChoiceIndex;
        private GameObject scalingPanel;
        private LobbyScaling scalingDraft;
        private TextMeshProUGUI resumeWorldToggleStatusText;
        private CustomButton resumeWorldToggleLeftButton;
        private CustomButton resumeWorldToggleRightButton;
        private CustomButton sharedExpToggleLeftButton;
        private CustomButton sharedExpToggleRightButton;
        private TextMeshProUGUI sharedExpToggleStatusText;

        protected void Awake()
        {
            filter = new ProfanityFilter.ProfanityFilter();
        }

        public void SetMainMenu(MainMenu menu)
        {
            mainMenu = menu;
        }

        protected override void OnUICreated()
        {
            CreateCloseButton();
            CreatePlayerNameInput();
            CreateMatchButtons();
            CreateStopButton();
            CreateFriendliesUI();
            CreateNetplayOptionsUI();
        }

        private void CreateCloseButton()
        {
            var buttonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            buttonObj.transform.SetParent(panel.transform, false);

            var originalButton = buttonObj.GetComponent<MyButtonNormal>();
            if (originalButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalButton);
            }

            UnityEngine.UI.Button button = buttonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            var localizeStringEvent = buttonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            closeButton = buttonObj.AddComponent<CustomButton>();
            closeButton.SetOnClickAction(OnCloseClicked);

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.text = "Close";
            textWrapper.t_text.fontSize = 36;

            var rectTransform = buttonObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, -200f);
            rectTransform.sizeDelta = new Vector2(300, 70);
        }

        private void CreatePlayerNameInput()
        {
            var inputObj = new GameObject("PlayerNameInput");
            inputObj.transform.SetParent(panel.transform, false);

            var rectTransform = inputObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.7f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.7f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(300, 50);

            var image = inputObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            playerNameInput = inputObj.AddComponent<TMP_InputField>();
            playerNameInput.textComponent = CreateInputText(inputObj);
            playerNameInput.placeholder = CreatePlaceholderText(inputObj);
            playerNameInput.text = ModConfig.PlayerName.Value;
            playerNameInput.characterLimit = 20;

            playerNameInput.caretWidth = 3;
            playerNameInput.caretColor = Color.white;
            playerNameInput.customCaretColor = true;
            playerNameInput.caretBlinkRate = 0.85f;

            playerNameInput.selectionColor = new Color(0.65f, 0.8f, 1f, 0.5f);

            // Enable/disable to force caret initialization (Thanks random user on Stack exchange)
            playerNameInput.enabled = false;
            playerNameInput.enabled = true;

            label = new GameObject("Label");
            label.transform.SetParent(panel.transform, false);

            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.75f);
            labelRect.anchorMax = new Vector2(0.5f, 0.75f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0, 25);
            labelRect.sizeDelta = new Vector2(300, 40);

            var labelText = label.AddComponent<TextMeshProUGUI>();
            labelText.text = "Player Name:";
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 50;
            labelText.color = Color.white;
        }

        private void CreateSaveToggle()
        {
            var settings = mainMenu.settings.GetComponent<Settings>();
            var settingPrefab = settings.GetSettingPrefab(SettingType.Enum);

            saveToggleSetting = GameObject.Instantiate(settingPrefab, panel.transform);

            var rectTransform = saveToggleSetting.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.6f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.6f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, 25);
            rectTransform.sizeDelta = new Vector2(700, 60);

            var textComponents = Il2CppFindHelper.RuntimeGetComponentsInChildren<TextMeshProUGUI>(saveToggleSetting);
            foreach (var textComp in textComponents)
            {
                if (textComp.name.StartsWith("Text"))
                {
                    textComp.text = "Allow Saving progression\nUse at your own risk";
                    textComp.fontSize = 20;
                    textComp.enableWordWrapping = false;
                }
                else if (textComp.name.StartsWith("StatusText"))
                {
                    saveToggleStatusText = textComp;
                    saveToggleStatusText.gameObject.SetActive(true);
                    UpdateSaveToggleStatus();
                }
            }

            var buttons = Il2CppFindHelper.RuntimeGetComponentsInChildren<UnityEngine.UI.Button>(saveToggleSetting);
            foreach (var btn in buttons)
            {
                if (btn.name == "B_Left")
                {
                    var origButton = btn.GetComponent<MyButtonNormal>();
                    if (origButton != null)
                    {
                        UnityEngine.Object.DestroyImmediate(origButton);
                    }

                    btn.onClick = new();

                    saveToggleLeftButton = btn.gameObject.AddComponent<CustomButton>();
                    saveToggleLeftButton.SetOnClickAction(OnSaveToggleLeftClicked);
                }
                else if (btn.name == "B_Right")
                {
                    var origButton = btn.GetComponent<MyButtonNormal>();
                    if (origButton != null)
                    {
                        UnityEngine.Object.DestroyImmediate(origButton);
                    }

                    btn.onClick = new();

                    saveToggleRightButton = btn.gameObject.AddComponent<CustomButton>();
                    saveToggleRightButton.SetOnClickAction(OnSaveToggleRightClicked);
                }
            }

            saveToggleSetting.SetActive(false);
        }

        private void OnSaveToggleLeftClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            ToggleSaveOption(false);
        }

        private void OnSaveToggleRightClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            ToggleSaveOption(true);
        }

        private void ToggleSaveOption(bool isEnabled)
        {
            ModConfig.AllowSavesDuringNetplay.Value = isEnabled;
            ModConfig.Save();
            UpdateSaveToggleStatus();
        }

        private void UpdateSaveToggleStatus()
        {
            if (saveToggleStatusText != null)
            {
                saveToggleStatusText.text = ModConfig.AllowSavesDuringNetplay.Value ? "ON" : "OFF";
                saveToggleStatusText.color = ModConfig.AllowSavesDuringNetplay.Value ? Color.green : Color.red;
            }
        }

        private void CreateSharedExpToggle()
        {
            var settings = mainMenu.settings.GetComponent<Settings>();
            var settingPrefab = settings.GetSettingPrefab(SettingType.Enum);

            sharedExpToggleSetting = GameObject.Instantiate(settingPrefab, panel.transform);

            var rectTransform = sharedExpToggleSetting.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, -50);
            rectTransform.sizeDelta = new Vector2(700, 60);

            var textComponents = Il2CppFindHelper.RuntimeGetComponentsInChildren<TextMeshProUGUI>(sharedExpToggleSetting);
            foreach (var textComp in textComponents)
            {
                if (textComp.name.StartsWith("Text"))
                {
                    textComp.text = "Shared Experience(Experimental)\nXP/Gold/Interaction shared\nActive pause enabled";
                    textComp.fontSize = 18;
                    textComp.enableWordWrapping = false;
                }
                else if (textComp.name.StartsWith("StatusText"))
                {
                    sharedExpToggleStatusText = textComp;
                    sharedExpToggleStatusText.gameObject.SetActive(true);
                    UpdateSharedExpToggleStatus();
                }
            }

            var buttons = Il2CppFindHelper.RuntimeGetComponentsInChildren<UnityEngine.UI.Button>(sharedExpToggleSetting);
            foreach (var btn in buttons)
            {
                if (btn.name == "B_Left")
                {
                    var origButton = btn.GetComponent<MyButtonNormal>();
                    if (origButton != null)
                    {
                        UnityEngine.Object.DestroyImmediate(origButton);
                    }

                    btn.onClick = new();

                    sharedExpToggleLeftButton = btn.gameObject.AddComponent<CustomButton>();
                    sharedExpToggleLeftButton.SetOnClickAction(OnSharedExpToggleLeftClicked);
                }
                else if (btn.name == "B_Right")
                {
                    var origButton = btn.GetComponent<MyButtonNormal>();
                    if (origButton != null)
                    {
                        UnityEngine.Object.DestroyImmediate(origButton);
                    }

                    btn.onClick = new();

                    sharedExpToggleRightButton = btn.gameObject.AddComponent<CustomButton>();
                    sharedExpToggleRightButton.SetOnClickAction(OnSharedExpToggleRightClicked);
                }
            }

            sharedExpToggleSetting.SetActive(false);
        }

        private void OnSharedExpToggleLeftClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            ToggleSharedExpOption(false);
        }

        private void OnSharedExpToggleRightClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            ToggleSharedExpOption(true);
        }

        private void ToggleSharedExpOption(bool isEnabled)
        {
            ModConfig.EnabledSharedExperience.Value = isEnabled;
            ModConfig.Save();
            UpdateSharedExpToggleStatus();
        }

        private void UpdateSharedExpToggleStatus()
        {
            if (sharedExpToggleStatusText != null)
            {
                sharedExpToggleStatusText.text = ModConfig.EnabledSharedExperience.Value ? "ON" : "OFF";
                sharedExpToggleStatusText.color = ModConfig.EnabledSharedExperience.Value ? Color.green : Color.red;
            }
        }

        /// <summary>
        /// BonkLink edition, 2026-09-13: choose between carrying on the saved co-op world and
        /// starting a fresh one, without editing a configuration file. Turn it off to begin a new
        /// world with different people; the previous world stays on disk either way.
        /// </summary>
        private void CreateResumeWorldToggle()
        {
            var mainMenu = Plugin.Instance.GetMainMenu();
            var settings = mainMenu.settings.GetComponent<Settings>();
            var settingPrefab = settings.GetSettingPrefab(SettingType.Enum);

            resumeWorldToggleSetting = GameObject.Instantiate(settingPrefab, panel.transform);

            var rectTransform = resumeWorldToggleSetting.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, -120);
            rectTransform.sizeDelta = new Vector2(700, 60);

            var textComponents = Il2CppFindHelper.RuntimeGetComponentsInChildren<TextMeshProUGUI>(resumeWorldToggleSetting);
            foreach (var textComp in textComponents)
            {
                if (textComp.name.StartsWith("Text"))
                {
                    textComp.text = "Continue Saved Co-op World\nON: pick up your last run on this stage\nOFF: start a fresh world";
                    textComp.fontSize = 18;
                    textComp.enableWordWrapping = false;
                }
                else if (textComp.name.StartsWith("StatusText"))
                {
                    resumeWorldToggleStatusText = textComp;
                    resumeWorldToggleStatusText.gameObject.SetActive(true);
                    UpdateResumeWorldToggleStatus();
                }
            }

            var buttons = Il2CppFindHelper.RuntimeGetComponentsInChildren<UnityEngine.UI.Button>(resumeWorldToggleSetting);
            foreach (var btn in buttons)
            {
                if (btn.name == "B_Left")
                {
                    var origButton = btn.GetComponent<MyButtonNormal>();
                    if (origButton != null)
                    {
                        UnityEngine.Object.DestroyImmediate(origButton);
                    }

                    btn.onClick = new();

                    resumeWorldToggleLeftButton = btn.gameObject.AddComponent<CustomButton>();
                    resumeWorldToggleLeftButton.SetOnClickAction(OnResumeWorldToggleLeftClicked);
                }
                else if (btn.name == "B_Right")
                {
                    var origButton = btn.GetComponent<MyButtonNormal>();
                    if (origButton != null)
                    {
                        UnityEngine.Object.DestroyImmediate(origButton);
                    }

                    btn.onClick = new();

                    resumeWorldToggleRightButton = btn.gameObject.AddComponent<CustomButton>();
                    resumeWorldToggleRightButton.SetOnClickAction(OnResumeWorldToggleRightClicked);
                }
            }

            resumeWorldToggleSetting.SetActive(false);
        }

        private void OnResumeWorldToggleLeftClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            ToggleResumeWorldOption(false);
        }

        private void OnResumeWorldToggleRightClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            ToggleResumeWorldOption(true);
        }

        private void ToggleResumeWorldOption(bool isEnabled)
        {
            ModConfig.ResumeLastWorld.Value = isEnabled;
            ModConfig.Save();
            UpdateResumeWorldToggleStatus();
        }

        private void UpdateResumeWorldToggleStatus()
        {
            if (resumeWorldToggleStatusText != null)
            {
                resumeWorldToggleStatusText.text = ModConfig.ResumeLastWorld.Value ? "ON" : "OFF";
                resumeWorldToggleStatusText.color = ModConfig.ResumeLastWorld.Value ? Color.green : Color.red;
            }
        }

        /// <summary>
        /// BonkLink edition, 2026-09-13: choose, before hosting, whether to continue one of the
        /// saved co-op worlds or begin a new one. A new world starts everybody fresh. Continuing a
        /// world gives back the character each returning player last had on it, while anyone who
        /// has not played that world, or who picks a character they have not used there, starts
        /// fresh alongside them.
        /// </summary>
        /// <summary>
        /// The world list is its own screen rather than something laid over Friendlies.
        /// Friendlies already carries Host, the room code and Join; putting a five-row list on
        /// top of that left rows sitting behind buttons that swallowed their clicks.
        /// Friendlies now shows one button saying which world is chosen, and that opens the list.
        /// </summary>
        private void CreateWorldPicker()
        {
            worldChooseButton = CreateMenuButton("WorldChooseButton", panel.transform,
                new Vector2(0f, 150f), new Vector2(620f, 56f), 20f,
                TextAlignmentOptions.Center, () => ShowWorldScreen(true), out worldChooseLabel);
            worldChooseButton.gameObject.SetActive(false);

            CreateWorldScreen();
            CreateWorldNameInput();
            CreateWorldList();

            RefreshWorldChoices();
        }

        /// <summary>The list's own screen: a heading, the rows, a name box and a way back.</summary>
        private void CreateWorldScreen()
        {
            worldScreenTitle = new GameObject("WorldScreenTitle");
            worldScreenTitle.transform.SetParent(panel.transform, false);
            var titleRect = worldScreenTitle.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 250f);
            titleRect.sizeDelta = new Vector2(700, 60);
            var titleText = worldScreenTitle.AddComponent<TextMeshProUGUI>();
            titleText.text = "Choose a World";
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontSize = 44;
            titleText.color = Color.white;
            worldScreenTitle.SetActive(false);

            worldDoneButton = CreateMenuButton("WorldDoneButton", panel.transform,
                new Vector2(0f, -200f), new Vector2(300f, 60f), 26f,
                TextAlignmentOptions.Center, () => ShowWorldScreen(false), out var doneLabel);
            if (doneLabel != null) doneLabel.text = "Back";
            worldDoneButton.gameObject.SetActive(false);
        }

        /// <summary>
        /// Swaps between Friendlies and the world list. Everything Friendlies owns is switched
        /// off while the list is up, so nothing can sit over a row.
        /// </summary>
        private void ShowWorldScreen(bool open)
        {
            worldScreenOpen = open;

            if (open) AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);

            friendliesTitle.SetActive(!open);
            hostButton.gameObject.SetActive(!open);
            codeLabel.SetActive(!open);
            codeInput.gameObject.SetActive(!open);
            joinButton.gameObject.SetActive(!open);
            friendliesBackButton.gameObject.SetActive(!open);
            if (worldChooseButton != null) worldChooseButton.gameObject.SetActive(!open);

            if (worldScreenTitle != null) worldScreenTitle.SetActive(open);
            if (worldDoneButton != null) worldDoneButton.gameObject.SetActive(open);
            if (worldListRoot != null)
            {
                worldListRoot.SetActive(open);
                // Drawn last so nothing left on the panel covers a row.
                if (open) worldListRoot.transform.SetAsLastSibling();
            }

            if (open) RefreshWorldChoices();
            else if (worldNameRow != null) worldNameRow.SetActive(false);

            RefreshChooseButton();
        }

        /// <summary>Friendlies shows which world is chosen without opening the list.</summary>
        private void RefreshChooseButton()
        {
            // Math.Clamp throws when the lower bound exceeds the upper one, so an empty list
            // would take the menu down rather than simply showing nothing.
            if (worldChooseLabel == null || worldChoices.Count == 0 || worldTitles.Count == 0) return;
            var index = worldChoiceIndex < 0 ? 0 : worldChoiceIndex;
            if (index >= worldTitles.Count || index >= worldChoices.Count) index = 0;
            var title = worldTitles[index];
            worldChooseLabel.text = $"World: {title}";
            worldChooseLabel.color = worldChoices[index] == System.Guid.Empty
                ? new Color(0.75f, 0.92f, 1f, 1f)
                : new Color(0.62f, 1f, 0.68f, 1f);
        }

        private void CreateWorldNameInput()
        {
            worldNameRow = new GameObject("WorldNameInput");
            worldNameRow.transform.SetParent(panel.transform, false);

            var rect = worldNameRow.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(110f, -125f);
            rect.sizeDelta = new Vector2(330, 42);

            var image = worldNameRow.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            worldNameInput = worldNameRow.AddComponent<TMP_InputField>();
            worldNameInput.textComponent = CreateInputText(worldNameRow);
            worldNameInput.placeholder = CreatePlaceholderText(worldNameRow);
            worldNameInput.text = "";
            worldNameInput.characterLimit = 24;
            worldNameInput.caretWidth = 3;
            worldNameInput.caretColor = Color.white;
            worldNameInput.customCaretColor = true;
            worldNameInput.caretBlinkRate = 0.85f;
            worldNameInput.selectionColor = new Color(0.65f, 0.8f, 1f, 0.5f);
            if (worldNameInput.placeholder is TextMeshProUGUI hint) hint.text = "Name this world...";

            // Same caret trick the player name field needs.
            worldNameInput.enabled = false;
            worldNameInput.enabled = true;

            var labelObj = new GameObject("WorldNameLabel");
            labelObj.transform.SetParent(panel.transform, false);
            var labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(-185f, -125f);
            labelRect.sizeDelta = new Vector2(250, 42);
            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = "New world name";
            labelText.fontSize = 18;
            labelText.alignment = TextAlignmentOptions.Right;
            labelText.color = new Color(0.8f, 0.85f, 0.95f, 1f);
            labelObj.transform.SetParent(worldNameRow.transform, true);

            // Shown only once a world that does not exist yet is selected, on the Friendlies
            // screen. Left on, it floats over the main menu.
            worldNameRow.SetActive(false);
        }

        /// <summary>
        /// The world list: one row per saved world, the way a player expects to pick a save.
        /// Built from plain UI objects rather than a game prefab, because the prefab this menu
        /// borrows is a single-line setting row and cannot become a list.
        /// </summary>
        private void CreateWorldList()
        {
            worldListRoot = new GameObject("WorldList");
            worldListRoot.transform.SetParent(panel.transform, false);

            var rect = worldListRoot.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, 175f);
            rect.sizeDelta = new Vector2(WorldRowWidth, WorldRowHeight * VisibleWorldRows);

            for (var i = 0; i < VisibleWorldRows; i++) worldRows.Add(CreateWorldRow(i));

            // Only shown when there are more worlds than fit on screen.
            worldScrollUp = CreateMenuButton("WorldScrollUp", worldListRoot.transform,
                new Vector2(WorldRowWidth / 2f + 30f, -2f), new Vector2(48f, 40f), 15f,
                TextAlignmentOptions.Center, () => ScrollWorldList(-1), out var upLabel).gameObject;
            worldScrollDown = CreateMenuButton("WorldScrollDown", worldListRoot.transform,
                new Vector2(WorldRowWidth / 2f + 30f, -(WorldRowHeight * (VisibleWorldRows - 1)) - 2f), new Vector2(48f, 40f), 15f,
                TextAlignmentOptions.Center, () => ScrollWorldList(1), out var downLabel).gameObject;
            if (upLabel != null) upLabel.text = "UP";
            if (downLabel != null) downLabel.text = "DN";

            // Belongs to the Friendlies screen only. Left visible it covers the main menu,
            // which is exactly what it did: rows behind the menu buttons and Delete buttons
            // hanging off the side of the panel.
            worldListRoot.SetActive(false);
        }

        /// <summary>
        /// Clones one of the game's own menu buttons for anything clickable.
        ///
        /// A bare GameObject with an Image on it looks like a button and never receives a
        /// click: CustomButton derives from the game's MyButton, and it is the game's own
        /// button machinery that calls OnClick. Cloning a real one is what every working
        /// button on this screen already does.
        /// </summary>
        private CustomButton CreateMenuButton(string name, Transform parent, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment, System.Action onClick, out TextMeshProUGUI label)
        {
            var mainMenu = Plugin.Instance.GetMainMenu();
            var obj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            obj.name = name;
            obj.transform.SetParent(parent, false);

            var original = obj.GetComponent<MyButtonNormal>();
            if (original != null) UnityEngine.Object.DestroyImmediate(original);

            var innerButton = obj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (innerButton != null) innerButton.onClick = new();

            // Left in place it would overwrite whatever text is set here with a translation.
            var localize = obj.GetComponentInChildren<LocalizeStringEvent>();
            if (localize != null) UnityEngine.Object.DestroyImmediate(localize);

            var custom = obj.AddComponent<CustomButton>();
            custom.SetOnClickAction(onClick);

            // The wrapper is the normal way in, but if it is missing the clone keeps saying
            // "PLAY" for ever, which is what the Delete buttons were doing. Fall back to
            // whatever text this button actually carries.
            label = null;
            var wrapper = obj.GetComponent<ButtonTextWrapper>();
            if (wrapper != null && wrapper.t_text != null) label = wrapper.t_text;
            if (label == null)
            {
                var texts = Il2CppFindHelper.RuntimeGetComponentsInChildren<TextMeshProUGUI>(obj, true);
                foreach (var candidate in texts) { label = candidate; break; }
            }

            if (label != null)
            {
                label.fontSize = fontSize;
                label.alignment = alignment;
                label.enableWordWrapping = false;
                label.richText = true;
                label.text = "";
            }
            else
            {
                Plugin.Log.LogWarning($"Button '{name}' has no text to set; it will keep the one it was cloned with.");
            }

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return custom;
        }

        private WorldRow CreateWorldRow(int index)
        {
            const float deleteWidth = 120f;
            const float gap = 8f;
            var rowWidth = WorldRowWidth - deleteWidth - gap;

            var row = CreateMenuButton("WorldRow" + index, worldListRoot.transform,
                new Vector2(-(WorldRowWidth - rowWidth) / 2f, -index * WorldRowHeight),
                new Vector2(rowWidth, WorldRowHeight - 8f),
                17f, TextAlignmentOptions.Left, () => OnWorldRowClicked(index), out var rowLabel);

            var delete = CreateMenuButton("WorldDelete" + index, worldListRoot.transform,
                new Vector2((WorldRowWidth - deleteWidth) / 2f, -index * WorldRowHeight),
                new Vector2(deleteWidth, WorldRowHeight - 14f),
                16f, TextAlignmentOptions.Center, () => OnWorldDeleteClicked(index), out var deleteLabel);

            if (deleteLabel != null)
            {
                deleteLabel.text = "Delete";
                deleteLabel.color = new Color(1f, 0.82f, 0.82f, 1f);
            }

            return new WorldRow { Root = row.gameObject, Label = rowLabel, Delete = delete.gameObject, DeleteText = deleteLabel };
        }

        private TextMeshProUGUI CreateRowText(GameObject parent, string name, Vector2 offsetMin, Vector2 offsetMax, float size, TextAlignmentOptions alignment)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var text = obj.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.color = Color.white;
            return text;
        }

        private void ScrollWorldList(int direction)
        {
            var maximum = System.Math.Max(0, worldChoices.Count - VisibleWorldRows);
            var wanted = System.Math.Clamp(worldListOffset + direction, 0, maximum);
            if (wanted == worldListOffset) return;

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            worldListOffset = wanted;
            RefreshWorldRows();
        }

        private void OnWorldRowClicked(int rowIndex)
        {
            var choice = worldListOffset + rowIndex;
            if (choice < 0 || choice >= worldChoices.Count) return;

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            worldChoiceIndex = choice;
            worldPendingDelete = System.Guid.Empty;
            ApplyWorldChoice();
        }

        private void OnWorldDeleteClicked(int rowIndex)
        {
            var choice = worldListOffset + rowIndex;
            if (choice < 0 || choice >= worldChoices.Count) return;

            var chosen = worldChoices[choice];
            if (chosen == System.Guid.Empty) return;

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);

            // Throwing away a run everyone played deserves a second press.
            if (worldPendingDelete != chosen)
            {
                worldPendingDelete = chosen;
                RefreshWorldRows();
                return;
            }

            try { Plugin.Services.GetService<IWorldSaveService>()?.DeleteWorld(chosen); }
            catch (System.Exception ex) { Plugin.Log.LogWarning("Could not delete that co-op world: " + ex.Message); }

            worldPendingDelete = System.Guid.Empty;
            worldChoiceIndex = 0;
            worldListOffset = 0;
            RefreshWorldChoices();
        }

        /// <summary>Paints the rows for whatever part of the list is on screen.</summary>
        private void RefreshWorldRows()
        {
            if (worldRows.Count == 0) return;

            var maximum = System.Math.Max(0, worldChoices.Count - VisibleWorldRows);
            worldListOffset = System.Math.Clamp(worldListOffset, 0, maximum);

            for (var i = 0; i < worldRows.Count; i++)
            {
                var row = worldRows[i];
                var choice = worldListOffset + i;

                if (choice >= worldChoices.Count)
                {
                    row.Root.SetActive(false);
                    continue;
                }

                if (choice >= worldTitles.Count || choice >= worldLabels.Count)
                {
                    row.Root.SetActive(false);
                    continue;
                }

                row.Root.SetActive(true);
                var id = worldChoices[choice];
                var isNew = id == System.Guid.Empty;
                var chosen = choice == worldChoiceIndex;

                if (row.Label != null)
                {
                    var title = isNew ? "+  Create New World" : worldTitles[choice];
                    var detail = isNew ? "Everyone starts fresh" : worldLabels[choice];
                    var mark = chosen ? "> " : "  ";
                    row.Label.text = $"{mark}{title}\n<size=13><color=#B4BECD>   {detail}</color></size>";
                    row.Label.color = chosen ? new Color(0.62f, 1f, 0.68f, 1f)
                        : isNew ? new Color(0.75f, 0.92f, 1f, 1f) : Color.white;
                }

                row.Delete.SetActive(!isNew);
                if (!isNew && row.DeleteText != null)
                {
                    var armed = worldPendingDelete == id;
                    row.DeleteText.text = armed ? "Sure?" : "Delete";
                    row.DeleteText.color = armed ? new Color(1f, 0.45f, 0.45f, 1f) : new Color(1f, 0.82f, 0.82f, 1f);
                }
            }

            if (worldScrollUp != null) worldScrollUp.SetActive(worldChoices.Count > VisibleWorldRows);
            if (worldScrollDown != null) worldScrollDown.SetActive(worldChoices.Count > VisibleWorldRows);
        }

        /// <summary>The name the host typed for a new world, or nothing to let it be named for them.</summary>
        public string GetChosenWorldName() => worldNameInput != null ? (worldNameInput.text ?? "").Trim() : "";

        private void OnWorldPickerLeftClicked() => StepWorldChoice(-1);

        private void OnWorldPickerRightClicked() => StepWorldChoice(1);

        private void StepWorldChoice(int direction)
        {
            if (worldChoices.Count == 0) return;

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
            worldChoiceIndex = (worldChoiceIndex + direction + worldChoices.Count) % worldChoices.Count;
            ApplyWorldChoice();
        }

        /// <summary>Rebuilds the list of worlds, keeping the current choice selected if it survives.</summary>
        private void RefreshWorldChoices()
        {
            try
            {
                var service = Plugin.Services.GetService<IWorldSaveService>();
                var previous = worldChoices.Count > worldChoiceIndex ? worldChoices[worldChoiceIndex] : System.Guid.Empty;

                worldChoices.Clear();
                worldChoices.Add(System.Guid.Empty); // a new world always comes first
                worldLabels.Clear();
                worldLabels.Add("Everyone starts fresh");
                worldTitles.Clear();
                worldTitles.Add("+  Create New World");

                foreach (var world in service?.ListWorlds() ?? new List<Common.Persistence.WorldSave>())
                {
                    worldChoices.Add(world.WorldId);
                    worldTitles.Add(string.IsNullOrWhiteSpace(world.Name) ? "Co-op world" : world.Name);
                    worldLabels.Add(DescribeWorld(world));
                }

                worldChoiceIndex = System.Math.Max(0, worldChoices.IndexOf(previous));
                ApplyWorldChoice();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not list the saved co-op worlds: {ex.Message}");
            }
        }

        private string DescribeWorld(Common.Persistence.WorldSave world)
        {
            var minutes = (int)(world.ElapsedSeconds / 60);
            var played = minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
            var age = (System.DateTimeOffset.UtcNow - world.SavedAt).TotalHours;
            var when = age < 1 ? "just now" : age < 24 ? $"{(int)age}h ago" : $"{(int)(age / 24)}d ago";

            var mine = world.FindMostRecent(ModConfig.PlayerIdentity.Value ?? "");
            var you = mine != null ? $"you: {(ECharacter)mine.Character} lv{mine.Level}" : "you: new character";

            return $"Stage {world.StageIndex + 1}  -  {played} played  -  {world.Players.Count} player(s)  -  {you}  -  saved {when}";
        }

        private void ApplyWorldChoice()
        {
            if (worldChoices.Count == 0) return;

            var chosen = worldChoices[worldChoiceIndex];

            try { Plugin.Services.GetService<IWorldSaveService>().SelectedWorldId = chosen; }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Could not select a co-op world: {ex.Message}"); }

            // Naming only makes sense for a world that does not exist yet, and only while the
            // list is actually on screen. Without that second condition this switched itself
            // back on during setup and floated over the main menu.
            var isNew = chosen == System.Guid.Empty;
            if (worldNameRow != null) worldNameRow.SetActive(isNew && worldScreenOpen);

            // The name travels with the world for its whole life, so it is handed over as soon
            // as the host picks "Create New World" rather than read back later from the menu.
            try
            {
                var service = Plugin.Services.GetService<IWorldSaveService>();
                if (service != null) service.PendingWorldName = isNew ? GetChosenWorldName() : "";
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Could not set the world name: {ex.Message}"); }

            // Keep the chosen row selected and scrolled into view.
            if (worldChoiceIndex < worldListOffset) worldListOffset = worldChoiceIndex;
            else if (worldChoiceIndex >= worldListOffset + VisibleWorldRows) worldListOffset = worldChoiceIndex - VisibleWorldRows + 1;

            RefreshWorldRows();
            RefreshChooseButton();
        }

        private void CreateNetplayOptionsUI()
        {
            netplayOptionsTitle = new GameObject("NetplayOptionsTitle");
            netplayOptionsTitle.transform.SetParent(panel.transform, false);

            var titleRect = netplayOptionsTitle.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.85f);
            titleRect.anchorMax = new Vector2(0.5f, 0.85f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.sizeDelta = new Vector2(400, 60);

            var titleText = netplayOptionsTitle.AddComponent<TextMeshProUGUI>();
            titleText.text = "Netplay Options";
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontSize = 60;
            titleText.color = Color.white;

            netplayOptionsTitle.SetActive(false);

            CreateSaveToggle();
            CreateSharedExpToggle();
            CreateResumeWorldToggle();
            CreateWorldPicker();

            var backButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            backButtonObj.transform.SetParent(panel.transform, false);

            var originalBackButton = backButtonObj.GetComponent<MyButtonNormal>();
            if (originalBackButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalBackButton);
            }

            UnityEngine.UI.Button backButton = backButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (backButton != null)
            {
                backButton.onClick = new();
            }

            var localizeStringEventBack = backButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEventBack != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEventBack);
            }

            netplayOptionsBackButton = backButtonObj.AddComponent<CustomButton>();
            netplayOptionsBackButton.SetOnClickAction(OnNetplayOptionsBackClicked);

            var backTextWrapper = backButtonObj.GetComponent<ButtonTextWrapper>();
            backTextWrapper.t_text.text = "Back";
            backTextWrapper.t_text.fontSize = 36;

            var backRectTransform = backButtonObj.GetComponent<RectTransform>();
            backRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            backRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            backRectTransform.pivot = new Vector2(0.5f, 0.5f);
            backRectTransform.anchoredPosition = new Vector2(0, -200f);
            backRectTransform.sizeDelta = new Vector2(300, 70);

            netplayOptionsBackButton.gameObject.SetActive(false);
        }

        private TextMeshProUGUI CreateInputText(GameObject parent)
        {
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(parent.transform, false);

            var rectTransform = textObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Left;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.fontSize = 35;
            text.color = Color.white;
            text.margin = new Vector4(10, 0, 10, 0);

            return text;
        }

        private TextMeshProUGUI CreatePlaceholderText(GameObject parent)
        {
            var placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(parent.transform, false);

            var rectTransform = placeholderObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var text = placeholderObj.AddComponent<TextMeshProUGUI>();
            text.text = "Enter your name...";
            text.alignment = TextAlignmentOptions.Left;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.fontSize = 24;
            text.color = new Color(1f, 1f, 1f, 0.3f);
            text.margin = new Vector4(10, 0, 10, 0);

            return text;
        }

        private void CreateStopButton()
        {
            var buttonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            buttonObj.transform.SetParent(panel.transform, false);

            var originalButton = buttonObj.GetComponent<MyButtonNormal>();
            if (originalButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalButton);
            }

            UnityEngine.UI.Button button = buttonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            var localizeStringEvent = buttonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            stopButton = buttonObj.AddComponent<CustomButton>();
            stopButton.SetOnClickAction(OnStopClicked);

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.fontSize = 36;

            var rectTransform = buttonObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, -220f);
            rectTransform.sizeDelta = new Vector2(300, 70);

            stopButton.gameObject.SetActive(false);
        }

        private void CreateMatchButtons()
        {
            var randomButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            randomButtonObj.transform.SetParent(panel.transform, false);

            var originalRandomButton = randomButtonObj.GetComponent<MyButtonNormal>();
            if (originalRandomButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalRandomButton);
            }

            UnityEngine.UI.Button button = randomButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            var localizeStringEvent = randomButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            randomButton = randomButtonObj.AddComponent<CustomButton>();
            randomButton.SetOnClickAction(OnRandomClicked);

            var randomTextWrapper = randomButtonObj.GetComponent<ButtonTextWrapper>();
            randomTextWrapper.t_text.text = "Random";
            randomTextWrapper.t_text.fontSize = 36;

            var randomRectTransform = randomButtonObj.GetComponent<RectTransform>();
            randomRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            randomRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            randomRectTransform.pivot = new Vector2(0.5f, 0.5f);
            randomRectTransform.anchoredPosition = new Vector2(-180f, -100f);
            randomRectTransform.sizeDelta = new Vector2(300, 70);

            var friendliesButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            friendliesButtonObj.transform.SetParent(panel.transform, false);

            var originalFriendliesButton = friendliesButtonObj.GetComponent<MyButtonNormal>();
            if (originalFriendliesButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalFriendliesButton);
            }

            UnityEngine.UI.Button butt = friendliesButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (butt != null)
            {
                butt.onClick = new();
            }

            var localizeStringEventFriendlies = friendliesButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEventFriendlies != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEventFriendlies);
            }

            friendliesButton = friendliesButtonObj.AddComponent<CustomButton>();
            friendliesButton.SetOnClickAction(OnFriendliesClicked);

            var friendliesTextWrapper = friendliesButtonObj.GetComponent<ButtonTextWrapper>();
            friendliesTextWrapper.t_text.text = "Friendlies";
            friendliesTextWrapper.t_text.fontSize = 36;

            var friendliesRectTransform = friendliesButtonObj.GetComponent<RectTransform>();
            friendliesRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            friendliesRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            friendliesRectTransform.pivot = new Vector2(0.5f, 0.5f);
            friendliesRectTransform.anchoredPosition = new Vector2(180f, -100f);
            friendliesRectTransform.sizeDelta = new Vector2(300, 70);

            var optionsButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            optionsButtonObj.transform.SetParent(panel.transform, false);

            var originalOptionsButton = optionsButtonObj.GetComponent<MyButtonNormal>();
            if (originalOptionsButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalOptionsButton);
            }

            UnityEngine.UI.Button optBtn = optionsButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (optBtn != null)
            {
                optBtn.onClick = new();
            }

            var localizeStringEventOptions = optionsButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEventOptions != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEventOptions);
            }

            netplayOptionsButton = optionsButtonObj.AddComponent<CustomButton>();
            netplayOptionsButton.SetOnClickAction(OnNetplayOptionsClicked);

            var optionsTextWrapper = optionsButtonObj.GetComponent<ButtonTextWrapper>();
            optionsTextWrapper.t_text.text = "Netplay Options";
            optionsTextWrapper.t_text.fontSize = 30;

            var optionsRectTransform = optionsButtonObj.GetComponent<RectTransform>();
            optionsRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            optionsRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            optionsRectTransform.pivot = new Vector2(0.5f, 0.5f);
            optionsRectTransform.anchoredPosition = new Vector2(0, -10f);
            optionsRectTransform.sizeDelta = new Vector2(300, 70);
        }

        private void CreateFriendliesUI()
        {
            friendliesTitle = new GameObject("FriendliesTitle");
            friendliesTitle.transform.SetParent(panel.transform, false);

            var titleRect = friendliesTitle.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.85f);
            titleRect.anchorMax = new Vector2(0.5f, 0.85f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.sizeDelta = new Vector2(400, 60);

            var titleText = friendliesTitle.AddComponent<TextMeshProUGUI>();
            titleText.text = "Friendlies";
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontSize = 60;
            titleText.color = Color.white;

            friendliesTitle.SetActive(false);

            var hostButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            hostButtonObj.transform.SetParent(panel.transform, false);

            var originalHostButton = hostButtonObj.GetComponent<MyButtonNormal>();
            if (originalHostButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalHostButton);
            }

            UnityEngine.UI.Button button = hostButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            var localizeStringEvent = hostButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            hostButton = hostButtonObj.AddComponent<CustomButton>();
            hostButton.SetOnClickAction(OnHostClicked);

            var hostTextWrapper = hostButtonObj.GetComponent<ButtonTextWrapper>();
            hostTextWrapper.t_text.text = "Host";
            hostTextWrapper.t_text.fontSize = 36;

            var hostRectTransform = hostButtonObj.GetComponent<RectTransform>();
            hostRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            hostRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hostRectTransform.pivot = new Vector2(0.5f, 0.5f);
            hostRectTransform.anchoredPosition = new Vector2(0, 50f);
            hostRectTransform.sizeDelta = new Vector2(300, 70);

            hostButton.gameObject.SetActive(false);

            codeLabel = new GameObject("CodeLabel");
            codeLabel.transform.SetParent(panel.transform, false);

            var codeLabelRect = codeLabel.AddComponent<RectTransform>();
            codeLabelRect.anchorMin = new Vector2(0.5f, 0.4f);
            codeLabelRect.anchorMax = new Vector2(0.5f, 0.4f);
            codeLabelRect.pivot = new Vector2(0.5f, 0.5f);
            codeLabelRect.anchoredPosition = new Vector2(0, 25);
            codeLabelRect.sizeDelta = new Vector2(300, 40);

            var codeLabelText = codeLabel.AddComponent<TextMeshProUGUI>();
            codeLabelText.text = "Room Code:";
            codeLabelText.alignment = TextAlignmentOptions.Center;
            codeLabelText.fontSize = 40;
            codeLabelText.color = Color.white;

            codeLabel.SetActive(false);

            var codeInputObj = new GameObject("CodeInput");
            codeInputObj.transform.SetParent(panel.transform, false);

            var codeInputRect = codeInputObj.AddComponent<RectTransform>();
            codeInputRect.anchorMin = new Vector2(0.5f, 0.35f);
            codeInputRect.anchorMax = new Vector2(0.5f, 0.35f);
            codeInputRect.pivot = new Vector2(0.5f, 0.5f);
            codeInputRect.anchoredPosition = new Vector2(-80f, -20f);
            codeInputRect.sizeDelta = new Vector2(250, 50);

            var codeInputImage = codeInputObj.AddComponent<Image>();
            codeInputImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            codeInput = codeInputObj.AddComponent<TMP_InputField>();
            codeInput.textComponent = CreateCodeInputText(codeInputObj);
            codeInput.placeholder = CreateCodePlaceholderText(codeInputObj);
            codeInput.characterLimit = 10;

            codeInput.caretWidth = 3;
            codeInput.caretColor = Color.white;
            codeInput.customCaretColor = true;
            codeInput.caretBlinkRate = 0.85f;

            codeInput.selectionColor = new Color(0.65f, 0.8f, 1f, 0.5f);

            codeInput.enabled = false;
            codeInput.enabled = true;

            codeInput.gameObject.SetActive(false);

            var joinButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            joinButtonObj.transform.SetParent(panel.transform, false);

            var originalJoinButton = joinButtonObj.GetComponent<MyButtonNormal>();
            if (originalJoinButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalJoinButton);
            }

            UnityEngine.UI.Button butt = joinButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (butt != null)
            {
                butt.onClick = new();
            }

            var localizeStringEventJoin = joinButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEventJoin != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEventJoin);
            }

            joinButton = joinButtonObj.AddComponent<CustomButton>();
            joinButton.SetOnClickAction(OnJoinClicked);

            var joinTextWrapper = joinButtonObj.GetComponent<ButtonTextWrapper>();
            joinTextWrapper.t_text.text = "Join";
            joinTextWrapper.t_text.fontSize = 36;

            var joinRectTransform = joinButtonObj.GetComponent<RectTransform>();
            joinRectTransform.anchorMin = new Vector2(0.5f, 0.35f);
            joinRectTransform.anchorMax = new Vector2(0.5f, 0.35f);
            joinRectTransform.pivot = new Vector2(0.5f, 0.5f);
            joinRectTransform.anchoredPosition = new Vector2(140f, -20f);
            joinRectTransform.sizeDelta = new Vector2(150, 50);

            joinButton.gameObject.SetActive(false);

            var backButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            backButtonObj.transform.SetParent(panel.transform, false);

            var originalBackButton = backButtonObj.GetComponent<MyButtonNormal>();
            if (originalBackButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalBackButton);
            }

            UnityEngine.UI.Button backButton = backButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (backButton != null)
            {
                backButton.onClick = new();
            }

            var localizeStringEventBack = backButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEventBack != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEventBack);
            }

            friendliesBackButton = backButtonObj.AddComponent<CustomButton>();
            friendliesBackButton.SetOnClickAction(OnFriendliesBackClicked);

            var backTextWrapper = backButtonObj.GetComponent<ButtonTextWrapper>();
            backTextWrapper.t_text.text = "Back";
            backTextWrapper.t_text.fontSize = 36;

            var backRectTransform = backButtonObj.GetComponent<RectTransform>();
            backRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            backRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            backRectTransform.pivot = new Vector2(0.5f, 0.5f);
            backRectTransform.anchoredPosition = new Vector2(0, -200f);
            backRectTransform.sizeDelta = new Vector2(300, 70);

            friendliesBackButton.gameObject.SetActive(false);
        }

        private TextMeshProUGUI CreateCodeInputText(GameObject parent)
        {
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(parent.transform, false);

            var rectTransform = textObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.fontSize = 30;
            text.color = Color.white;
            text.margin = new Vector4(10, 0, 10, 0);

            return text;
        }

        private TextMeshProUGUI CreateCodePlaceholderText(GameObject parent)
        {
            var placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(parent.transform, false);

            var rectTransform = placeholderObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var text = placeholderObj.AddComponent<TextMeshProUGUI>();
            text.text = "Enter code...";
            text.alignment = TextAlignmentOptions.Center;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.fontSize = 22;
            text.color = new Color(1f, 1f, 1f, 0.3f);
            text.margin = new Vector4(10, 0, 10, 0);

            return text;
        }

        protected override void Update()
        {
            base.Update();

            if (playerNameInput == null) return;

            bool isFocused = playerNameInput.isFocused;

            if (wasInputFocused && !isFocused)
            {
                OnPlayerNameEndEdit();
            }
            wasInputFocused = isFocused;
        }

        private void OnPlayerNameEndEdit()
        {
            if (playerNameInput == null) return;

            string newName = playerNameInput.text;

            if (string.IsNullOrWhiteSpace(newName))
            {
                playerNameInput.text = ModConfig.PlayerName.Value;
                return;
            }

            if (filter.IsProfanity(newName))
            {
                newName = filter.CensorString(newName);
                playerNameInput.text = newName;
            }

            newName = newName.Trim();

            ModConfig.PlayerName.Value = newName;
            ModConfig.Save();
            Plugin.Log.LogInfo($"Player name updated to: {ModConfig.PlayerName.Value}");
        }

        private void OnCloseClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            CloseModal();
        }

        private void OnRandomClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            Plugin.Instance.Mode.Mode = NetworkModeType.Random;

            UpdateModalContents(false);

            ShowLoader("Connecting...");

            Plugin.Instance.NetworkHandler.HandleNetworking();
            connectionCoroutine = CoroutineRunner.Instance.Run(HandleConnectionStatus());
        }

        private void OnFriendliesClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            Plugin.Instance.Mode.Mode = NetworkModeType.Friendlies;

            UpdateModalContents(false);
            UpdateFriendliesUI(true);
        }

        private void OnHostClicked()
        {
            UpdateFriendliesUI(false);
            if (scalingPanel != null) UnityEngine.Object.DestroyImmediate(scalingPanel);
            // IL2CPP has no GameObject(string, params Type[]) constructor: calling it throws at
            // runtime, which is what stopped the Host button from doing anything at all.
            scalingPanel = new GameObject("Host scaling");
            scalingPanel.AddComponent<RectTransform>();
            scalingPanel.transform.SetParent(panel.transform, false);
            var area = scalingPanel.GetComponent<RectTransform>();
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero; area.offsetMax = Vector2.zero;
            var source = Plugin.Instance.Mode.Scaling;
            var chosen = Plugin.Services.GetRequiredService<IWorldSaveService>().SelectedWorldId;
            foreach (var world in Plugin.Services.GetRequiredService<IWorldSaveService>().ListWorlds())
                if (world.WorldId == chosen) source = world.Scaling;
            scalingDraft = new LobbyScaling { EnemyHealthPerPlayer = source.EnemyHealthPerPlayer,
                BossHealthPerPlayer = source.BossHealthPerPlayer, SpawnsPerPlayer = source.SpawnsPerPlayer, EnemyCap = source.EnemyCap };
            CreateScalingRow("Mob HP per extra player", 125, () => scalingDraft.EnemyHealthPerPlayer,
                v => scalingDraft.EnemyHealthPerPlayer = v, false);
            CreateScalingRow("Boss HP per extra player", 65, () => scalingDraft.BossHealthPerPlayer,
                v => scalingDraft.BossHealthPerPlayer = v, false);
            CreateScalingRow("Extra mobs per extra player", 5, () => scalingDraft.SpawnsPerPlayer,
                v => scalingDraft.SpawnsPerPlayer = v, false);
            CreateScalingRow("Maximum active mobs", -55, () => scalingDraft.EnemyCap,
                v => scalingDraft.EnemyCap = (int)v, true);
            CreateScalingButton("Create lobby", -135, () =>
            {
                Plugin.Instance.Mode.Scaling = scalingDraft;
                Plugin.Instance.Mode.ScalingChosenByHost = true;
                scalingPanel.SetActive(false);
                StartConfiguredHost();
            });
            CreateScalingButton("Back", -215, () => { scalingPanel.SetActive(false); UpdateFriendliesUI(true); });
        }

        private void CreateScalingRow(string caption, float y, System.Func<float> read, System.Action<float> write, bool cap)
        {
            var prefab = mainMenu.settings.GetComponent<Settings>().GetSettingPrefab(SettingType.Enum);
            var row = GameObject.Instantiate(prefab, scalingPanel.transform);
            row.SetActive(true);
            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(760, 55);
            foreach (var localized in Il2CppFindHelper.RuntimeGetComponentsInChildren<LocalizeStringEvent>(row))
                UnityEngine.Object.DestroyImmediate(localized);
            TextMeshProUGUI status = null;
            foreach (var text in Il2CppFindHelper.RuntimeGetComponentsInChildren<TextMeshProUGUI>(row))
            {
                text.fontSize = 20; text.enableWordWrapping = false;
                if (text.name.StartsWith("StatusText")) { status = text; text.gameObject.SetActive(true); }
                else if (text.name.StartsWith("Text")) text.text = caption;
            }
            void Refresh() { if (status != null) status.text = cap ? $"{read():0}" : $"+{read() * 100:0}%"; }
            foreach (var button in Il2CppFindHelper.RuntimeGetComponentsInChildren<UnityEngine.UI.Button>(row))
            {
                var step = button.name == "B_Left" ? -1 : 1;
                // Runtime helper: IL2CPP has no generic GetComponents<T>() and it throws here.
                foreach (var old in button.RuntimeGetComponents<MyButton>()) UnityEngine.Object.DestroyImmediate(old);
                button.onClick = new();
                button.gameObject.AddComponent<CustomButton>().SetOnClickAction(() =>
                {
                    write(cap ? Mathf.Clamp(read() + step * 100, 100, 2500) : LobbyScaling.StepRate(read(), step));
                    Refresh();
                });
            }
            Refresh();
        }

        private void CreateScalingButton(string caption, float y, System.Action action)
        {
            var button = GameObject.Instantiate(mainMenu.btnPlay.gameObject, scalingPanel.transform);
            foreach (var old in Il2CppFindHelper.RuntimeGetComponentsInChildren<MyButton>(button)) UnityEngine.Object.DestroyImmediate(old);
            foreach (var localized in Il2CppFindHelper.RuntimeGetComponentsInChildren<LocalizeStringEvent>(button)) UnityEngine.Object.DestroyImmediate(localized);
            foreach (var ui in Il2CppFindHelper.RuntimeGetComponentsInChildren<UnityEngine.UI.Button>(button)) ui.onClick = new();
            button.AddComponent<CustomButton>().SetOnClickAction(action);
            var text = button.GetComponent<ButtonTextWrapper>().t_text;
            text.text = caption; text.fontSize = 32;
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(330, 65);
            button.SetActive(true);
        }

        private void StartConfiguredHost()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            Plugin.Instance.Mode.Mode = NetworkModeType.Friendlies;
            Plugin.Instance.Mode.Role = Role.Host;

            UpdateFriendliesUI(false);

            ShowLoader("Connecting...");

            Plugin.Instance.NetworkHandler.HandleNetworking();
            connectionCoroutine = CoroutineRunner.Instance.Run(HandleFriendlies());
        }

        private void OnJoinClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            var code = codeInput.text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatusText("Please enter a room code");
                return;
            }

            Plugin.Instance.Mode.Mode = NetworkModeType.Friendlies;
            Plugin.Instance.Mode.Role = Role.Client;
            Plugin.Instance.Mode.RoomCode = code;

            UpdateFriendliesUI(false);

            ShowLoader("Joining room...");

            Plugin.Instance.NetworkHandler.HandleNetworking();
            connectionCoroutine = CoroutineRunner.Instance.Run(HandleFriendlies());
        }

        private void OnFriendliesBackClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            UpdateFriendliesUI(false);
            UpdateModalContents(true);
        }

        private void OnNetplayOptionsClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            UpdateModalContents(false);
            UpdateNetplayOptionsUI(true);
        }

        private void OnNetplayOptionsBackClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            UpdateNetplayOptionsUI(false);
            UpdateModalContents(true);
        }

        private void OnStopClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            if (connectionCoroutine != null)
            {
                CoroutineRunner.Instance.StopCoroutine(connectionCoroutine);
                connectionCoroutine = null;
            }

            Plugin.Instance.NetworkHandler.ResetNetworking();

            HideLoader();

            UpdateModalContents(true);
            stopButton.gameObject.SetActive(false);

            var closeTextWrapper = closeButton.gameObject.GetComponent<ButtonTextWrapper>();
            closeTextWrapper.t_text.text = "Close";
            SetStatusText("");
        }

        private void UpdateModalContents(bool isVisible)
        {
            randomButton.gameObject.SetActive(isVisible);
            friendliesButton.gameObject.SetActive(isVisible);
            netplayOptionsButton.gameObject.SetActive(isVisible);
            closeButton.gameObject.SetActive(isVisible);
            playerNameInput.gameObject.SetActive(isVisible);
            label.SetActive(isVisible);
        }

        private void UpdateFriendliesUI(bool isVisible)
        {
            friendliesTitle.SetActive(isVisible);
            hostButton.gameObject.SetActive(isVisible);
            codeLabel.SetActive(isVisible);
            codeInput.gameObject.SetActive(isVisible);
            joinButton.gameObject.SetActive(isVisible);
            friendliesBackButton.gameObject.SetActive(isVisible);

            // The world list is a screen of its own, reached from the button below.
            if (worldChooseButton != null) worldChooseButton.gameObject.SetActive(isVisible);
            if (worldScreenTitle != null) worldScreenTitle.SetActive(false);
            if (worldDoneButton != null) worldDoneButton.gameObject.SetActive(false);
            if (worldListRoot != null) worldListRoot.SetActive(false);
            if (worldNameRow != null) worldNameRow.SetActive(false);
            worldScreenOpen = false;

            if (isVisible)
            {
                RefreshWorldChoices();
                RefreshChooseButton();
            }
        }

        private void UpdateNetplayOptionsUI(bool isVisible)
        {
            netplayOptionsTitle.SetActive(isVisible);
            saveToggleSetting.SetActive(isVisible);
            sharedExpToggleSetting.SetActive(isVisible);
            resumeWorldToggleSetting.SetActive(isVisible);
            netplayOptionsBackButton.gameObject.SetActive(isVisible);
        }

        private IEnumerator HandleConnectionStatus()
        {
            stopButton.gameObject.SetActive(true);
            var stopTextWrapper = stopButton.gameObject.GetComponent<ButtonTextWrapper>();
            stopTextWrapper.t_text.text = "Stop";

            float timeout = 30f;
            float elapsed = 0f;

            while (elapsed < timeout && !Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.HasValue)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (!Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.Value)
            {
                HideLoader();
                SetStatusText($"Failed to connect to server : {Plugin.Instance.NetworkHandler.MatchMakerFailureMessage}");
                Plugin.Instance.NetworkHandler.ResetNetworking();

                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateModalContents(true);
                SetStatusText("");

                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            var sharedExpStatus = ModConfig.EnabledSharedExperience.Value
                ? "<color=green>ON</color>"
                : "<color=red>OFF</color>";
            SetStatusText($"Waiting for a match... \n (You can only match people with shared experience {sharedExpStatus})");

            while (!Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue)
            {
                if (Plugin.Instance.NetworkHandler.IsNetworkInterruptedStatus)
                {
                    HideLoader();
                    SetStatusText($"Network interrupted. Please try again : {Plugin.Instance.NetworkHandler.MatchMakerFailureMessage}");
                    Plugin.Instance.NetworkHandler.ResetNetworking();

                    stopButton.gameObject.SetActive(false);

                    yield return new WaitForSeconds(3f);
                    UpdateModalContents(true);
                    SetStatusText("");
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }

            if (!Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue || !Plugin.Instance.NetworkHandler.HasFoundMatch.Value)
            {
                HideLoader();
                SetStatusText($"Failed to connect: {Plugin.Instance.NetworkHandler.MatchMakerFailureMessage}");
                Plugin.Instance.NetworkHandler.ResetNetworking();
                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateModalContents(true);
                SetStatusText("");
                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.purchaseSfx.sounds[0]);
            HideLoader();
            SetStatusText("Match found!");
            stopButton.gameObject.SetActive(false);

            mainMenu.GoToCharacterSelection();

            var characterMenu = WindowManager.activeWindow?.GetComponent<CharacterMenu>();
            if (characterMenu != null)
            {
                characterMenu.selectedButton = characterMenu.characterButtons[0];
                characterMenu.b_confirm.SetInteractable(false);
            }

            var role = Plugin.Instance.NetworkHandler.IsHost ? "Host" : "Client";
            var lobbySize = Plugin.Instance.NetworkHandler.GetLobbySize();
            Plugin.StartNotification(("MegabonkTogether", "MatchSuccess"), ("MegabonkTogether", "MatchSuccessDesc"), [role, lobbySize.ToString()]);

            yield return new WaitForSeconds(1f);
            CloseModal();
        }

        private IEnumerator HandleFriendlies()
        {
            stopButton.gameObject.SetActive(true);
            var stopTextWrapper = stopButton.gameObject.GetComponent<ButtonTextWrapper>();
            stopTextWrapper.t_text.text = "Stop";

            float timeout = 30f;
            float elapsed = 0f;

            while (elapsed < timeout && !Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.HasValue)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (!Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.Value)
            {
                HideLoader();
                SetStatusText($"Failed to connect to server : {Plugin.Instance.NetworkHandler.MatchMakerFailureMessage}");
                Plugin.Instance.NetworkHandler.ResetNetworking();

                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateFriendliesUI(true);
                SetStatusText("");

                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            elapsed = 0f;

            while (elapsed < timeout && !Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue)
            {
                if (Plugin.Instance.NetworkHandler.IsNetworkInterruptedStatus)
                {
                    HideLoader();
                    SetStatusText($"Network interrupted. Please try again : {Plugin.Instance.NetworkHandler.MatchMakerFailureMessage}");
                    Plugin.Instance.NetworkHandler.ResetNetworking();

                    stopButton.gameObject.SetActive(false);

                    yield return new WaitForSeconds(3f);
                    UpdateFriendliesUI(true);
                    SetStatusText("");
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (!Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue || !Plugin.Instance.NetworkHandler.HasFoundMatch.Value)
            {
                HideLoader();
                SetStatusText($"Failed to connect: {Plugin.Instance.NetworkHandler.MatchMakerFailureMessage}");
                Plugin.Instance.NetworkHandler.ResetNetworking();
                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateFriendliesUI(true);
                SetStatusText("");
                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.purchaseSfx.sounds[0]);
            HideLoader();
            SetStatusText("Joined!");
            stopButton.gameObject.SetActive(false);

            mainMenu.GoToCharacterSelection();

            var characterMenu = WindowManager.activeWindow?.GetComponent<CharacterMenu>();
            if (characterMenu != null)
            {
                characterMenu.selectedButton = characterMenu.characterButtons[0];
                characterMenu.b_confirm.SetInteractable(false);
            }

            if (Plugin.Instance.NetworkHandler.IsHost)
            {
                Plugin.StartNotification(("MegabonkTogether", "FriendliesHostSuccess"), ("MegabonkTogether", "FriendliesHostSuccessDesc"), []);
            }
            else
            {
                Plugin.StartNotification(("MegabonkTogether", "FriendliesClientSuccess"), ("MegabonkTogether", "FriendliesClientSuccessDesc"), [""]);
            }

            yield return new WaitForSeconds(1f);
            CloseModal();
        }
    }
}
