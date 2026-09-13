using Assets.Scripts.Utility;
using HarmonyLib;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Button;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization.Components;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(PauseUi))]
    internal static class PauseUiPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        private const string SaveButtonName = "BonkLinkSaveWorldButton";
        private static ButtonTextWrapper saveButtonText;

        /// <summary>
        /// Prevent pause (shared experience or not) on pausing screen
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PauseUi.OnEnable))]
        public static void OnEnable_Postfix(PauseUi __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            MyTime.Unpause();

            // BonkLink edition, 2026-09-13: the host can checkpoint the world by hand from here,
            // rather than trusting the next autosave to land before they stop playing.
            TryAddSaveButton(__instance);
        }

        private static void TryAddSaveButton(PauseUi pauseUi)
        {
            try
            {
                var isHost = synchronizationService.IsServerMode() ?? false;
                if (!isHost) return;

                var existing = Il2CppFindHelper.RuntimeGetComponentsInChildren<CustomButton>(pauseUi)
                    .FirstOrDefault(b => b != null && b.gameObject.name == SaveButtonName);

                if (existing != null)
                {
                    // Reopening the menu should offer a fresh save rather than last time's result.
                    SetSaveButtonLabel("Save Co-op World");
                    return;
                }

                var template = Il2CppFindHelper.RuntimeGetComponentsInChildren<MyButton>(pauseUi)
                    .FirstOrDefault(b => b != null
                        && b.gameObject.name != SaveButtonName
                        && b.gameObject.GetComponent<ButtonTextWrapper>() != null);

                if (template == null)
                {
                    Plugin.Log.LogWarning("Pause menu has no button to model the co-op save button on.");
                    return;
                }

                var saveObject = GameObject.Instantiate(template.gameObject);
                saveObject.name = SaveButtonName;
                saveObject.transform.SetParent(template.transform.parent, false);
                saveObject.transform.SetAsLastSibling();
                saveObject.SetActive(true);

                // Every button behaviour the clone inherited has to go. Removing only the
                // MyButtonNormal would leave any other MyButton subclass wired up, and a copy of
                // the exit button that still exits the game is worse than no button at all.
                foreach (var inherited in saveObject.RuntimeGetComponents<MyButton>())
                {
                    Object.DestroyImmediate(inherited);
                }

                var unityButton = saveObject.GetComponentInChildren<UnityEngine.UI.Button>();
                if (unityButton != null) unityButton.onClick = new();

                // The cloned label is localized; ours is not, so the localizer has to go or it
                // overwrites the text on the next language refresh.
                foreach (var localizer in saveObject.RuntimeGetComponentsInChildren<LocalizeStringEvent>(true))
                {
                    Object.DestroyImmediate(localizer);
                }

                var custom = saveObject.AddComponent<CustomButton>();
                custom.SetOnClickAction(OnSaveWorldClicked);

                saveButtonText = saveObject.GetComponent<ButtonTextWrapper>();
                SetSaveButtonLabel("Save Co-op World");

                custom.state = MyButton.EButtonState.Active;
                custom.RefreshState();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not add the co-op save button to the pause menu: {ex.Message}");
            }
        }

        private static void OnSaveWorldClicked()
        {
            try
            {
                AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            }
            catch { /* the click still has to do its job without a sound */ }

            try
            {
                var saved = Plugin.Services.GetService<IWorldSaveService>()?.SaveNow("pause menu") ?? false;
                SetSaveButtonLabel(saved ? "Co-op World Saved" : "Save Unavailable");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Saving the co-op world from the pause menu failed: {ex}");
                SetSaveButtonLabel("Save Failed");
            }
        }

        private static void SetSaveButtonLabel(string text)
        {
            if (saveButtonText != null && saveButtonText.t_text != null)
            {
                saveButtonText.t_text.text = text;
            }
        }
    }
}
