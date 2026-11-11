using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PSAI
{
    internal sealed partial class GameAdapter
    {
        private StoreClientInstance? firearmClient;
        private string firearmTarget = "";
        private long nextFirearmTarget;

        private void ClearFirearm()
        {
            firearmClient = null;
            firearmTarget = "";
            nextFirearmTarget = 0;
        }

        private void CaptureFirearm(JObject snapshot, JArray screens, JArray available)
        {
            var store = PlayerStore.Instance;
            var ui = StoreUIManager.Instance;
            var current = store.currentClientInstance;
            var gun = FindCached(ref cachedGun);
            var ready = !IsBusy(store) && current != null && store.isClientArrived;
            var canRaise = ready && !ui.isShootingMode && gun != null && gun.isActiveAndEnabled && gun.CanPickup();
            var canShoot = ready && ui.isShootingMode && ui.CanShoot();
            var canLower = !IsBusy(store) && ui.isShootingMode &&
                (ui.CanShoot() || (ui.shootingUI != null && ui.shootingUI.activeInHierarchy));
            snapshot["firearm"] = new JObject {
                ["target"] = current == null ? null : firearmTarget, ["raised"] = ui.isShootingMode,
                ["can_raise"] = canRaise, ["can_shoot"] = canShoot, ["can_lower"] = canLower,
                ["shot_registered"] = ui.isShot, ["resolving_shootout"] = store.isHandlingShootout,
                ["prompt"] = gun == null ? null : gun.GetTooltip()
            };
            AddScreen("shooting_confirmation", ui.confirmPanelUIManager?.panel, screens, available);
            if (canRaise) AddAction(available, "raise_shotgun", "Raise the shotgun using the normal interaction", () => {
                if (ui.isShootingMode || gun == null || !gun.isActiveAndEnabled || !gun.CanPickup())
                    throw new InvalidOperationException("shotgun_not_available");
                gun.OnClick();
            });
            if (canLower) AddAction(available, "lower_shotgun", "Leave the normal shooting view", () => {
                if (!ui.isShootingMode || (!ui.CanShoot() && (ui.shootingUI == null || !ui.shootingUI.activeInHierarchy)))
                    throw new InvalidOperationException("shotgun_cannot_lower");
                ui.ExitShootingMode();
            });
            if (canShoot)
            {
                var expectedClient = current;
                var expectedTarget = firearmTarget;
                available.Add(new JObject { ["action"] = "shoot_customer", ["label"] = "Fire at the current customer using the normal game action",
                    ["enabled"] = true, ["parameters"] = new JArray("target"), ["target"] = expectedTarget });
                parameterActions["shoot_customer"] = args => {
                    if (args.Value<string>("target") != expectedTarget || !ReferenceEquals(store.currentClientInstance, expectedClient))
                        throw new InvalidOperationException("firearm_target_changed");
                    if (!ui.isShootingMode || !store.isClientArrived || !ui.CanShoot())
                        throw new InvalidOperationException("cannot_shoot");
                    ui.Shoot();
                };
            }
        }
    }
}
