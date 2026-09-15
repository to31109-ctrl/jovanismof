using Assets.Scripts.Inventory__Items__Pickups.Chests;
using Assets.Scripts.Inventory__Items__Pickups.Interactables;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(DetectInteractables))]
    public static class DetectInteractablesPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// Send interaction to other players
        /// </summary>

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DetectInteractables.TryInteract))]
        public static bool TryInteract_Prefix(DetectInteractables __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (!Plugin.CAN_SEND_MESSAGES) //Prevent sending messages on received events
            {
                return true;
            }

            if (__instance.currentInteractable == null)
            {
                return true;
            }

            if (!CanSynchronize(__instance))
            {
                return true;
            }

            // This player pressed the key, so whatever it costs is theirs to pay even if
            // somebody else's replay is being shielded at the same moment. Long enough to cover
            // a chest window that charges when it closes rather than when it opens.
            Patches.ChestPurchases.OwnPurchaseUntil = UnityEngine.Time.unscaledTime + 20f;

            synchronizationService.OnInteractableUsed(__instance.currentInteractable);

            var isHost = synchronizationService.IsServerMode() ?? false;
            if (!isHost)
            {
                return CanSimulateClientSide(__instance.currentInteractable);
            }

            return true;
        }

        private static bool CanSimulateClientSide(BaseInteractable interactable)
        {
            var challengeShrine = interactable.GetComponentInChildren<InteractableShrineChallenge>();
            if (challengeShrine != null)
            {
                challengeShrine.done = true;
                challengeShrine.fx.SetActive(true);
                GameObject.Destroy(challengeShrine.alertIcon);
                return false;
            }

            var egg = interactable.GetComponentInChildren<InteractableEgg>();
            if (egg != null)
            {
                egg.done = true;
                egg.breakFx.SetActive(true);
                interactable.gameObject.SetActive(false);
                return false;
            }

            return true;

        }

        private static bool CanSynchronize(DetectInteractables __instance)
        {
            // The mod makes a player briefly untouchable around a level-up, and it does that
            // with the game's own teleporting flag -- which also switches interaction off. So
            // for several seconds after every level-up, pressing the interact key on a chest, a
            // shrine, an egg or a microwave did nothing at all, with no explanation. In a run
            // where players reach level sixty that is most of the run: one player's log carried
            // a hundred and eighty refusals.
            //
            // Worse, if whatever was meant to clear that flag never got to -- which is the exact
            // shape of several faults found in this mod -- the player could not interact with
            // anything again for the rest of the session.
            //
            // Portals already had a workaround doing precisely this, written by somebody who had
            // clearly hit it. It belongs on every interaction, not one kind: pressing the key is
            // the player saying they would rather use the thing than keep the protection.
            if (Plugin.Instance.IS_MANUAL_INVINCIBLE)
            {
                Plugin.Instance.IS_MANUAL_INVINCIBLE = false;
                var self = GameManager.Instance?.player;
                if (self != null) self.isTeleporting = false;
            }

            if (!__instance.CanInteract() || !__instance.currentInteractable.CanInteract())
            {
                Plugin.Log.LogWarning($"Cant interact with {__instance?.currentInteractable}");
                return false;
            }

            var microwave = __instance.currentInteractable.GetComponentInChildren<InteractableMicrowave>();
            if (microwave != null)
            {
                if (microwave.GetPrice() > GameManager.Instance.player.inventory.gold)
                {
                    Plugin.Log.LogDebug($"Not enough gold to interact with microwave! Required: {microwave.GetPrice()}, Current: {GameManager.Instance.player.inventory.gold}");
                    return false;
                }

                if (microwave.hasItem)
                {
                    Plugin.Log.LogDebug($"Microwave already has an item!");
                    return true;
                }

                var uniqueItemsInRarity = GameManager.Instance.player.inventory.itemInventory.GetUniqueItemsInRarity(microwave.rarity);
                if (uniqueItemsInRarity < 2)
                {
                    Plugin.Log.LogDebug($"Not enough items of rarity {microwave.rarity} to interact with microwave");
                    return false;
                }
            }

            var chest = __instance.currentInteractable.GetComponentInChildren<InteractableChest>();
            if (chest != null)
            {
                if (!chest.CanAfford())
                {
                    return false;
                }
            }

            var portal = __instance.currentInteractable.GetComponentInChildren<InteractablePortal>();
            if (portal != null)
            {
                if (Plugin.Instance.IS_MANUAL_INVINCIBLE)
                {
                    Plugin.Instance.IS_MANUAL_INVINCIBLE = false;
                    GameManager.Instance.player.isTeleporting = false;
                    return true;
                }

                if (GameManager.Instance.player.isTeleporting)
                {
                    return false;
                }
            }

            var finalPortal = __instance.currentInteractable.GetComponentInChildren<InteractableBossSpawnerFinal>();
            if (finalPortal != null)
            {
                if (Plugin.Instance.IS_MANUAL_INVINCIBLE)
                {
                    Plugin.Instance.IS_MANUAL_INVINCIBLE = false;
                    GameManager.Instance.player.isTeleporting = false;
                    return true;
                }

                if (GameManager.Instance.player.isTeleporting)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
