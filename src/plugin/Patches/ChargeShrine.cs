using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(ChargeShrine))]
    internal static class ChargeShrinePatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// Keeps a shrine charging at its normal speed while the world is slowed for a choice.
        ///
        /// Charging advances on game time, and a choice puts the world at a quarter speed -- so
        /// standing in a shrine took four times as long for no reason the player could see. The
        /// slow-motion is there to give somebody time to read three upgrades, not to make
        /// everything else in the world take longer.
        ///
        /// Written as a top-up rather than by replacing the shrine's own timing: the shortfall
        /// between what a real second should have added and what the slowed second actually did.
        /// It only ever applies while the shrine is already advancing, so a shrine nobody is
        /// standing in is never pushed along by it.
        ///
        /// The same postfix also makes a shrine fill faster the more of the party stand in it.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ChargeShrine.Update))]
        public static void Update_Prefix(ChargeShrine __instance, out float __state)
        {
            __state = __instance.currentChargeTime;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ChargeShrine.Update))]
        public static void Update_Postfix(ChargeShrine __instance, float __state)
        {
            try
            {
                var advanced = __instance.currentChargeTime - __state;
                if (advanced <= 0f) return;   // nobody is in it; leave it alone

                // A second of slow-motion must still charge a second's worth. The slowdown is
                // there to give somebody time to read three upgrades, not to make everything
                // else in the world take four times as long.
                if (ChoiceSlowMotion.IsSlowing)
                {
                    var real = UnityEngine.Time.unscaledDeltaTime;
                    if (advanced < real)
                    {
                        __instance.currentChargeTime = __state + real;
                        advanced = real;
                    }
                }

                if (!synchronizationService.HasNetplaySessionStarted()) return;

                // Everyone standing in it pulls their weight: two players charge it twice as
                // fast, three three times. A shrine is the one thing in the game that asks the
                // party to stand still together, so it should be worth doing together.
                var shrineNetplayId = DynamicData.For(__instance.gameObject).Get<uint?>("netplayId");
                if (!shrineNetplayId.HasValue) return;

                var chargers = synchronizationService.CountPlayersChargingShrine(shrineNetplayId.Value);
                if (chargers <= 1) return;

                __instance.currentChargeTime += advanced * (chargers - 1);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not keep a shrine charging at normal speed: {ex.Message}");
            }
        }

        /// <summary>
        /// Synchronize starting to charge shrine.
        /// The server check and notify other clients if they need to start (Nothing happens if already charging)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ChargeShrine.OnTriggerEnter))]
        public static bool OnTriggerEnter_Prefix(ChargeShrine __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (!Plugin.CAN_SEND_MESSAGES)
            {
                return true;
            }

            var shrineNetplayId = DynamicData.For(__instance.gameObject).Get<uint?>("netplayId");

            if (shrineNetplayId.HasValue)
            {
                return synchronizationService.OnStartingToChargingShrine(shrineNetplayId.Value);
            }
            else
            {
                Plugin.Log.LogWarning("Charge shrine has no netplay id set!");
            }

            return true;
        }

        /// <summary>
        /// Synchronize stopping to charge shrine.
        /// The server check and notify other clients if they need to stop (Nothing happens if a player is still charging)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ChargeShrine.OnTriggerExit))]
        public static bool OnTriggerExit_Prefix(ChargeShrine __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }
            if (!Plugin.CAN_SEND_MESSAGES)
            {
                return true;
            }
            var shrineNetplayId = DynamicData.For(__instance.gameObject).Get<uint?>("netplayId");
            if (shrineNetplayId.HasValue)
            {
                return synchronizationService.OnStoppingChargingShrine(shrineNetplayId.Value);
            }
            else
            {
                Plugin.Log.LogWarning("Charge shrine has no netplay id set!");
            }
            return true;
        }
    }
}
