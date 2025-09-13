using Facepunch;
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.ArmoredTrainExtensionMethods;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using CompanionServer.Handlers;
using Time = UnityEngine.Time;
using static TrainEngine;
using static ProtoBuf.PatternFirework;
using static BaseCombatEntity;

namespace Oxide.Plugins
{
    [Info("ArmoredTrain", "Adem", "1.8.2")]
    class ArmoredTrain : RustPlugin
    {
        #region Variables
        const bool en = true;
        static ArmoredTrain ins;
        EventController eventController;
        [PluginReference] Plugin NpcSpawn, PveMode, GUIAnnouncements, Notify, DiscordMessages, DynamicPVP, Economics, ServerRewards, IQEconomic, TrainHomes, AlphaLoot;
        HashSet<string> subscribeMethods = new HashSet<string>
        {
            "OnEntitySpawned",
            "OnEntityTakeDamage",
            "OnEntityDeath",
            "OnEntityEnter",
            "CanHelicopterTarget",
            "CanBradleyApcTarget",
            "OnTrainCarUncouple",
            "CanTrainCarCouple",
            "CanPickupEntity",
            "CanMountEntity",
            "OnSwitchToggle",
            "OnSwitchToggled",
            "OnCounterModeToggle",
            "OnCounterTargetChange",
            "CanLootEntity",
            "OnSamSiteModeToggle",
            "OnTurretAuthorize",
            "CanHackCrate",
            "OnLootEntity",
            "OnLootEntityEnd",
            "OnCorpsePopulate",

            "OnCustomNpcTarget",
            "CanEntityBeTargeted",
            "CanEntityTakeDamage",
            "CanPopulateLoot",
            "OnCustomLootContainer",
            "OnCustomLootNPC",
            "OnCreateDynamicPVP",
            "SetOwnerPveMode",
            "ClearOwnerPveMode",
            "CanBradleySpawnNpc",
            "CanHelicopterSpawnNpc"
        };
        #endregion Variables

        #region ExternalAPI
        bool IsArmoredTrainActive()
        {
            return EventLauncher.IsEventActive();
        }

        bool StopArmoredTrain()
        {
            if (!EventLauncher.IsEventActive())
                return false;

            //eventController.StopMoving();
            return true;
        }

        bool StartArmoredTrainEvent()
        {
            if (EventLauncher.IsEventActive())
                return false;

            EventLauncher.DelayStartEvent();
            return true;
        }

        bool EndArmoredTrainEvent()
        {
            if (EventLauncher.IsEventActive())
                return false;

            EventLauncher.StopEvent();
            return true;
        }

        Vector3 ArmoredTrainLocomotivePosition()
        {
            if (!EventLauncher.IsEventActive())
                return Vector3.zero;

            return eventController.GetEventPosition();
        }

        bool IsTrainBradley(ulong netID)
        {
            return EventLauncher.IsEventActive() && eventController.IsTrainBradley(netID);
        }

        bool IsTrainHeli(ulong netID)
        {
            if (!EventLauncher.IsEventActive())
                return false;

            return EventHeli.GetEventHeliByNetId(netID) != null;
        }

        bool IsTrainCrate(ulong netID)
        {
            return EventLauncher.IsEventActive() && LootManager.GetContainerDataByNetId(netID) != null;
        }

        bool IsTrainSamSite(ulong netID)
        {
            return EventLauncher.IsEventActive() && eventController.IsTrainSamSite(netID);
        }

        bool IsTrainWagon(ulong netID)
        {
            return EventLauncher.IsEventActive() && eventController.IsTrainWagon(netID);
        }

        bool IsTrainTurret(ulong netID)
        {
            return EventLauncher.IsEventActive() && eventController.IsTrainTurret(netID);
        }
        #endregion ExternalAPI

        #region Hooks
        void Init()
        {
            Unsubscribes();
        }

        void OnServerInitialized()
        {
            ins = this;

            if (_config == null)
                LoadDefaultConfig();

            if (!NpcSpawnManager.IsNpcSpawnReady())
                return;

            LoadDefaultMessages();
            UpdateConfig();
            UpdateLootTables();

            GuiManager.LoadImages();
            WagonCustomizator.LoadCurrentCustomizationProfile();
            LootManager.InitialLootManagerUpdate();
            EventLauncher.AutoStartEvent();
        }

        void Unload()
        {
            EventLauncher.StopEvent(true);
            ins = null;
        }

        void OnEntitySpawned(HelicopterDebris entity)
        {
            if (!entity.IsExists() || entity.ShortPrefabName != "servergibs_bradley")
                return;

            if (Vector3.Distance(eventController.GetEventPosition(), entity.transform.position) < eventController.eventConfig.zoneRadius)
                entity.Kill();
        }

        void OnEntitySpawned(LockedByEntCrate entity)
        {
            if (!entity.IsExists())
                return;

            if (entity.ShortPrefabName == "heli_crate")
                LootManager.OnHeliCrateSpawned(entity);
        }

        object OnEntityTakeDamage(TrainCar trainCar, HitInfo info)
        {
            if (trainCar == null || trainCar.net == null || info == null)
                return null;

            if (eventController.IsTrainWagon(trainCar.net.ID.Value))
            {
                if (info.InitiatorPlayer.IsRealPlayer())
                    eventController.OnTrainAttacked(info.InitiatorPlayer);

                return true;
            }

            return null;
        }

        object OnEntityTakeDamage(PatrolHelicopter patrolHelicopter, HitInfo info)
        {
            if (patrolHelicopter == null || patrolHelicopter.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return null;

            EventHeli eventHeli = EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value);

            if (eventHeli == null)
                return null;

            if (!eventController.IsPlayerCanDealDamage(info.InitiatorPlayer, patrolHelicopter, true))
                return true;
            else
            {
                eventHeli.OnHeliAttacked(info.InitiatorPlayer.userID);
                eventController.OnTrainAttacked(info.InitiatorPlayer);
            }

            return null;
        }

        object OnEntityTakeDamage(BradleyAPC bradley, HitInfo info)
        {
            if (bradley == null || bradley.net == null || info == null)
                return null;

            if (!eventController.IsTrainBradley(bradley.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return true;

            if (!eventController.IsPlayerCanDealDamage(info.InitiatorPlayer, bradley, true))
                return true;
            else
                eventController.OnTrainAttacked(info.InitiatorPlayer);

            return null;
        }

        object OnEntityTakeDamage(AutoTurret autoTurret, HitInfo info)
        {
            if (autoTurret == null || autoTurret.net == null || info == null)
                return null;

            if (!eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return true;
            else if (!eventController.IsPlayerCanDealDamage(info.InitiatorPlayer, autoTurret, true))
                return true;
            else
                eventController.OnTrainAttacked(info.InitiatorPlayer);

            return null;
        }

        object OnEntityTakeDamage(SamSite samSite, HitInfo info)
        {
            if (samSite == null || samSite.net == null || info == null)
                return null;

            if (!eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return true;
            else if (!eventController.IsPlayerCanDealDamage(info.InitiatorPlayer, samSite, true))
                return true;
            else
                eventController.OnTrainAttacked(info.InitiatorPlayer);

            return null;
        }

        object OnEntityTakeDamage(ElectricSwitch electricSwitch, HitInfo info)
        {
            if (electricSwitch == null || electricSwitch.net == null || info == null)
                return null;

            if (eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return true;

            return null;
        }

        object OnEntityTakeDamage(PowerCounter powerCounter, HitInfo info)
        {
            if (powerCounter == null || powerCounter.net == null || info == null)
                return null;

            if (eventController.IsTrainCounter(powerCounter.net.ID.Value))
                return true;

            return null;
        }

        object OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            ScientistNPC scientistNPC = player as ScientistNPC;
            if (scientistNPC != null)
            {
                if (scientistNPC == null || scientistNPC.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                    return null;

                if (NpcSpawnManager.GetScientistByNetId(scientistNPC.net.ID.Value) == null)
                    return null;

                if (!eventController.IsPlayerCanDealDamage(info.InitiatorPlayer, scientistNPC, true))
                {
                    info.damageTypes.ScaleAll(0);
                    eventController.OnTrainAttacked(info.InitiatorPlayer);
                    return true;
                }
                else
                {
                    if (scientistNPC.isMounted)
                    {
                        if (!eventController.IsPlayerCanStopTrain(info.InitiatorPlayer, true))
                        {
                            info.damageTypes.ScaleAll(0);
                            return true;
                        }

                        if (_config.mainConfig.allowDriverDamage)
                        {
                            info.damageTypes.ScaleAll(10);
                        }
                        else
                        {
                            info.damageTypes.ScaleAll(0);
                            return true;
                        }
                    }
                    eventController.OnTrainAttacked(info.InitiatorPlayer);
                }
                return null;
            }
            else
            {
                if (!player.IsRealPlayer() || !player.IsSleeping() || info == null || info.InitiatorPlayer == null)
                    return null;

                if (!info.InitiatorPlayer.isMounted || info.InitiatorPlayer.userID.IsSteamId() || info.InitiatorPlayer.net == null)
                    return null;

                if (NpcSpawnManager.GetScientistByNetId(info.InitiatorPlayer.net.ID.Value))
                    return true;
            }

            return null;
        }

        void OnEntityDeath(PatrolHelicopter patrolHelicopter, HitInfo info)
        {
            if (patrolHelicopter == null || patrolHelicopter.net == null)
                return;

            EventHeli eventHeli = EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value);

            if (eventHeli != null && eventHeli.lastAttackedPlayer != 0)
                EconomyManager.AddBalance(eventHeli.lastAttackedPlayer, _config.supportedPluginsConfig.economicsConfig.heliPoint);
        }

        void OnEntityDeath(AutoTurret autoTurret, HitInfo info)
        {
            if (autoTurret == null || autoTurret.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return;

            if (eventController.IsTrainTurret(autoTurret.net.ID.Value))
                EconomyManager.AddBalance(info.InitiatorPlayer.userID, _config.supportedPluginsConfig.economicsConfig.turretPoint);
        }

        void OnEntityDeath(BradleyAPC bradleyAPC, HitInfo info)
        {
            if (bradleyAPC == null || bradleyAPC.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return;

            if (eventController.IsTrainBradley(bradleyAPC.net.ID.Value))
                EconomyManager.AddBalance(info.InitiatorPlayer.userID, _config.supportedPluginsConfig.economicsConfig.bradleyPoint);
        }

        void OnEntityDeath(ScientistNPC scientistNPC, HitInfo info)
        {
            if (scientistNPC == null || scientistNPC.net == null || info == null || !info.InitiatorPlayer.IsRealPlayer())
                return;

            if (NpcSpawnManager.GetScientistByNetId(scientistNPC.net.ID.Value) != null)
            {
                if (scientistNPC.isMounted)
                    eventController.OnDriverKilled(info.InitiatorPlayer);

                EconomyManager.AddBalance(info.InitiatorPlayer.userID, _config.supportedPluginsConfig.economicsConfig.npcPoint);
            }
        }

        object OnEntityEnter(TriggerTrainCollisions trigger, TrainCar trainCar)
        {
            if (!ins._config.mainConfig.destrroyWagons || trigger == null || !trigger.owner.IsExists() || trigger.owner.net == null || !trainCar.IsExists() || trainCar.net == null)
                return null;

            if (eventController.IsTrainWagon(trainCar.net.ID.Value) && !eventController.IsTrainWagon(trigger.owner.net.ID.Value))
            {
                if (trainCar is TrainEngine)
                {
                    if (eventController.IsReverse())
                        return null;
                }
                else if (!eventController.IsReverse())
                {
                    return null;
                }

                if (trigger.owner.IsExists() && ins._config.mainConfig.destrroyWagons)
                {
                    if (ins.plugins.Exists("TrainHomes") && (bool)TrainHomes.Call("IsTrainHomes", trigger.owner.net.ID.Value) && !(bool)TrainHomes.Call("IsFreeWagon", trigger.owner.net.ID.Value))
                        return null;

                    ins.NextTick(() => trigger.owner.Kill(BaseNetworkable.DestroyMode.Gib));
                }
            }

            return null;
        }

        object CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player)
        {
            if (heli == null || heli.helicopterBase == null || heli.helicopterBase.net == null)
                return null;

            EventHeli eventHeli = EventHeli.GetEventHeliByNetId(heli.helicopterBase.net.ID.Value);

            if (eventHeli != null && !eventHeli.IsHeliCanTarget())
                return false;

            if (player.IsSleeping() || (player.InSafeZone() && !player.IsHostile()))
                return false;

            return null;
        }

        object OnCustomNpcTarget(ScientistNPC scientistNPC, BasePlayer player)
        {
            if (eventController == null || scientistNPC == null || scientistNPC.net == null)
                return null;

            if (NpcSpawnManager.GetScientistByNetId(scientistNPC.net.ID.Value) == null)
                return null;

            if (!eventController.IsAgressive())
                return false;

            if (player.IsSleeping() || (player.InSafeZone() && !player.IsHostile()))
                return false;

            return null;
        }

        object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity entity)
        {
            if (bradley == null || bradley.net == null)
                return null;

            if (!eventController.IsTrainBradley(bradley.net.ID.Value))
                return null;

            BasePlayer targetPlayer = entity as BasePlayer;

            if (!targetPlayer.IsRealPlayer())
                return false;

            if (targetPlayer.IsSleeping() || (targetPlayer.InSafeZone() && !targetPlayer.IsHostile()))
                return false;

            return null;
        }

        object OnTrainCarUncouple(TrainCar trainCar, BasePlayer player)
        {
            if (trainCar == null || player == null || trainCar.net == null)
                return null;

            if (eventController.IsTrainWagon(trainCar.net.ID.Value))
                return true;

            return null;
        }

        object CanTrainCarCouple(TrainCar trainCar1, TrainCar trainCar2)
        {
            if (trainCar1 == null || trainCar1.net == null || trainCar2 == null || trainCar2.net == null)
                return null;

            if (eventController.IsTrainWagon(trainCar1.net.ID.Value) && !eventController.CanConnectToTrainWagon(trainCar1))
                return false;

            if (eventController.IsTrainWagon(trainCar2.net.ID.Value) && !eventController.CanConnectToTrainWagon(trainCar2))
                return false;

            return null;
        }

        object CanPickupEntity(BasePlayer player, ElectricSwitch electricSwitch)
        {
            if (player == null || electricSwitch == null || electricSwitch.net == null)
                return null;

            if (eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return false;

            return null;
        }

        object CanPickupEntity(BasePlayer player, PowerCounter powerCounter)
        {
            if (player == null || powerCounter == null || powerCounter.net == null)
                return null;

            if (eventController.IsTrainCounter(powerCounter.net.ID.Value))
                return false;

            return null;
        }

        object CanMountEntity(BasePlayer player, BaseVehicleSeat entity)
        {
            if (!player.IsRealPlayer() || !entity.IsExists())
                return null;

            TrainCar trainCar = entity.VehicleParent() as TrainCar;

            if (!trainCar.IsExists() || trainCar.net == null)
                return null;

            if (eventController.IsTrainWagon(trainCar.net.ID.Value))
                return true;

            return null;
        }

        object OnSwitchToggle(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (!electricSwitch.IsExists() || electricSwitch.net == null || player == null)
                return null;

            if (!eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return null;

            if (!electricSwitch.IsOn() && (!ins._config.mainConfig.allowEnableMovingByHandbrake || !eventController.IsDriverAlive()))
                return true;

            if (electricSwitch.IsOn() && !eventController.IsPlayerCanStopTrain(player, true))
                return true;

            return null;
        }

        void OnSwitchToggled(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (!electricSwitch.IsExists() || electricSwitch.net == null || player == null)
                return;

            if (!eventController.IsTrainSwitch(electricSwitch.net.ID.Value))
                return;

            eventController.OnSwitchToggled(player);
        }

        object OnCounterModeToggle(PowerCounter counter, BasePlayer player, bool mode)
        {
            if (!counter.IsExists() || counter.net == null || player == null)
                return null;

            if (eventController.IsTrainCounter(counter.net.ID.Value))
                return true;

            return null;
        }

        object OnCounterTargetChange(PowerCounter counter, BasePlayer player, int targetNumber)
        {
            if (!counter.IsExists() || counter.net == null || player == null)
                return null;

            if (eventController.IsTrainCounter(counter.net.ID.Value))
                return true;

            return null;
        }

        void OnPlayerSleep(BasePlayer player)
        {
            if (player == null)
                return;

            ZoneController.OnPlayerLeaveZone(player);
        }

        object CanLootEntity(BasePlayer player, LootContainer container)
        {
            if (player == null || container == null || container.net == null)
                return null;

            if (LootManager.GetContainerDataByNetId(container.net.ID.Value) == null)
                return null;

            if (!eventController.IsAgressive())
            {
                eventController.MakeAgressive();
                return true;
            }

            if (!eventController.IsPlayerCanLoot(player, true))
                return true;

            return null;
        }

        object CanLootEntity(BasePlayer player, SamSite samSite)
        {
            if (player == null || samSite == null || samSite.net == null)
                return null;

            if (!eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!eventController.IsAgressive())
                eventController.MakeAgressive();

            return true;
        }

        object OnSamSiteModeToggle(SamSite samSite, BasePlayer player, bool isEnable)
        {
            if (player == null || samSite == null || samSite.net == null)
                return null;

            if (!eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!eventController.IsAgressive())
                eventController.MakeAgressive();

            return true;
        }

        object OnTurretAuthorize(AutoTurret autoTurret, BasePlayer player)
        {
            if (player == null || autoTurret == null || autoTurret.net == null)
                return null;

            if (!eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return null;

            if (!eventController.IsAgressive())
                eventController.MakeAgressive();

            return true;
        }

        object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null || crate.net == null)
                return null;

            StorageContainerData storageContainerData = LootManager.GetContainerDataByNetId(crate.net.ID.Value);

            if (storageContainerData == null)
                return null;

            if (!eventController.IsAgressive())
            {
                eventController.MakeAgressive();
                return true;
            }

            if (!eventController.IsPlayerCanLoot(player, true))
                return true;

            if (!PveModeManager.IsPveModeBlockAction(player))
                EconomyManager.AddBalance(player.userID, _config.supportedPluginsConfig.economicsConfig.hackCratePoint);

            CrateConfig crateConfig = LootManager.GetCrateConfigByPresetName(storageContainerData.presetName);
            crate.Invoke(() => LootManager.UpdateCrateHackTime(crate, storageContainerData.presetName), 1.1f);
            eventController.OnPlayerStartHackingCrate((int)crateConfig.hackTime);

            return null;
        }

        void OnLootEntity(BasePlayer player, StorageContainer storageContainer)
        {
            if (player == null || storageContainer == null || storageContainer.net == null)
                return;

            StorageContainerData storageContainerData = LootManager.GetContainerDataByNetId(storageContainer.net.ID.Value);

            if (storageContainerData == null)
                return;

            LootManager.OnEventCrateLooted(storageContainer, player.userID);
        }

        void OnLootEntityEnd(BasePlayer player, StorageContainer storageContainer)
        {
            if (storageContainer == null || storageContainer.net == null || !player.IsRealPlayer())
                return;

            if (LootManager.GetContainerDataByNetId(storageContainer.net.ID.Value) == null)
                return;

            if (storageContainer is LootContainer == false)
            {
                if (storageContainer.inventory.IsEmpty())
                    storageContainer.Kill();
            }

            eventController.EventPassingCheck();
        }

        void OnCorpsePopulate(ScientistNPC scientistNPC, NPCPlayerCorpse corpse)
        {
            if (scientistNPC == null || scientistNPC.net == null || corpse == null)
                return;

            if (NpcSpawnManager.GetScientistByNetId(scientistNPC.net.ID.Value) == null)
                return;

            NpcConfig npcConfig = NpcSpawnManager.GetNpcConfigByDisplayName(scientistNPC.displayName);

            if (npcConfig != null)
            {
                ins.NextTick(() =>
                {
                    if (corpse == null)
                        return;

                    if (!corpse.containers.IsNullOrEmpty() && corpse.containers[0] != null)
                        LootManager.UpdateItemContainer(corpse.containers[0], npcConfig.lootTableConfig, npcConfig.lootTableConfig.clearDefaultItemList);

                    if (npcConfig.deleteCorpse && !corpse.IsDestroyed)
                        corpse.Kill();
                });
            }
        }

        #region OtherPlugins
        object CanEntityBeTargeted(BasePlayer player, AutoTurret turret)
        {
            if (eventController == null || turret == null || turret.net == null)
                return null;

            if (!eventController.IsTrainTurret(turret.net.ID.Value))
                return null;

            if (!player.IsRealPlayer())
                return false;
            else if (!eventController.IsAgressive())
                return false;
            else if (!PveModeManager.IsPveModeBlockAction(player))
                return true;

            return null;
        }

        object CanEntityBeTargeted(PlayerHelicopter playerHelicopter, SamSite samSite)
        {
            if (eventController == null || samSite == null || samSite.net == null)
                return null;

            if (!eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!eventController.IsAgressive())
                return false;

            return true;
        }

        object CanEntityTakeDamage(AutoTurret autoTurret, HitInfo info)
        {
            if (eventController == null || autoTurret == null || autoTurret.net == null || info == null)
                return null;

            if (!eventController.IsTrainTurret(autoTurret.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return false;
            else if (!eventController.IsPlayerCanDealDamage(info.InitiatorPlayer, autoTurret, true))
                return false;
            else if (!_config.supportedPluginsConfig.pveMode.enable || _config.supportedPluginsConfig.pveMode.damageTurret || !PveModeManager.IsPveModeBlockAction(info.InitiatorPlayer))
                return true;

            return null;
        }

        object CanEntityTakeDamage(SamSite samSite, HitInfo info)
        {
            if (eventController == null || samSite == null || samSite.net == null || info == null)
                return null;

            if (!eventController.IsTrainSamSite(samSite.net.ID.Value))
                return null;

            if (!info.InitiatorPlayer.IsRealPlayer())
                return false;
            else if (!eventController.IsPlayerCanDealDamage(info.InitiatorPlayer, samSite, true))
                return false;
            else if (!_config.supportedPluginsConfig.pveMode.enable || _config.supportedPluginsConfig.pveMode.damageTurret || !PveModeManager.IsPveModeBlockAction(info.InitiatorPlayer))
                return true;

            return null;
        }

        object CanEntityTakeDamage(BasePlayer victim, HitInfo hitinfo)
        {
            if (eventController == null || hitinfo == null || hitinfo.Initiator == null || hitinfo.Initiator.net == null || !victim.IsRealPlayer())
                return null;

            if (_config.zoneConfig.isPVPZone && !_config.supportedPluginsConfig.pveMode.enable)
            {
                if (hitinfo.InitiatorPlayer.IsRealPlayer() && ZoneController.IsPlayerInZone(hitinfo.InitiatorPlayer.userID) && ZoneController.IsPlayerInZone(victim.userID))
                    return true;
            }

            if (hitinfo.Initiator is AutoTurret)
            {
                if (eventController.IsTrainTurret(hitinfo.Initiator.net.ID.Value))
                    return true;
            }

            return null;
        }

        object CanEntityTakeDamage(PlayerHelicopter playerHelicopter, HitInfo hitinfo)
        {
            if (eventController == null || hitinfo == null || hitinfo.Initiator == null || hitinfo.Initiator.net == null)
                return null;

            if (hitinfo.Initiator is SamSite)
            {
                if (eventController.IsTrainSamSite(hitinfo.Initiator.net.ID.Value))
                    return true;
            }

            return null;
        }

        object CanEntityTakeDamage(CustomBradley bradleyApc, HitInfo hitinfo)
        {
            if (eventController == null || hitinfo == null || !hitinfo.InitiatorPlayer.IsRealPlayer() || bradleyApc.net == null)
                return null;

            if (eventController.IsTrainBradley(bradleyApc.net.ID.Value))
                return true;

            return null;
        }

        object CanPopulateLoot(LootContainer lootContainer)
        {
            if (eventController == null || lootContainer == null || lootContainer.net == null)
                return null;

            StorageContainerData storageContainerData = LootManager.GetContainerDataByNetId(lootContainer.net.ID.Value);

            if (storageContainerData != null)
            {
                CrateConfig crateConfig = LootManager.GetCrateConfigByPresetName(storageContainerData.presetName);

                if (crateConfig != null && !crateConfig.lootTableConfig.isAlphaLoot)
                    return true;
            }

            return null;
        }

        object CanPopulateLoot(ScientistNPC scientistNPC, NPCPlayerCorpse corpse)
        {
            if (eventController == null || scientistNPC == null || scientistNPC.net == null)
                return null;

            NpcConfig npcConfig = NpcSpawnManager.GetNpcConfigByDisplayName(scientistNPC.displayName);

            if (npcConfig != null && !npcConfig.lootTableConfig.isAlphaLoot)
                return true;

            return null;
        }

        object OnCustomLootContainer(NetworkableId netID)
        {
            if (eventController == null || netID == null)
                return null;

            StorageContainerData storageContainerData = LootManager.GetContainerDataByNetId(netID.Value);

            if (storageContainerData != null)
            {
                CrateConfig crateConfig = LootManager.GetCrateConfigByPresetName(storageContainerData.presetName);

                if (crateConfig != null && !crateConfig.lootTableConfig.isCustomLoot)
                    return true;
            }

            return null;
        }

        object OnCustomLootNPC(NetworkableId netID)
        {
            if (eventController == null || netID == null)
                return null;

            ScientistNPC scientistNPC = NpcSpawnManager.GetScientistByNetId(netID.Value);

            if (scientistNPC != null)
            {
                NpcConfig npcConfig = NpcSpawnManager.GetNpcConfigByDisplayName(scientistNPC.displayName);

                if (npcConfig != null && !npcConfig.lootTableConfig.isCustomLoot)
                    return true;
            }

            return null;
        }

        object OnContainerPopulate(LootContainer lootContainer)
        {
            if (eventController == null || lootContainer == null || lootContainer.net == null)
                return null;

            StorageContainerData storageContainerData = LootManager.GetContainerDataByNetId(lootContainer.net.ID.Value);

            if (storageContainerData != null)
            {
                CrateConfig crateConfig = LootManager.GetCrateConfigByPresetName(storageContainerData.presetName);

                if (crateConfig != null && !crateConfig.lootTableConfig.isLootTablePLugin)
                    return true;
            }

            return null;
        }

        object OnCorpsePopulate(NPCPlayerCorpse corpse)
        {
            if (eventController == null || corpse == null)
                return null;

            NpcConfig npcConfig = NpcSpawnManager.GetNpcConfigByDisplayName(corpse.playerName);

            if (npcConfig != null && !npcConfig.lootTableConfig.isAlphaLoot)
                return true;

            return null;
        }

        object OnCreateDynamicPVP(string eventName, PatrolHelicopter patrolHelicopter)
        {
            if (eventController == null || patrolHelicopter == null || patrolHelicopter.net == null)
                return null;

            if (EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value) != null)
                return true;

            return null;
        }

        object OnCreateDynamicPVP(string eventName, BradleyAPC bradleyAPC)
        {
            if (eventController == null || bradleyAPC == null || bradleyAPC.net == null)
                return null;

            if (eventController.IsTrainBradley(bradleyAPC.net.ID.Value))
                return true;

            return null;
        }

        void SetOwnerPveMode(string eventName, BasePlayer player)
        {
            if (eventController == null || string.IsNullOrEmpty(eventName) || eventName != Name || !player.IsRealPlayer())
                return;

            if (eventName == Name)
                PveModeManager.OnNewOwnerSet(player);
        }

        void ClearOwnerPveMode(string shortname)
        {
            if (eventController == null || string.IsNullOrEmpty(shortname))
                return;

            if (shortname == Name)
                PveModeManager.OnOwnerDeleted();
        }

        object CanBradleySpawnNpc(BradleyAPC bradley)
        {
            if (eventController == null || bradley == null || bradley.net == null)
                return null;

            if (eventController.IsTrainBradley(bradley.net.ID.Value))
                return true;

            return null;
        }

        object CanHelicopterSpawnNpc(PatrolHelicopter patrolHelicopter)
        {
            if (eventController == null || patrolHelicopter == null || patrolHelicopter.net == null)
                return null;

            if (ins._config.supportedPluginsConfig.betterNpcConfig.isHeliNpc)
                return null;

            if (EventHeli.GetEventHeliByNetId(patrolHelicopter.net.ID.Value) != null)
                return true;

            return null;
        }
        #endregion OtherPlugins
        #endregion Hooks

        #region Commands
        [ChatCommand("atrainstart")]
        void ChatStartCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (arg == null || arg.Length == 0)
                EventLauncher.DelayStartEvent(false, player);
            else
            {
                string eventPresetName = arg[0];
                EventLauncher.DelayStartEvent(false, player, eventPresetName);
            }
        }

        [ChatCommand("atrainstop")]
        void ChatStopCommand(BasePlayer player, string command, string[] arg)
        {
            if (player.IsAdmin)
                EventLauncher.StopEvent();
        }

        [ConsoleCommand("atrainstart")]
        void ConsoleStartCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            if (arg == null || arg.Args == null || arg.Args.Length == 0)
                EventLauncher.DelayStartEvent();
            else
            {
                string eventPresetName = arg.Args[0];
                EventLauncher.DelayStartEvent(presetName: eventPresetName);
            }
        }

        [ConsoleCommand("atrainstop")]
        void ConsoleStopCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                EventLauncher.StopEvent();
        }

        [ChatCommand("atrainstartunderground")]
        void ChatUndergroundStartCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (arg == null || arg.Length == 0)
                EventLauncher.DelayStartEvent(false, player, overrideUndergroundChance: 100);
            else
            {
                string eventPresetName = arg[0];
                EventLauncher.DelayStartEvent(false, player, eventPresetName, 100);
            }
        }

        [ChatCommand("atrainstartaboveground")]
        void ChatAbovegroundStartCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (arg == null || arg.Length == 0)
                EventLauncher.DelayStartEvent(false, player, overrideUndergroundChance: 0);
            else
            {
                string eventPresetName = arg[0];
                EventLauncher.DelayStartEvent(false, player, eventPresetName, 0);
            }
        }

        [ConsoleCommand("atrainstartunderground")]
        void ConsoleUndergroundStartCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            if (arg == null || arg.Args == null || arg.Args.Length == 0)
                EventLauncher.DelayStartEvent(overrideUndergroundChance: 100);
            else
            {
                string eventPresetName = arg.Args[0];
                EventLauncher.DelayStartEvent(presetName: eventPresetName, overrideUndergroundChance: 100);
            }
        }

        [ConsoleCommand("atrainstartaboveground")]
        void ConsoleAbovegroundStartCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            if (arg == null || arg.Args == null || arg.Args.Length == 0)
                EventLauncher.DelayStartEvent(overrideUndergroundChance: 0);
            else
            {
                string eventPresetName = arg.Args[0];
                EventLauncher.DelayStartEvent(presetName: eventPresetName, overrideUndergroundChance: 0);
            }
        }

        [ChatCommand("atrainpoint")]
        void ChatCustomPointCommand(BasePlayer player, string command, string[] arg)
        {
            if (!player.IsAdmin)
                return;

            if (!SpawnPositionFinder.IsRailsInPosition(player.transform.position))
            {
                PrintToChat(player, _config.prefix + " <color=#ce3f27>Couldn't</color> find the rails");
                return;
            }

            PrintToChat(player, _config.prefix + " New spawn point <color=#738d43>successfully</color> added");
            Vector3 rotation = player.eyes.GetLookRotation().eulerAngles;
            LocationConfig locationConfig = new LocationConfig
            {
                position = player.transform.position.ToString(),
                rotation = (new Vector3(0, rotation.y, 0)).ToString()
            };
            _config.mainConfig.customSpawnPointConfig.points.Add(locationConfig);
            SaveConfig();
        }

        [ConsoleCommand("savecustomwagon")]
        void ConsoleSaveCustomWagonCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null)
                return;

            string customizationPresetName = arg.Args[0];
            string wagonShortPrefabName = arg.Args[1];

            WagonCustomizator.MapSaver.CreateOrAddNewWagonToData(customizationPresetName, wagonShortPrefabName);
        }
        #endregion Commands

        #region Methods
        void Unsubscribes()
        {
            foreach (string hook in subscribeMethods)
                Unsubscribe(hook);
        }

        void Subscribes()
        {
            foreach (string hook in subscribeMethods)
                Subscribe(hook);
        }

        void UpdateConfig()
        {
            if (_config.versionConfig != Version)
            {
                PluginConfig defaultConfig = PluginConfig.DefaultConfig();

                if (_config.versionConfig.Minor == 4)
                {
                    if (_config.versionConfig.Patch <= 1)
                    {
                        CrateConfig crateNormalConfig = new CrateConfig
                        {
                            presetName = "crate_normal_default",
                            prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            hackTime = 0,
                            lootTableConfig = new LootTableConfig
                            {
                                minItemsAmount = 1,
                                maxItemsAmount = 2,
                                items = new List<LootItemConfig>
                                    {
                                        new LootItemConfig
                                        {
                                            shortname = "scrap",
                                            minAmount = 100,
                                            maxAmount = 200,
                                            chance = 100f,
                                            isBlueprint = false,
                                            skin = 0,
                                            name = ""
                                        }
                                    }
                            }
                        };
                        CrateConfig crateNormal2Config = new CrateConfig
                        {
                            presetName = "crate_normal2_default",
                            prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            hackTime = 0,
                            lootTableConfig = new LootTableConfig
                            {
                                minItemsAmount = 1,
                                maxItemsAmount = 2,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = ""
                                    }
                                }
                            }
                        };
                        _config.crateConfigs.Add(crateNormalConfig);
                        _config.crateConfigs.Add(crateNormal2Config);

                        TurretConfig turretConfig = _config.turretConfigs.FirstOrDefault(x => true);
                        WagonConfig halloweenWagoConfig = new WagonConfig
                        {
                            presetName = "halloween_wagon",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["crate_normal_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0.407, 2.403, -3.401)",
                                        rotation = "(303.510, 0, 328.794)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.374, 2.416, 3.104)",
                                        rotation = "(21.106, 261.772, 352.540)"
                                    }
                                },
                                ["crate_normal2_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.095, 1.817, -0.217)",
                                        rotation = "(19.048, 336.704, 359.624)"
                                    }
                                }
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>(),
                            decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                [turretConfig.presetName] = new HashSet<LocationConfig>
                                    {
                                        new LocationConfig
                                        {
                                            position = "(-0.940, 1.559, -6.811)",
                                            rotation = "(0, 180, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(0.940, 1.559, -6.811)",
                                            rotation = "(0, 180, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(-0.940, 1.559, 6.811)",
                                            rotation = "(0, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(0.940, 1.559, 6.811)",
                                            rotation = "(0, 0, 0)"
                                        }
                                    }
                            },
                        };
                        _config.wagonConfigs.Add(halloweenWagoConfig);

                        NpcConfig npcConfig = _config.npcConfigs.FirstOrDefault(x => true);
                        LocomotiveConfig halloweenLocomotive = new LocomotiveConfig
                        {
                            presetName = "locomotive_halloween",
                            prefabName = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab",
                            engineForce = 500000f,
                            maxSpeed = 14,
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                [npcConfig.displayName] = new HashSet<LocationConfig>
                                    {
                                        new LocationConfig
                                        {
                                            position = "(-1.341, 1.546, 2)",
                                            rotation = "(0, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(-1.341, 1.546, -2)",
                                            rotation = "(0, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(-1.341, 1.546, -6)",
                                            rotation = "(0, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(1.341, 1.546, 2)",
                                            rotation = "(0, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(1.341, 1.546, -2)",
                                            rotation = "(0, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(1.341, 1.546, -6)",
                                            rotation = "(0, 0, 0)"
                                        }
                                    }
                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            }
                        };
                        _config.locomotiveConfigs.Add(halloweenLocomotive);

                        WagonConfig samsiteWagomConfig = _config.wagonConfigs.FirstOrDefault(x => x.presetName.Contains("sam"));
                        WagonConfig crateWagomConfig = _config.wagonConfigs.FirstOrDefault(x => x.presetName.Contains("crate"));

                        EventConfig halloweenTrainConfig = new EventConfig
                        {
                            presetName = "train_halloween",
                            displayName = en ? "Halloween Train" : "Хэллоуинский Поезд",
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 300,
                            isAutoStart = true,
                            chance = 0,
                            locomotivePreset = "locomotive_halloween",
                            wagonsPreset = new List<string>(),
                            heliPreset = ""
                        };

                        if (crateWagomConfig != null)
                            halloweenTrainConfig.wagonsPreset.Add(crateWagomConfig.presetName);

                        halloweenTrainConfig.wagonsPreset.Add(halloweenWagoConfig.presetName);

                        if (samsiteWagomConfig != null)
                            halloweenTrainConfig.wagonsPreset.Add(samsiteWagomConfig.presetName);

                        _config.eventConfigs.Add(halloweenTrainConfig);
                    }
                    if (_config.versionConfig.Patch <= 3)
                    {
                        _config.customizationConfig = new CustomizationConfig
                        {
                            isElectricFurnacesEnable = true,
                            isBoilersEnable = true,
                            isFireEnable = true
                        };
                    }
                    if (_config.versionConfig.Patch <= 4)
                    {
                        if (_config.customizationConfig == null)
                        {
                            _config.customizationConfig = new CustomizationConfig
                            {
                                isElectricFurnacesEnable = true,
                                isBoilersEnable = true,
                                isFireEnable = true
                            };
                        }
                    }
                    if (_config.versionConfig.Patch <= 6)
                    {
                        _config.customizationConfig.isLightOnlyAtNight = true;
                        _config.mainConfig.isRestoreStopTimeAfterDamageOrLoot = true;
                    }
                    if (_config.versionConfig.Patch <= 7)
                    {
                        _config.customizationConfig.profileName = "";

                        _config.eventConfigs.Add
                        (
                            new EventConfig
                            {
                                presetName = "train_xmas_easy",
                                displayName = en ? "Small Christmas train" : "Маленький Новогодний Поезд",
                                isUndergroundTrain = true,
                                eventTime = 3600,
                                stopTime = 300,
                                isAutoStart = true,
                                chance = 0,
                                locomotivePreset = "locomotive_default",
                                wagonsPreset = new List<string>
                                {
                                    "xmas_wagon_1"
                                },
                                heliPreset = ""
                            }
                        );
                        _config.eventConfigs.Add
                        (
                            new EventConfig
                            {
                                presetName = "train_xmas_medium",
                                displayName = en ? "Medium Christmas train" : "Средний Новогодний Поезд",
                                isUndergroundTrain = true,
                                eventTime = 3600,
                                stopTime = 300,
                                isAutoStart = true,
                                chance = 0,
                                locomotivePreset = "locomotive_turret",
                                wagonsPreset = new List<string>
                                {
                                    "xmas_wagon_1",
                                    "xmas_wagon_2"
                                },
                                heliPreset = ""
                            }
                        );
                        _config.eventConfigs.Add
                        (
                           new EventConfig
                           {
                               presetName = "train_xmas_hard",
                               displayName = en ? "Big Christmas train" : "Большой Новогодний Поезд",
                               isUndergroundTrain = false,
                               eventTime = 3600,
                               stopTime = 300,
                               isAutoStart = true,
                               chance = 0,
                               locomotivePreset = "locomotive_new",
                               wagonsPreset = new List<string>
                                {
                                    "wagon_crate_2",
                                    "xmas_wagon_2",
                                    "wagon_bradley"
                                },
                               heliPreset = ""
                           }
                        );

                        _config.wagonConfigs.Add
                        (
                            new WagonConfig
                            {
                                presetName = "xmas_wagon_1",
                                prefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab",
                                brradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                                crates = new Dictionary<string, HashSet<LocationConfig>>
                                {
                                    ["xmas_crate"] = new HashSet<LocationConfig>
                                    {
                                        new LocationConfig
                                        {
                                            position = "(-0.148, 2.707, -1.613)",
                                            rotation = "(72.214, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(-0.221, 2.478, -2.314)",
                                            rotation = "(52.555, 180.000, 0)"
                                        }
                                    }
                                },
                                samsites = new Dictionary<string, HashSet<LocationConfig>>(),
                                decors = new Dictionary<string, HashSet<LocationConfig>>(),
                                npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                                turrets = new Dictionary<string, HashSet<LocationConfig>>
                                {
                                    ["turret_ak"] = new HashSet<LocationConfig>
                                    {
                                        new LocationConfig
                                        {
                                            position = "(-0.940, 1.559, -6.811)",
                                            rotation = "(0, 180, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(0.940, 1.559, -6.811)",
                                            rotation = "(0, 180, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(-0.940, 1.559, 6.811)",
                                            rotation = "(0, 0, 0)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(0.940, 1.559, 6.811)",
                                            rotation = "(0, 0, 0)"
                                        }
                                    }
                                },
                            }
                        );
                        _config.wagonConfigs.Add
                        (
                           new WagonConfig
                           {
                               presetName = "xmas_wagon_2",
                               prefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadable.entity.prefab",
                               brradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                               crates = new Dictionary<string, HashSet<LocationConfig>>
                               {
                                   ["xmas_crate"] = new HashSet<LocationConfig>
                                    {
                                        new LocationConfig
                                        {
                                            position = "(0.027, 3.276, -3.562)",
                                            rotation = "(0.361, 355.541, 16.972)"
                                        },
                                        new LocationConfig
                                        {
                                            position = "(0.027, 3.276, 3.897)",
                                            rotation = "(334.884, 355.419, 352.239)"
                                        }
                                    }
                               },
                               samsites = new Dictionary<string, HashSet<LocationConfig>>(),
                               decors = new Dictionary<string, HashSet<LocationConfig>>(),
                               npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                               turrets = new Dictionary<string, HashSet<LocationConfig>>
                               {
                               },
                           }
                        );

                        _config.crateConfigs.Add
                        (
                            new CrateConfig
                            {
                                presetName = "xmas_crate",
                                prefab = "assets/prefabs/missions/portal/proceduraldungeon/xmastunnels/loot/xmastunnellootbox.prefab",
                                hackTime = 0,
                                lootTableConfig = new LootTableConfig
                                {
                                    maxItemsAmount = 1,
                                    minItemsAmount = 2,
                                    items = new List<LootItemConfig>
                                    {
                                        new LootItemConfig
                                        {
                                            shortname = "scrap",
                                            minAmount = 100,
                                            maxAmount = 200,
                                            chance = 100f,
                                            isBlueprint = false,
                                            skin = 0,
                                            name = ""
                                        }
                                    }
                                }
                            }
                        );

                        _config.customizationConfig.isNeonSignsEnable = true;
                        _config.customizationConfig.giftCannonSetting = defaultConfig.customizationConfig.giftCannonSetting;
                        _config.customizationConfig.fireworksSettings = defaultConfig.customizationConfig.fireworksSettings;
                    }
                    if (_config.versionConfig.Patch <= 9)
                    {

                    }
                    _config.versionConfig = new VersionNumber(1, 5, 0);
                }

                if (_config.versionConfig.Minor == 5)
                {
                    if (_config.versionConfig.Patch == 0)
                        _config.supportedPluginsConfig.pveMode.scaleDamage = new Dictionary<string, float>
                        {
                            ["Npc"] = 1f,
                            ["Bradley"] = 2f,
                            ["Helicopter"] = 2f,
                            ["Turret"] = 2f,
                        };

                    if (_config.versionConfig.Patch <= 1)
                    {
                        PrefabLootTableConfigs prefabConfigs = new PrefabLootTableConfigs
                        {
                            isEnable = false,
                            prefabs = new List<PrefabConfig>
                            {
                                new PrefabConfig
                                {
                                    minLootScale = 1,
                                    maxLootScale = 1,
                                    prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                }
                            }
                        };

                        foreach (CrateConfig crateConfig in _config.crateConfigs)
                        {
                            crateConfig.lootTableConfig.prefabConfigs = prefabConfigs;

                            foreach (LootItemConfig lootItemConfig in crateConfig.lootTableConfig.items)
                                lootItemConfig.genomes = new List<string>();
                        }

                        foreach (NpcConfig npcConfig in _config.npcConfigs)
                        {
                            npcConfig.speed = 5f;
                            npcConfig.roamRange = 10;
                            npcConfig.chaseRange = 110;
                            npcConfig.lootTableConfig.prefabConfigs = prefabConfigs;

                            foreach (NpcBelt item in npcConfig.beltItems)
                                item.ammo = "";

                            foreach (LootItemConfig lootItemConfig in npcConfig.lootTableConfig.items)
                                lootItemConfig.genomes = new List<string>();
                        }

                        foreach (LootItemConfig lootItemConfig in _config.customizationConfig.giftCannonSetting.items)
                            lootItemConfig.genomes = new List<string>();

                        foreach (EventConfig eventConfig in _config.eventConfigs)
                        {
                            eventConfig.minTimeAfterWipe = 0;
                            eventConfig.maxTimeAfterWipe = -1;
                            eventConfig.zoneRadius = 100;
                        }

                        _config.guiConfig.offsetMinY = defaultConfig.guiConfig.offsetMinY;
                        _config.notifyConfig.gameTipConfig = defaultConfig.notifyConfig.gameTipConfig;

                        _config.mainConfig.maxGroundDamageDistance = defaultConfig.mainConfig.maxGroundDamageDistance;
                        _config.mainConfig.maxHeliDamageDistance = defaultConfig.mainConfig.maxHeliDamageDistance;

                        NpcConfig newDriverConfig = _config.npcConfigs.FirstOrDefault(x => true);

                        foreach (LocomotiveConfig locomotiveConfig in _config.locomotiveConfigs)
                        {
                            locomotiveConfig.driverName = newDriverConfig.displayName;
                            locomotiveConfig.handleBrakeConfig = new EntitySpawnConfig { isEnable = true };
                            locomotiveConfig.stopTimerConfig = new EntitySpawnConfig { isEnable = true };
                            locomotiveConfig.eventTimerConfig = new EntitySpawnConfig { isEnable = true };
                        }

                        _config.mainConfig.enableBackConnector = true;
                        _config.mainConfig.allowEnableMovingByHandbrake = true;
                        _config.mainConfig.isNpcJumpInSubway = true;
                        _config.mainConfig.isNpcJumpOnSurface = true;
                        _config.mainConfig.allowDriverDamage = true;
                        _config.mainConfig.reviveTrainDriver = true;

                        _config.mainConfig.customSpawnPointConfig = new CustomSpawnPointConfig
                        {
                            isEnabled = false,
                            points = new HashSet<LocationConfig>(),
                        };
                    }

                    if (_config.versionConfig.Patch <= 3)
                    {
                        _config.supportedPluginsConfig.pveMode.showEventOwnerNameOnMap = true;
                    }

                    if (_config.versionConfig.Patch <= 6)
                    {
                        foreach (LocomotiveConfig locomotiveConfig in _config.locomotiveConfigs)
                        {
                            if (locomotiveConfig.prefabName == "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab")
                            {
                                locomotiveConfig.handleBrakeConfig.location = new LocationConfig
                                {
                                    position = "(0.270, 2.805, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                };
                                locomotiveConfig.eventTimerConfig.location = new LocationConfig
                                {
                                    position = "(0.270, 2.412, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                };
                                locomotiveConfig.stopTimerConfig.location = new LocationConfig
                                {
                                    position = "(0.270, 3.012, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                };
                            }
                            else
                            {
                                locomotiveConfig.handleBrakeConfig.location = new LocationConfig
                                {
                                    position = "(0.097, 2.805, 1.816)",
                                    rotation = "(0, 180, 0)"
                                };
                                locomotiveConfig.stopTimerConfig.location = new LocationConfig
                                {
                                    position = "(0.097, 3.012, 1.810)",
                                    rotation = "(0, 180, 0)"
                                };
                                locomotiveConfig.eventTimerConfig.location = new LocationConfig
                                {
                                    position = "(0.097, 2.412, 1.810)",
                                    rotation = "(0, 180, 0)"
                                };
                            }
                        }
                    }

                    if (_config.versionConfig.Patch <= 7)
                    {
                        BaseLootTableConfig defaultLootTable = new BaseLootTableConfig
                        {
                            clearDefaultItemList = false,
                            prefabConfigs = new PrefabLootTableConfigs
                            {
                                isEnable = false,
                                prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                            },
                            isRandomItemsEnable = false,
                            maxItemsAmount = 1,
                            minItemsAmount = 2,
                            items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                        };

                        foreach (HeliConfig heliConfig in ins._config.heliConfigs)
                            heliConfig.baseLootTableConfig = defaultLootTable;
                    }
                    _config.versionConfig = new VersionNumber(1, 6, 0);
                }

                if (_config.versionConfig.Minor == 6)
                {
                    if (_config.versionConfig.Patch == 0)
                    {
                        _config.supportedPluginsConfig.betterNpcConfig = new BetterNpcConfig
                        {
                            isHeliNpc = false,
                        };

                        _config.zoneConfig.isColoredBorder = _config.zoneConfig.isDome && _config.zoneConfig.darkening > 0;
                        _config.zoneConfig.brightness = 5;
                        _config.zoneConfig.borderColor = 2;

                        foreach (LootItemConfig itemConfig in ins._config.customizationConfig.giftCannonSetting.items)
                            if (itemConfig.genomes == null)
                                itemConfig.genomes = new List<string>();
                    }

                    if (_config.versionConfig.Patch <= 7)
                    {
                        foreach (HeliConfig heliConfig in _config.heliConfigs)
                        {
                            heliConfig.immediatelyKill = true;
                        }

                        foreach (NpcConfig npcConfig in _config.npcConfigs)
                        {
                            npcConfig.presetName = npcConfig.displayName;
                        }

                        _config.customizationConfig.giftCannonSetting.isGiftCannonEnable = false;
                        _config.customizationConfig.fireworksSettings.isFireworksOn = false;
                    }
                    if (_config.versionConfig.Patch <= 8)
                    {
                        foreach (NpcConfig npcConfig in _config.npcConfigs)
                            npcConfig.lootTableConfig.alphaLootPresetName = string.Empty;

                        foreach (CrateConfig crateConfig in _config.crateConfigs)
                            crateConfig.lootTableConfig.alphaLootPresetName = string.Empty;

                        foreach (LootItemConfig lootItemConfig in _config.customizationConfig.giftCannonSetting.items)
                            if (lootItemConfig.genomes == null)
                                lootItemConfig.genomes = new List<string>();
                    }
                    _config.versionConfig = new VersionNumber(1, 7, 0);
                }

                if (_config.versionConfig.Minor == 7)
                {
                    if (_config.versionConfig.Patch <= 5)
                    {
                        foreach (HeliConfig heliConfig in _config.heliConfigs)
                        {
                            heliConfig.cratesLifeTime = 1800;
                        }
                    }
                    _config.versionConfig = new VersionNumber(1, 8, 0);
                }

                _config.versionConfig = Version;
                SaveConfig();
            }
        }

        void UpdateLootTables()
        {
            foreach (CrateConfig crateConfig in ins._config.crateConfigs)
                UpdateBaseLootTable(crateConfig.lootTableConfig);

            foreach (NpcConfig npcConfig in ins._config.npcConfigs)
                UpdateBaseLootTable(npcConfig.lootTableConfig);
        }

        void UpdateBaseLootTable(LootTableConfig LootTableConfig)
        {
            for (int i = 0; i < LootTableConfig.items.Count; i++)
            {
                LootItemConfig lootItemConfig = LootTableConfig.items[i];

                if (lootItemConfig.chance <= 0)
                    LootTableConfig.items.RemoveAt(i);
            }

            LootTableConfig.items = LootTableConfig.items.OrderByQuickSort(x => x.chance);

            if (LootTableConfig.maxItemsAmount > LootTableConfig.items.Count)
                LootTableConfig.maxItemsAmount = LootTableConfig.items.Count;

            if (LootTableConfig.minItemsAmount > LootTableConfig.maxItemsAmount)
                LootTableConfig.minItemsAmount = LootTableConfig.maxItemsAmount;
        }

        static void Debug(params object[] arg)
        {
            string result = "";

            foreach (object obj in arg)
                if (obj != null)
                    result += obj.ToString() + " ";

            ins.Puts(result);
        }
        #endregion Methods

        #region Classes
        static class EventLauncher
        {
            static Coroutine autoEventCoroutine;
            static Coroutine delayedEventStartCorountine;

            internal static bool IsEventActive()
            {
                return ins != null && ins.eventController != null;
            }

            internal static void AutoStartEvent()
            {
                if (!ins._config.mainConfig.isAutoEvent)
                    return;

                if (autoEventCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(autoEventCoroutine);

                autoEventCoroutine = ServerMgr.Instance.StartCoroutine(AutoEventCorountine());
            }

            internal static void DelayStartEvent(bool isAutoActivated = false, BasePlayer activator = null, string presetName = "", float overrideUndergroundChance = -1)
            {
                if (IsEventActive() || delayedEventStartCorountine != null)
                {
                    NotifyManager.PrintError(activator, "EventActive_Exeption");
                    return;
                }

                if (autoEventCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(autoEventCoroutine);

                float undergroundChance = overrideUndergroundChance >= 0 ? overrideUndergroundChance : ins._config.mainConfig.undergroundChance;
                bool isUnderGround = UnityEngine.Random.Range(0f, 100f) < undergroundChance;

                EventConfig eventConfig = DefineEventConfig(presetName, isUnderGround);
                if (eventConfig == null)
                {
                    NotifyManager.PrintError(activator, "ConfigurationNotFound_Exeption");
                    StopEvent();
                    return;
                }

                delayedEventStartCorountine = ServerMgr.Instance.StartCoroutine(DelayedStartEventCorountine(eventConfig, isUnderGround));

                if (!isAutoActivated)
                    NotifyManager.PrintInfoMessage(activator, "SuccessfullyLaunched");
            }

            static IEnumerator AutoEventCorountine()
            {
                yield return CoroutineEx.waitForSeconds(UnityEngine.Random.Range(ins._config.mainConfig.minTimeBetweenEvents, ins._config.mainConfig.maxTimeBetweenEvents));
                yield return CoroutineEx.waitForSeconds(5f);
                DelayStartEvent(true);
            }

            static IEnumerator DelayedStartEventCorountine(EventConfig eventConfig, bool isUnderGround)
            {
                if (ins._config.notifyConfig.preStartTime > 0)
                    NotifyManager.SendMessageToAll("PreStartTrain", ins._config.prefix, eventConfig.displayName, ins._config.notifyConfig.preStartTime);

                yield return CoroutineEx.waitForSeconds(ins._config.notifyConfig.preStartTime);

                StartEvent(eventConfig, isUnderGround);
            }

            static void StartEvent(EventConfig eventConfig, bool isUnderGround)
            {
                GameObject gameObject = new GameObject();
                ins.eventController = gameObject.AddComponent<EventController>();
                ins.eventController.Init(eventConfig, isUnderGround);

                if (ins._config.mainConfig.enableStartStopLogs)
                    NotifyManager.PrintLogMessage("EventStart_Log", eventConfig.presetName);

                Interface.CallHook($"On{ins.Name}EventStart");
            }

            internal static void StopEvent(bool isPluginUnloadingOrFailed = false)
            {
                if (IsEventActive())
                {
                    ins.Unsubscribes();

                    ins.eventController.DeleteController();
                    ZoneController.TryDeleteZone();
                    PveModeManager.OnEventEnd();
                    EventMapMarker.DeleteMapMarker();
                    NpcSpawnManager.ClearData(false);
                    LootManager.ClearLootData();
                    GuiManager.DestroyAllGui();
                    EconomyManager.OnEventEnd();
                    EventHeli.ClearData();

                    NotifyManager.SendMessageToAll("EndEvent", ins._config.prefix);
                    Interface.CallHook($"On{ins.Name}EventStop");

                    if (ins._config.mainConfig.enableStartStopLogs)
                        NotifyManager.PrintLogMessage("EventStop_Log");

                    if (!isPluginUnloadingOrFailed)
                        AutoStartEvent();
                }

                if (delayedEventStartCorountine != null)
                {
                    ServerMgr.Instance.StopCoroutine(delayedEventStartCorountine);
                    delayedEventStartCorountine = null;
                }
            }

            static EventConfig DefineEventConfig(string eventPresetName, bool isUnderGround)
            {
                if (eventPresetName != "")
                    return ins._config.eventConfigs.FirstOrDefault(x => x.presetName == eventPresetName);

                HashSet<EventConfig> suitableEventConfigs = ins._config.eventConfigs.Where(x => x.chance > 0 && x.isAutoStart && (!isUnderGround || x.isUndergroundTrain) && IsEventConfigSuitableByTime(x));

                if (suitableEventConfigs == null || suitableEventConfigs.Count == 0)
                    return null;

                float sumChance = 0;
                foreach (EventConfig eventConfig in suitableEventConfigs)
                    sumChance += eventConfig.chance;

                float random = UnityEngine.Random.Range(0, sumChance);

                foreach (EventConfig eventConfig in suitableEventConfigs)
                {
                    random -= eventConfig.chance;

                    if (random <= 0)
                        return eventConfig;
                }

                return null;
            }

            static bool IsEventConfigSuitableByTime(EventConfig eventConfig)
            {
                if (eventConfig.minTimeAfterWipe <= 0 && eventConfig.maxTimeAfterWipe <= 0)
                    return true;

                int timeScienceWipe = GetTimeScienceLastWipe();

                if (timeScienceWipe < eventConfig.minTimeAfterWipe)
                    return false;
                if (eventConfig.maxTimeAfterWipe > 0 && timeScienceWipe > eventConfig.maxTimeAfterWipe)
                    return false;

                return true;
            }

            static int GetTimeScienceLastWipe()
            {
                DateTime startTime = new DateTime(2019, 1, 1, 0, 0, 0);

                double realTime = DateTime.UtcNow.Subtract(startTime).TotalSeconds;
                double wipeTime = SaveRestore.SaveCreatedTime.Subtract(startTime).TotalSeconds;

                return Convert.ToInt32(realTime - wipeTime);
            }
        }

        class EventController : FacepunchBehaviour
        {
            internal EventConfig eventConfig;
            TrainEngine trainEngine;
            TrainCar lastWagon;
            Coroutine spawnCoroutine;
            Coroutine eventCoroutine;
            List<WagonData> wagonDatas = new List<WagonData>();
            HashSet<BradleyAPC> bradleys = new HashSet<BradleyAPC>();
            HashSet<AutoTurret> turrets = new HashSet<AutoTurret>();
            HashSet<SamSite> samsites = new HashSet<SamSite>();
            HashSet<NpcData> npcDatas = new HashSet<NpcData>();
            HashSet<StorageContainer> containers = new HashSet<StorageContainer>();
            ScientistNPC driver;
            ElectricSwitch electricSwitch;
            PowerCounter stopCounter;
            PowerCounter eventCounter;
            WagonCustomizator wagonCustomizator;
            int eventTime;
            int agressiveTime;
            int stopTime;
            bool isReverse;
            bool isUnderGround;
            bool isEventLooted;
            Vector3 lastGoodPosition;
            float lastGoodPositionTime;
            BasePlayer lastStopper;

            internal int GetEventTime()
            {
                return eventTime;
            }

            internal bool IsStopped()
            {
                return stopTime > 0 && ZoneController.IsZoneCreated();
            }

            internal bool IsTrainWagon(ulong netID)
            {
                return wagonDatas.Any(x => x.trainCar.IsExists() && x.trainCar.net != null && x.trainCar.net.ID.Value == netID);
            }

            internal bool IsTrainBradley(ulong netID)
            {
                return bradleys.Any(x => x.IsExists() && x.net != null && x.net.ID.Value == netID);
            }

            internal bool IsTrainTurret(ulong netID)
            {
                return turrets.Any(x => x.IsExists() && x.net != null && x.net.ID.Value == netID);
            }

            internal bool IsTrainSamSite(ulong netID)
            {
                return samsites.Any(x => x.IsExists() && x.net != null && x.net.ID.Value == netID);
            }

            internal bool IsTrainSwitch(ulong netID)
            {
                return electricSwitch.IsExists() && electricSwitch.net != null && electricSwitch.net.ID.Value == netID;
            }

            internal bool IsTrainCounter(ulong netID)
            {
                return (stopCounter.IsExists() && stopCounter.net != null && stopCounter.net.ID.Value == netID) || (eventCounter.IsExists() && eventCounter.net != null && eventCounter.net.ID.Value == netID);
            }

            internal bool IsAgressive()
            {
                return ins._config.mainConfig.isAggressive || agressiveTime > 0 || stopTime > 0 || !driver.IsExists() || driver.IsDead();
            }

            internal bool IsDriverAlive()
            {
                return driver.IsExists();
            }

            internal bool IsPlayerCanDealDamage(BasePlayer player, BaseCombatEntity trainEntity, bool shoudSendMessages)
            {
                Vector3 playerGroundPosition = new Vector3(player.transform.position.x, 0, player.transform.position.z);
                Vector3 entityGroundPosition = new Vector3(trainEntity.transform.position.x, 0, trainEntity.transform.position.z);
                float distance = Vector3.Distance(playerGroundPosition, entityGroundPosition);

                if (ins._config.mainConfig.maxGroundDamageDistance > 0 && distance > ins._config.mainConfig.maxGroundDamageDistance)
                {
                    if (shoudSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "DamageDistance", ins._config.prefix);

                    return false;
                }

                if (PveModeManager.IsPveModeBlockInterract(player))
                {
                    if (player.IsAdmin && ins._config.supportedPluginsConfig.pveMode.ignoreAdmin)
                        return true;

                    NotifyManager.SendMessageToPlayer(player, "PveMode_BlockAction", ins._config.prefix);
                    return false;
                }

                return true;
            }

            internal bool IsPlayerCanLoot(BasePlayer player, bool shoudSendMessages)
            {
                if (ins._config.mainConfig.needStopTrain && !IsStopped())
                {
                    if (shoudSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "NeedStopTrain", ins._config.prefix);

                    return false;
                }

                if (ins._config.mainConfig.needKillNpc && npcDatas.Any(x => x.scientistNPC.IsExists()) || ins._config.mainConfig.needKillBradleys && bradleys.Any(x => x.IsExists()) || ins._config.mainConfig.needKillTurrets && turrets.Any(x => x.IsExists() && x.totalAmmo > 0) || ins._config.mainConfig.needKillHeli && EventHeli.IsEventHeliAlive())
                {
                    if (shoudSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "NeedKillGuards", ins._config.prefix);

                    return false;
                }

                if (player.IsAdmin && ins._config.supportedPluginsConfig.pveMode.ignoreAdmin)
                    return true;

                if (PveModeManager.IsPveModeBlockInterract(player))
                {

                    if (shoudSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "PveMode_BlockAction", ins._config.prefix);

                    return false;
                }

                if (PveModeManager.IsPveModeBlockLooting(player))
                {
                    if (shoudSendMessages)
                        NotifyManager.SendMessageToPlayer(player, "PveMode_YouAreNoOwner", ins._config.prefix);

                    return false;
                }

                return true;
            }

            internal bool IsPlayerCanStopTrain(BasePlayer player, bool shoudSendMessages)
            {
                if (PveModeManager.IsPveModeBlockInterract(player))
                {
                    NotifyManager.SendMessageToPlayer(player, "PveMode_BlockAction", ins._config.prefix);
                    return false;
                }

                return true;
            }

            internal bool IsReverse()
            {
                return isReverse;
            }

            internal bool CanConnectToTrainWagon(TrainCar myTrainCar)
            {
                if (stopTime > 0)
                    return false;

                if (isReverse)
                {
                    if (!ins._config.mainConfig.enableFrontConnector && myTrainCar.net.ID.Value == lastWagon.net.ID.Value)
                        return false;
                    if (!ins._config.mainConfig.enableBackConnector && myTrainCar.net.ID.Value == trainEngine.net.ID.Value)
                        return false;
                }
                else
                {
                    if (!ins._config.mainConfig.enableFrontConnector && myTrainCar.net.ID.Value == trainEngine.net.ID.Value)
                        return false;
                    if (!ins._config.mainConfig.enableBackConnector && myTrainCar.net.ID.Value == lastWagon.net.ID.Value)
                        return false;
                }

                return true;
            }

            internal Vector3 GetEventPosition()
            {
                Vector3 resultPositon = Vector3.zero;

                foreach (WagonData wagonData in wagonDatas)
                    resultPositon += wagonData.trainCar.transform.position;

                return resultPositon / wagonDatas.Count;
            }

            internal void OnPlayerStartHackingCrate(int hackTime)
            {
                if (hackTime <= 0)
                    hackTime = 900;

                int minEventTime = hackTime + 30;

                if (!isEventLooted && eventTime < minEventTime)
                    eventTime = minEventTime;
            }

            internal HashSet<ulong> GetAliveTurretsNetIDS()
            {
                HashSet<ulong> turretsIDs = new HashSet<ulong>();

                foreach (AutoTurret autoTurret in turrets)
                    if (autoTurret.IsExists() && autoTurret.net != null)
                        turretsIDs.Add(autoTurret.net.ID.Value);

                return turretsIDs;
            }

            internal HashSet<ulong> GetAliveBradleysNetIDS()
            {
                HashSet<ulong> bradleysIDs = new HashSet<ulong>();

                foreach (BradleyAPC bradleyAPC in bradleys)
                    if (bradleyAPC.IsExists() && bradleyAPC.net != null)
                        bradleysIDs.Add(bradleyAPC.net.ID.Value);

                return bradleysIDs;
            }

            internal void Init(EventConfig eventConfig, bool isUnderGround)
            {
                this.eventConfig = eventConfig;
                this.isUnderGround = isUnderGround;

                StartSpawnTrain();
            }

            void StartSpawnTrain()
            {
                PositionData positionData = SpawnPositionFinder.GetSpawnPositionData(isUnderGround);

                if (positionData == null)
                {
                    EventLauncher.StopEvent();
                    return;
                }

                spawnCoroutine = ServerMgr.Instance.StartCoroutine(TrainSpawnCorountine(positionData));
            }

            IEnumerator TrainSpawnCorountine(PositionData positionData)
            {
                yield return CoroutineEx.waitForSeconds(1f);
                SpawnLocomotiveAndDriver(positionData);

                if (!trainEngine.IsExists() || !driver.IsExists())
                {
                    OnSpawnFailed();
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                ChangeSpeed(EngineSpeeds.Fwd_Hi);
                TrainCar lastTrainCar = trainEngine;
                float lastSpawnedTime = UnityEngine.Time.realtimeSinceStartup;
                Dictionary<TrainCar, WagonConfig> trainWaginsConfigs = new Dictionary<TrainCar, WagonConfig>();

                foreach (string wagonPresetName in eventConfig.wagonsPreset)
                {
                    WagonConfig wagonConfig = ins._config.wagonConfigs.FirstOrDefault(x => x.presetName == wagonPresetName);

                    if (wagonConfig == null)
                    {
                        NotifyManager.PrintError(null, "PresetNotFound_Exeption", wagonPresetName);
                        OnSpawnFailed();
                        break;
                    }

                    while (!IsSpawnFailed(lastSpawnedTime, false) && Vector3.Distance(positionData.position, lastTrainCar.transform.position) < 25)
                        yield return CoroutineEx.waitForSeconds(0.5f);

                    TrainCar newTrainCar = SpawnTrainCar(wagonConfig.prefabName, positionData, wagonConfig);
                    yield return CoroutineEx.waitForSeconds(0.5f);

                    if (IsSpawnFailed(lastSpawnedTime, false) || !newTrainCar.IsExists())
                    {
                        OnSpawnFailed();
                        break;
                    }

                    CoupleWagons(lastTrainCar, newTrainCar);
                    lastTrainCar = newTrainCar;
                    lastWagon = newTrainCar;
                    lastSpawnedTime = UnityEngine.Time.realtimeSinceStartup;
                    trainWaginsConfigs.Add(newTrainCar, wagonConfig);
                }

                yield return CoroutineEx.waitForSeconds(0.5f);

                if (IsSpawnFailed(lastSpawnedTime, true))
                    OnSpawnFailed();
                else
                    OnTrainSpawned();
            }

            void SpawnLocomotiveAndDriver(PositionData positionData)
            {
                LocomotiveConfig locomotiveConfig = ins._config.locomotiveConfigs.FirstOrDefault(x => x.presetName == eventConfig.locomotivePreset);

                if (locomotiveConfig == null)
                {
                    NotifyManager.PrintError(null, "PresetNotFound_Exeption", eventConfig.locomotivePreset);
                    return;
                }

                trainEngine = SpawnTrainCar(locomotiveConfig.prefabName, positionData, locomotiveConfig) as TrainEngine;

                if (trainEngine == null)
                {
                    NotifyManager.PrintError(null, "LocomotiveSpawn_Exeption", locomotiveConfig.prefabName);
                    return;
                }

                trainEngine.engineForce = locomotiveConfig.engineForce;
                trainEngine.maxSpeed = locomotiveConfig.maxSpeed;

                EntityFuelSystem entityFuelSystem = trainEngine.GetFuelSystem() as EntityFuelSystem;
                entityFuelSystem.cachedHasFuel = true;
                entityFuelSystem.nextFuelCheckTime = float.MaxValue;

                CreateDriver();
                trainEngine.SetFlag(BaseEntity.Flags.Reserved2, true);
            }

            void CreateDriver()
            {
                WagonData wagonData = wagonDatas.FirstOrDefault(x => x.wagonConfig is LocomotiveConfig);

                if (wagonData == null || !wagonData.trainCar.IsExists())
                    return;

                LocomotiveConfig locomotiveConfig = wagonData.wagonConfig as LocomotiveConfig;

                if (locomotiveConfig == null)
                    return;

                driver = NpcSpawnManager.SpawnScientistNpc(locomotiveConfig.driverName, trainEngine.transform.position, 1, true, true);
                trainEngine.mountPoints[0].mountable.AttemptMount(driver);
            }

            bool IsSpawnFailed(float lastSpawnTime, bool checkWagonDistance)
            {
                if (!trainEngine.IsExists() || !driver.IsExists() || wagonDatas.Any(x => !x.trainCar.IsExists()) || UnityEngine.Time.realtimeSinceStartup - lastSpawnTime > 30f)
                    return true;

                if (!checkWagonDistance)
                    return false;

                for (int i = 1; i < wagonDatas.Count; i++)
                {
                    WagonData frontWagon = wagonDatas[i];
                    WagonData backWagon = wagonDatas[i - 1];

                    if (Vector3.Angle(frontWagon.trainCar.transform.forward, backWagon.trainCar.transform.forward) > 90f)
                        return true;

                    float distance = Vector3.Distance(frontWagon.trainCar.transform.position, backWagon.trainCar.transform.position);
                    float targetDisatance = frontWagon.trainCar.ShortPrefabName.Contains("workcart") || backWagon.trainCar.ShortPrefabName.Contains("workcart") ? 13.45f : frontWagon.trainCar.ShortPrefabName.Contains("locomotive") || backWagon.trainCar.ShortPrefabName.Contains("locomotive") ? 18.38f : 16.25f;

                    if (Math.Abs(distance - targetDisatance) > 1)
                        return true;
                }

                return false;
            }

            TrainCar SpawnTrainCar(string prefabName, PositionData positionData, WagonConfig wagonConfig)
            {
                TrainCar trainCar = BuildManager.SpawnRegularEntity(prefabName, positionData.position, positionData.rotation) as TrainCar;

                if (trainCar == null)
                {
                    NotifyManager.PrintError(null, "EntitySpawn_Exeption", prefabName);
                    return null;
                }

                trainCar.CancelInvoke(trainCar.DecayTick);
                wagonDatas.Add(new WagonData(trainCar, wagonConfig, trainCar.rigidBody.mass));

                TriggerParentEnclosed triggerParentEnclosed = trainCar.GetComponentInChildren<TriggerParentEnclosed>();
                if (triggerParentEnclosed != null)
                    triggerParentEnclosed.parentSleepers = false;

                return trainCar;
            }

            void CoupleWagons(TrainCar frontWagon, TrainCar backWagon)
            {
                backWagon.coupling.frontCoupling.TryCouple(frontWagon.coupling.rearCoupling, false);
                frontWagon.coupling.rearCoupling.TryCouple(backWagon.coupling.frontCoupling, false);
            }

            void OnSpawnFailed()
            {
                KillTrain();
                trainEngine = null;
                wagonDatas.Clear();

                if (spawnCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(spawnCoroutine);

                StartSpawnTrain();
            }

            void OnTrainSpawned()
            {
                spawnCoroutine = ServerMgr.Instance.StartCoroutine(EntitiesSpawnCorountine());

                ins.Subscribes();
                eventTime = eventConfig.eventTime;
                eventCoroutine = ServerMgr.Instance.StartCoroutine(EventCorountine());
                UpdateTrainCouples();
                StartMoving();
                EventMapMarker.CreateMarker();
                NotifyManager.SendMessageToAll("StartTrain", ins._config.prefix, eventConfig.displayName, MapHelper.GridToString(MapHelper.PositionToGrid(GetEventPosition())));
            }

            void UpdateTrainCouples()
            {
                trainEngine.coupling.Uncouple(true);
                lastWagon.coupling.Uncouple(false);
            }

            IEnumerator EntitiesSpawnCorountine()
            {
                if (WagonCustomizator.IsCustomizationCanApplied())
                    wagonCustomizator = this.gameObject.AddComponent<WagonCustomizator>();

                foreach (WagonData wagonData in wagonDatas)
                {
                    LocomotiveConfig locomotiveConfig = wagonData.wagonConfig as LocomotiveConfig;

                    if (locomotiveConfig != null)
                    {
                        if (locomotiveConfig.handleBrakeConfig.isEnable)
                        {
                            electricSwitch = BuildManager.SpawnChildEntity(wagonData.trainCar, "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab", locomotiveConfig.handleBrakeConfig.location, 0, false) as ElectricSwitch;
                            electricSwitch.UpdateFromInput(10, 0);
                            electricSwitch.SetSwitch(true);
                        }

                        if (locomotiveConfig.stopTimerConfig.isEnable)
                        {
                            stopCounter = BuildManager.SpawnChildEntity(wagonData.trainCar, "assets/prefabs/deployable/playerioents/counter/counter.prefab", locomotiveConfig.stopTimerConfig.location, 0, false) as PowerCounter;
                            stopCounter.targetCounterNumber = int.MaxValue;
                        }

                        if (locomotiveConfig.eventTimerConfig.isEnable)
                        {
                            eventCounter = BuildManager.SpawnChildEntity(wagonData.trainCar, "assets/prefabs/deployable/playerioents/counter/counter.prefab", locomotiveConfig.eventTimerConfig.location, 0, false) as PowerCounter;
                            eventCounter.targetCounterNumber = int.MaxValue;
                            eventCounter.UpdateFromInput(10, 0);
                        }
                    }

                    TrainCarUnloadable trainCarUnloadable = wagonData.trainCar as TrainCarUnloadable;

                    if (trainCarUnloadable != null)
                    {
                        foreach (LootContainer lootContainer in trainCarUnloadable.GetComponentsInChildren<LootContainer>())
                            if (lootContainer.IsExists())
                                lootContainer.Kill();

                        trainCarUnloadable.lifestate = LifeState.Dead;
                    }

                    foreach (var bradleyData in wagonData.wagonConfig.brradleys)
                    {
                        BradleyConfig bradleyConfig = ins._config.bradleysConfigs.FirstOrDefault(x => x.presetName == bradleyData.Key);

                        if (bradleyConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", bradleyData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in bradleyData.Value)
                        {
                            SpawnBradley(bradleyConfig, locationConfig, wagonData.trainCar);
                            yield return null;
                        }
                    }

                    foreach (var turretData in wagonData.wagonConfig.turrets)
                    {
                        TurretConfig turretConfig = ins._config.turretConfigs.FirstOrDefault(x => x.presetName == turretData.Key);

                        if (turretConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", turretData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in turretData.Value)
                        {
                            SpawnTurret(turretConfig, locationConfig, wagonData.trainCar);
                            yield return null;
                        }
                    }

                    foreach (var samsiteData in wagonData.wagonConfig.samsites)
                    {
                        SamSiteConfig samSiteConfig = ins._config.samsiteConfigs.FirstOrDefault(x => x.presetName == samsiteData.Key);

                        if (samSiteConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", samsiteData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in samsiteData.Value)
                        {
                            SpawnSamSite(samSiteConfig, locationConfig, wagonData.trainCar);
                            yield return null;
                        }
                    }

                    foreach (var crateData in wagonData.wagonConfig.crates)
                    {
                        CrateConfig crateConfig = ins._config.crateConfigs.FirstOrDefault(x => x.presetName == crateData.Key);

                        if (crateConfig == null)
                        {
                            NotifyManager.PrintError(null, "PresetNotFound_Exeption", crateData.Key);
                            continue;
                        }

                        foreach (LocationConfig locationConfig in crateData.Value)
                        {
                            SpawnCrate(crateConfig, locationConfig, wagonData.trainCar);
                            yield return null;
                        }
                    }

                    foreach (var npcData in wagonData.wagonConfig.npcs)
                    {
                        foreach (LocationConfig locationConfig in npcData.Value)
                        {
                            SpawnNpc(npcData.Key, locationConfig, wagonData.trainCar);
                            yield return null;
                        }
                    }

                    WagonCustomizationData wagonCustomizationData = WagonCustomizator.GetWagonCustomizationData(wagonData.trainCar.ShortPrefabName, wagonData.wagonConfig.presetName);

                    if (wagonCustomizationData == null || !wagonCustomizationData.isBaseDecorDisable)
                    {
                        foreach (var decorEntityData in wagonData.wagonConfig.decors)
                        {
                            foreach (LocationConfig locationConfig in decorEntityData.Value)
                            {
                                SpawnDecorEntity(decorEntityData.Key, locationConfig, wagonData.trainCar);
                                yield return null;
                            }
                        }
                    }
                    if (wagonCustomizator != null)
                        wagonCustomizator.DecorateWagon(wagonData.trainCar, wagonCustomizationData);

                    if (trainCarUnloadable != null)
                        foreach (LootContainer lootContainer in trainCarUnloadable.GetComponentsInChildren<LootContainer>())
                            if (lootContainer != null)
                                lootContainer.inventory.SetLocked(false);
                }

                if (eventConfig.heliPreset != "" && !isUnderGround)
                {
                    HeliConfig heliConfig = ins._config.heliConfigs.FirstOrDefault(x => x.presetName == eventConfig.heliPreset);

                    if (heliConfig == null)
                        NotifyManager.PrintError(null, "PresetNotFound_Exeption", eventConfig.heliPreset);
                    else
                        EventHeli.SpawnHeli(heliConfig);
                }

                yield return CoroutineEx.waitForSeconds(1);

                if (wagonCustomizator != null)
                    wagonCustomizator.OnTrainSpawned();
            }

            void SpawnBradley(BradleyConfig bradleyConfig, LocationConfig locationConfig, TrainCar trainCar)
            {
                Vector3 localPosition = locationConfig.position.ToVector3();
                Vector3 localRotation = locationConfig.rotation.ToVector3();

                BradleyAPC bradleyAPC = CustomBradley.SpawnCustomBradley(localPosition, localRotation, trainCar, bradleyConfig);
                BuildManager.UpdateEntityMaxHealth(bradleyAPC, bradleyConfig.hp);

                bradleyAPC.myRigidBody.isKinematic = true;
                bradleyAPC.maxCratesToSpawn = bradleyConfig.crateCount;
                bradleyAPC.viewDistance = bradleyConfig.viewDistance;
                bradleyAPC.searchRange = bradleyConfig.searchDistance;
                bradleyAPC.coaxAimCone *= bradleyConfig.coaxAimCone;
                bradleyAPC.coaxFireRate *= bradleyConfig.coaxFireRate;
                bradleyAPC.coaxBurstLength = bradleyConfig.coaxBurstLength;
                bradleyAPC.nextFireTime = bradleyConfig.nextFireTime;
                bradleyAPC.topTurretFireRate = bradleyConfig.topTurretFireRate;
                bradleyAPC.ScientistSpawnCount = 0;
                bradleyAPC.skinID = 755447;
                bradleys.Add(bradleyAPC);

                bradleyAPC.Invoke(() =>
                {
                    if (trainCar != null)
                    {
                        bradleyAPC.myRigidBody = trainCar.rigidBody;

                        foreach (var a in bradleyAPC.GetComponentsInChildren<TriggerBase>())
                            a.enabled = false;

                        foreach (var a in bradleyAPC.GetComponentsInChildren<WheelCollider>())
                            DestroyImmediate(a);

                        bradleyAPC.rightWheels = new WheelCollider[0];
                        bradleyAPC.leftWheels = new WheelCollider[0];
                    }
                }, 1f);
            }

            void SpawnTurret(TurretConfig turretConfig, LocationConfig locationConfig, TrainCar trainCar)
            {
                AutoTurret autoTurret = BuildManager.SpawnChildEntity(trainCar, "assets/prefabs/npc/autoturret/autoturret_deployed.prefab", locationConfig, 0, false) as AutoTurret;
                BuildManager.UpdateEntityMaxHealth(autoTurret, turretConfig.hp);

                autoTurret.inventory.Insert(ItemManager.CreateByName(turretConfig.shortNameWeapon));
                autoTurret.inventory.Insert(ItemManager.CreateByName(turretConfig.shortNameAmmo, turretConfig.countAmmo));

                autoTurret.UpdateFromInput(IsAgressive() ? 10 : 0, 0);
                autoTurret.isLootable = false;
                autoTurret.dropFloats = false;
                autoTurret.dropsLoot = ins._config.mainConfig.isTurretDropWeapon;
                turrets.Add(autoTurret);

                if (turretConfig.targetLossRange != 0)
                    autoTurret.sightRange = turretConfig.targetLossRange;

                if (turretConfig.targetDetectionRange != 0 && autoTurret.targetTrigger != null)
                {
                    SphereCollider sphereCollider = autoTurret.targetTrigger.GetComponent<SphereCollider>();

                    if (sphereCollider != null)
                        sphereCollider.radius = turretConfig.targetDetectionRange;
                }

                TurretOptimizer.Attach(autoTurret, turretConfig.targetDetectionRange == 0 ? 30 : turretConfig.targetDetectionRange);
            }

            void SpawnSamSite(SamSiteConfig samSiteConfig, LocationConfig locationConfig, TrainCar trainCar)
            {
                SamSite samSite = BuildManager.SpawnChildEntity(trainCar, "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab", locationConfig, 0, false) as SamSite;
                BuildManager.UpdateEntityMaxHealth(samSite, samSiteConfig.hp);

                samSite.inventory.Insert(ItemManager.CreateByName("ammo.rocket.sam", samSiteConfig.countAmmo));
                samSite.UpdateFromInput(IsAgressive() ? 100 : 0, 0);
                samSite.isLootable = false;
                samSite.dropFloats = false;
                samSite.dropsLoot = false;
                samsites.Add(samSite);
            }

            void SpawnDecorEntity(string prefabName, LocationConfig locationConfig, TrainCar trainCar)
            {
                BaseCombatEntity baseCombatEntity = BuildManager.SpawnChildEntity(trainCar, prefabName, locationConfig, 0, false) as BaseCombatEntity;

                if (baseCombatEntity == null)
                {
                    NotifyManager.PrintError(null, "EntitySpawn_Exeption", prefabName);
                    return;
                }

                baseCombatEntity.lifestate = BaseCombatEntity.LifeState.Dead;
                baseCombatEntity.SendNetworkUpdate();
            }

            void SpawnCrate(CrateConfig crateConfig, LocationConfig locationConfig, TrainCar trainCar)
            {
                StorageContainer crateEntity = BuildManager.CreateEntity(crateConfig.prefab, trainCar.transform.position, trainCar.transform.rotation, crateConfig.skin, false) as StorageContainer;
                if (crateEntity == null)
                {
                    NotifyManager.PrintError(null, "EntitySpawn_Exeption", crateConfig.prefab);
                    return;
                }
                BuildManager.DestroyUnnessesaryComponents(crateEntity);
                BuildManager.SetParent(trainCar, crateEntity, locationConfig.position.ToVector3(), locationConfig.rotation.ToVector3());
                LootManager.AddContainerData(crateEntity, crateConfig);
                crateEntity.Spawn();
                LootContainer lootContainer = crateEntity as LootContainer;

                if (lootContainer != null)
                    LootManager.UpdateLootContainer(lootContainer, crateConfig);
                else
                    LootManager.UpdateStorageContainer(crateEntity, crateConfig);

                containers.Add(crateEntity);
            }

            void SpawnNpc(string npcPresetName, LocationConfig locationConfig, TrainCar trainCar)
            {
                ScientistNPC scientistNPC = CreateChildNpc(npcPresetName, trainCar, locationConfig, 1);

                if (scientistNPC == null)
                    return;

                NpcData npcData = new NpcData(scientistNPC, npcPresetName, locationConfig, trainCar);
                npcDatas.Add(npcData);
            }

            void ReplaceByRoamNpcs()
            {
                if ((isUnderGround && !ins._config.mainConfig.isNpcJumpInSubway) || (!isUnderGround && !ins._config.mainConfig.isNpcJumpOnSurface))
                    return;

                foreach (NpcData npcData in npcDatas)
                {
                    if (!npcData.scientistNPC.IsExists() || npcData.isRoaming)
                        continue;

                    int scale = npcData.scientistNPC.transform.localPosition.x < 0 ? -1 : 1;
                    Vector3 spawnPosition = npcData.scientistNPC.transform.position + npcData.trainCar.transform.right * scale * 2.5f;
                    NavMeshHit navMeshHit;

                    if (!PositionDefiner.GetNavmeshInPoint(spawnPosition, 2, out navMeshHit))
                        continue;

                    spawnPosition = navMeshHit.position;
                    Vector3 vector3 = PositionDefiner.GetGroundPositionInPoint(spawnPosition);
                    if (Math.Abs(vector3.y - spawnPosition.y) > 0.5f)
                        continue;

                    float healthFraction = npcData.scientistNPC.healthFraction;
                    npcData.scientistNPC.Kill();
                    npcData.scientistNPC = NpcSpawnManager.SpawnScientistNpc(npcData.presetName, spawnPosition, healthFraction, false);
                    npcData.isRoaming = true;
                }
            }

            void ReplaceByStaticNpcs()
            {
                foreach (NpcData npcData in npcDatas)
                {
                    if (!npcData.scientistNPC.IsExists() || !npcData.isRoaming)
                        continue;

                    float healthFraction = npcData.scientistNPC.healthFraction;
                    npcData.scientistNPC.Kill();
                    npcData.scientistNPC = CreateChildNpc(npcData.presetName, npcData.trainCar, npcData.locationConfig, healthFraction);
                    npcData.isRoaming = false;
                }
            }

            ScientistNPC CreateChildNpc(string presetName, TrainCar trainCar, LocationConfig locationConfig, float healthFraction)
            {
                Vector3 localPosition = locationConfig.position.ToVector3();
                Vector3 localRotation = locationConfig.rotation.ToVector3();
                Vector3 spawnPosition = PositionDefiner.GetGlobalPosition(trainCar.transform, localPosition);
                ScientistNPC scientistNPC = NpcSpawnManager.SpawnScientistNpc(presetName, spawnPosition, healthFraction, true);

                if (scientistNPC == null)
                    return null;

                BuildManager.SetParent(trainCar, scientistNPC, localPosition, localRotation);
                return scientistNPC;
            }

            IEnumerator EventCorountine()
            {
                while (eventTime > 0 || (!isEventLooted && ins._config.mainConfig.dontStopEventIfPlayerInZone && ZoneController.IsAnyPlayerInEventZone()))
                {
                    if (ins._config.notifyConfig.timeNotifications.Contains(eventTime))
                        NotifyManager.SendMessageToAll("RemainTime", ins._config.prefix, eventTime);

                    if (wagonDatas.Any(x => !x.trainCar.IsExists()))
                        break;

                    if (stopTime > 0)
                    {
                        stopTime--;

                        if (stopTime <= 0)
                        {
                            isReverse = false;
                            StartMoving();
                        }
                    }

                    if (!IsStopped() && agressiveTime > 0)
                    {
                        agressiveTime--;

                        if (agressiveTime <= 0 && !IsAgressive())
                            MakeNoAgressive();
                    }

                    if (stopTime > 0 && !ZoneController.IsZoneCreated() && Math.Abs(trainEngine.GetSpeed()) <= 1)
                    {
                        OnTrainFullStop();
                    }

                    UpdateElectrickCounters();
                    CheckUnderGround();
                    CheckStuck();

                    if (eventTime % 30 == 0)
                    {
                        UpdateCratesvisibility();

                        if (eventConfig.eventTime - eventTime > 30)
                            EventPassingCheck();
                    }

                    if (eventTime > 0)
                        eventTime--;

                    yield return CoroutineEx.waitForSeconds(1);
                }

                EventLauncher.StopEvent();
            }

            internal void EventPassingCheck()
            {
                if (isEventLooted)
                    return;

                LootManager.UpdateCountOfUnlootedCrates();
                int countOfUnlootedCrates = LootManager.GetCountOfUnlootedCrates();

                if (countOfUnlootedCrates == 0)
                {
                    isEventLooted = true;
                    StopMoving();

                    if (ins._config.mainConfig.killTrainAfterLoot && eventTime > ins._config.mainConfig.endAfterLootTime)
                        eventTime = ins._config.mainConfig.endAfterLootTime;

                    NotifyManager.SendMessageToAll("Looted", ins._config.prefix, eventConfig.displayName);
                }
            }

            internal void OnSwitchToggled(BasePlayer player)
            {
                MakeAgressive();

                if (electricSwitch.IsOn())
                    StartMoving();
                else if (stopTime == 0)
                    OnPlayerStoppedTrain(player);
            }

            internal void OnDriverKilled(BasePlayer attacker)
            {
                if (stopTime == 0)
                    OnPlayerStoppedTrain(attacker);
            }

            internal void OnTrainAttacked(BasePlayer attacker)
            {
                if (stopTime == 0 && ins._config.mainConfig.stopTrainAfterReceivingDamage)
                    OnPlayerStoppedTrain(attacker);

                if (stopTime > 0 && ins._config.mainConfig.isRestoreStopTimeAfterDamageOrLoot)
                    stopTime = eventConfig.stopTime;

                MakeAgressive();
            }

            internal void MakeAgressive()
            {
                if (ins._config.mainConfig.isAggressive)
                    return;

                if (agressiveTime > 0)
                {
                    agressiveTime = ins._config.mainConfig.agressiveTime;
                    return;
                }

                agressiveTime = ins._config.mainConfig.agressiveTime;

                foreach (AutoTurret autoTurret in turrets)
                    if (autoTurret.IsExists())
                        autoTurret.UpdateFromInput(10, 0);

                foreach (SamSite samSite in samsites)
                    if (samSite.IsExists())
                        samSite.UpdateFromInput(100, 0);
            }

            void MakeNoAgressive()
            {
                if (ins._config.mainConfig.isAggressive)
                    return;

                foreach (AutoTurret autoTurret in turrets)
                    if (autoTurret.IsExists())
                        autoTurret.UpdateFromInput(0, 0);

                foreach (SamSite samSite in samsites)
                    if (samSite.IsExists())
                        samSite.UpdateFromInput(0, 0);
            }

            void OnPlayerStoppedTrain(BasePlayer player)
            {
                if (eventConfig.stopTime <= 0)
                    return;

                if (!IsPlayerCanStopTrain(player, true))
                    return;

                if (player != null)
                    NotifyManager.SendMessageToAll("PlayerStopTrain", ins._config.prefix, player.displayName);

                lastStopper = player;
                StopMoving();
            }

            void StartMoving()
            {
                if (!driver.IsExists())
                {
                    if (ins._config.mainConfig.reviveTrainDriver)
                        CreateDriver();
                    else
                        return;
                }

                if (isEventLooted)
                    return;

                stopTime = 0;
                ChangeSpeed(isReverse ? EngineSpeeds.Rev_Hi : EngineSpeeds.Fwd_Hi);

                if (electricSwitch.IsExists())
                    electricSwitch.SetSwitch(true);

                if (stopCounter.IsExists())
                    stopCounter.UpdateFromInput(0, 0);

                ZoneController.TryDeleteZone();
                ReplaceByStaticNpcs();
                UpdateTrainCouples();

                lastGoodPositionTime = UnityEngine.Time.realtimeSinceStartup;
                lastGoodPosition = trainEngine.transform.position;

                foreach (WagonData wagonData in wagonDatas)
                    wagonData.trainCar.rigidBody.mass = wagonData.mass;
            }

            void StopMoving()
            {
                if (eventConfig.stopTime <= 0)
                    return;

                stopTime = eventConfig.stopTime;
                ChangeSpeed(EngineSpeeds.Zero);

                if (electricSwitch.IsExists())
                    electricSwitch.SetSwitch(false);

                UpdateElectrickCounters();

                if (stopCounter.IsExists())
                    stopCounter.UpdateFromInput(10, 0);

                UpdateTrainCouples();

                foreach (WagonData wagonData in wagonDatas)
                    wagonData.trainCar.rigidBody.mass = float.MaxValue;
            }

            void OnTrainFullStop()
            {
                ReplaceByRoamNpcs();
                UpdateCratesvisibility();
                ZoneController.CreateZone(ins._config.supportedPluginsConfig.pveMode.ownerIsStopper ? lastStopper : null);
                isReverse = false;
                lastStopper = null;
            }

            void UpdateCratesvisibility()
            {
                foreach (StorageContainer storageContainer in containers)
                {
                    if (storageContainer.IsExists() && (storageContainer is HackableLockedCrate || storageContainer is SupplyDrop))
                    {
                        storageContainer.limitNetworking = true;
                        storageContainer.limitNetworking = false;
                    }
                }
            }

            void UpdateElectrickCounters()
            {
                if (stopCounter.IsExists())
                {
                    stopCounter.counterNumber = stopTime;
                    stopCounter.SendNetworkUpdate();
                }

                if (eventCounter.IsExists())
                {
                    eventCounter.counterNumber = eventTime;
                    eventCounter.SendNetworkUpdate();
                }
            }

            void CheckUnderGround()
            {
                if (stopTime > 0)
                    return;

                isUnderGround = trainEngine.transform.position.y < 0;
            }

            void CheckStuck()
            {
                if (stopTime > 0)
                    return;

                float distanceToLastGoodPosition = Vector3.Distance(trainEngine.transform.position, lastGoodPosition);

                if (distanceToLastGoodPosition > 1)
                {
                    lastGoodPositionTime = UnityEngine.Time.realtimeSinceStartup;
                    lastGoodPosition = trainEngine.transform.position;
                    return;
                }

                if (UnityEngine.Time.realtimeSinceStartup - lastGoodPositionTime >= 10)
                {
                    isReverse = !isReverse;
                    StartMoving();
                }
            }

            void ChangeSpeed(EngineSpeeds engineSpeeds)
            {
                if (!trainEngine.engineController.IsStartingOrOn)
                    trainEngine.engineController.TryStartEngine(driver);

                trainEngine.SetThrottle(engineSpeeds);
            }

            internal void DeleteController()
            {
                if (eventCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(eventCoroutine);

                if (spawnCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(spawnCoroutine);

                if (wagonCustomizator != null)
                    wagonCustomizator.DestroyCustomizator();

                KillTrain();
                GameObject.Destroy(this);
            }

            void KillTrain()
            {
                foreach (WagonData wagonData in wagonDatas)
                    if (wagonData.trainCar.IsExists())
                        wagonData.trainCar.DismountAllPlayers();

                foreach (AutoTurret autoTurret in turrets)
                {
                    if (autoTurret.IsExists())
                    {
                        AutoTurret.interferenceUpdateList.Remove(autoTurret);
                        autoTurret.Kill();
                    }
                }

                if (driver.IsExists())
                {
                    driver.Kill();
                }

                foreach (NpcData npcData in npcDatas)
                    if (npcData.scientistNPC.IsExists())
                        npcData.scientistNPC.Kill();

                foreach (WagonData wagonData in wagonDatas)
                    if (wagonData.trainCar.IsExists())
                        wagonData.trainCar.Kill();

                turrets.Clear();
                npcDatas.Clear();
                wagonDatas.Clear();
            }

            class WagonData
            {
                internal TrainCar trainCar;
                internal WagonConfig wagonConfig;
                internal float mass;

                internal WagonData(TrainCar trainCar, WagonConfig wagonConfig, float mass)
                {
                    this.trainCar = trainCar;
                    this.wagonConfig = wagonConfig;
                    this.mass = mass;
                }
            }

            class NpcData
            {
                internal ScientistNPC scientistNPC;
                internal string presetName;
                internal LocationConfig locationConfig;
                internal TrainCar trainCar;
                internal bool isRoaming;

                internal NpcData(ScientistNPC scientistNPC, string presetName, LocationConfig locationConfig, TrainCar trainCar)
                {
                    this.scientistNPC = scientistNPC;
                    this.presetName = presetName;
                    this.locationConfig = locationConfig;
                    this.trainCar = trainCar;
                }
            }
        }

        class WagonCustomizator : FacepunchBehaviour
        {
            static CustomizeProfile customizeProfile;
            static HashSet<string> fireEntities = new HashSet<string>
                {
                    "hobobarrel.deployed",
                    "largecandleset",
                    "jackolantern.happy",
                    "skullspikes.candles.deployed",
                    "skull_fire_pit"
                };

            Coroutine updateCoroutine;
            HashSet<BaseEntity> lightEntities = new HashSet<BaseEntity>();
            HashSet<NeonSign> neonSigns = new HashSet<NeonSign>();
            float lastNeonUpdateTime;
            ItemThrower itemThrower;
            CustomFirework customFirework;
            bool isLightEnable;

            internal static void LoadCurrentCustomizationProfile()
            {
                if (ins._config.customizationConfig.profileName == "")
                    return;

                customizeProfile = LoadProfile(ins._config.customizationConfig.profileName);

                if (!IsCustomizationCanApplied())
                {
                    NotifyManager.PrintError(null, "DataFileNotFound_Exeption", ins._config.customizationConfig.profileName);
                    return;
                }
            }

            internal static bool IsCustomizationCanApplied()
            {
                return customizeProfile != null && customizeProfile.wagonPresets != null && customizeProfile.wagonPresets.Count > 0;
            }

            static CustomizeProfile LoadProfile(string profileName)
            {
                string filePath = $"{ins.Name}/{profileName}";
                return Interface.Oxide.DataFileSystem.ReadObject<CustomizeProfile>(filePath);
            }

            internal static JArray GetCustomizeNpcWearSet()
            {
                if (customizeProfile == null || customizeProfile.npcPresets == null)
                    return null;

                CustomizeNpcConfig randomCustomizeNpcConfig = customizeProfile.npcPresets.GetRandom();

                if (randomCustomizeNpcConfig == null)
                    return null;

                return new JArray
                {
                    randomCustomizeNpcConfig.customWearItems.Select(x => new JObject
                    {
                        ["ShortName"] = x.shortName,
                        ["SkinID"] = x.skinID
                    })
                };
            }

            internal void OnTrainSpawned()
            {
                updateCoroutine = ServerMgr.Instance.StartCoroutine(UpdateCoroutine());
            }

            internal void DecorateWagon(TrainCar trainCar, WagonCustomizationData wagonCustomizationData)
            {
                if (wagonCustomizationData == null)
                    return;

                if (wagonCustomizationData.decorEntityConfigs != null)
                {
                    List<DecorEntityConfig> decorEntityConfigList = wagonCustomizationData.decorEntityConfigs.ToList();

                    for (int i = 0; i < decorEntityConfigList.Count; i++)
                    {
                        DecorEntityConfig decorEntityConfig = decorEntityConfigList[i];
                        if (i > 0)
                        {
                            DecorEntityConfig previousDecorConfig = decorEntityConfigList[i - 1];
                            if (previousDecorConfig.position == decorEntityConfig.position && previousDecorConfig.rotation == decorEntityConfig.rotation && previousDecorConfig.prefabName == decorEntityConfig.prefabName)
                                continue;
                        }
                        SpawnDecorEntity(decorEntityConfig, trainCar);
                    }
                }

                if (wagonCustomizationData.signConfigs != null)
                    foreach (DecorEntityConfig decorEntityConfig in wagonCustomizationData.signConfigs)
                        SpawnDecorEntity(decorEntityConfig, trainCar);

                TrainEngine trainEngine = trainCar as TrainEngine;

                if (trainEngine != null)
                    SpawnCustomCanonShell(trainEngine);
            }

            internal static WagonCustomizationData GetWagonCustomizationData(string shortPrefabName, string wagonPresetName)
            {
                if (customizeProfile == null || customizeProfile.wagonPresets == null)
                    return null;

                return customizeProfile.wagonPresets.FirstOrDefault(x => x.isEnabled && x.shortPrefabName == shortPrefabName && !x.wagonExceptions.Contains(wagonPresetName) && (x.wagonOnly == null || x.wagonOnly.Count == 0 || x.wagonOnly.Contains(wagonPresetName)));
            }

            void SpawnDecorEntity(DecorEntityConfig decorEntityConfig, TrainCar trainCar)
            {
                Vector3 localPosition = decorEntityConfig.position.ToVector3();
                Vector3 localRotation = decorEntityConfig.rotation.ToVector3();
                bool isNoDecorEntity = decorEntityConfig.prefabName.Contains("neon") || decorEntityConfig.prefabName.Contains("skullspikes");

                BaseEntity entity = BuildManager.SpawnChildEntity(trainCar, decorEntityConfig.prefabName, localPosition, localRotation, decorEntityConfig.skin, !isNoDecorEntity);

                if (entity != null)
                    UpdateDecorEntity(entity, decorEntityConfig);
            }

            void UpdateDecorEntity(BaseEntity entity, DecorEntityConfig decorEntityConfig)
            {
                entity.SetFlag(BaseEntity.Flags.Busy, true);
                entity.SetFlag(BaseEntity.Flags.Locked, true);

                NeonSign neonSign = entity as NeonSign;
                if (neonSign != null)
                    UpdateNeonSign(neonSign, decorEntityConfig);

                else
                    UpdateCommonEntities(entity);
            }

            void UpdateNeonSign(NeonSign neonSign, DecorEntityConfig decorEntityConfig)
            {
                PaintedSignConfig paintedSignConfig = decorEntityConfig as PaintedSignConfig;

                if (paintedSignConfig != null)

                    if (ins._config.customizationConfig.isNeonSignsEnable)
                        SignPainter.UpdateNeonSign(neonSign, paintedSignConfig.imageName);

                neonSigns.Add(neonSign);
            }

            void UpdateCommonEntities(BaseEntity entity)
            {
                if (entity.ShortPrefabName == "skulltrophy.deployed")
                    entity.SetFlag(BaseEntity.Flags.Reserved1, true);

                if (ins._config.customizationConfig.isBoilersEnable && entity.ShortPrefabName == "cursedcauldron.deployed")
                    UpdateLightEntity(entity);

                else if (ins._config.customizationConfig.isElectricFurnacesEnable && entity.ShortPrefabName == "electricfurnace.deployed")
                    UpdateLightEntity(entity);

                else if (ins._config.customizationConfig.isFireEnable && IsEntityFire(entity.ShortPrefabName))
                    UpdateLightEntity(entity);

                else if (entity.ShortPrefabName == "industrial.wall.lamp.red.deployed")
                    UpdateLightEntity(entity);

                else if (entity.ShortPrefabName == "xmas_tree.deployed")
                    DecorateChristmasTree(entity);

                if (entity.ShortPrefabName == "wooden_crate_gingerbread" || entity.ShortPrefabName == "gingerbread_barricades_snowman" || entity.ShortPrefabName == "gingerbread_barricades_house" || entity.ShortPrefabName == "gingerbread_barricades_tree")
                    entity.gameObject.layer = 12;
            }

            bool IsEntityFire(string shortPrefabName)
            {
                return fireEntities.Contains(shortPrefabName);
            }

            void UpdateLightEntity(BaseEntity entity)
            {
                if (ins._config.customizationConfig.isLightOnlyAtNight)
                    lightEntities.Add(entity);
                else
                    entity.SetFlag(BaseEntity.Flags.On, true);
            }

            void DecorateChristmasTree(BaseEntity christmasTree)
            {
                christmasTree.SetFlag(BaseEntity.Flags.Reserved1, true);
                christmasTree.SetFlag(BaseEntity.Flags.Reserved2, true);
                christmasTree.SetFlag(BaseEntity.Flags.Reserved3, true);
                christmasTree.SetFlag(BaseEntity.Flags.Reserved4, true);
                christmasTree.SetFlag(BaseEntity.Flags.Reserved5, true);
                christmasTree.SetFlag(BaseEntity.Flags.Reserved6, true);
                christmasTree.SetFlag(BaseEntity.Flags.Reserved7, true);
            }

            void SpawnCustomCanonShell(TrainEngine trainEngine)
            {
                if (!ins._config.customizationConfig.giftCannonSetting.isGiftCannonEnable && !ins._config.customizationConfig.fireworksSettings.isFireworksOn)
                    return;

                if (itemThrower != null || customFirework != null)
                    return;

                Vector3 canonShellPosition = trainEngine.ShortPrefabName == "locomotive.entity" ? new Vector3(0, 4.649f, 4.478f) : new Vector3(0.719f, 3.814f, 3.513f);
                BuildManager.SpawnChildEntity(trainEngine, "assets/prefabs/deployable/fireworks/mortarpattern.prefab", canonShellPosition, Vector3.zero, 0, true);

                if (ins._config.customizationConfig.giftCannonSetting.isGiftCannonEnable)
                    itemThrower = new ItemThrower(trainEngine, canonShellPosition);

                if (ins._config.customizationConfig.fireworksSettings.isFireworksOn && customizeProfile.fireworkConfigs != null && customizeProfile.fireworkConfigs.Count > 0)
                    customFirework = new CustomFirework(trainEngine, canonShellPosition);
            }

            IEnumerator UpdateCoroutine()
            {
                while (EventLauncher.IsEventActive())
                {
                    PeriodicUpdateOfCustomizationEntities();
                    yield return CoroutineEx.waitForSeconds(1);
                }
            }

            void PeriodicUpdateOfCustomizationEntities()
            {
                UptateTimeLight();
                TryUpdateSignEntities();

                if (itemThrower != null)
                    itemThrower.UpdateItemThrower();

                if (customFirework != null)
                    customFirework.UpdateCustomFirework();
            }

            void UptateTimeLight()
            {
                if (ins._config.customizationConfig.isLightOnlyAtNight)
                {

                    if (isLightEnable)
                    {
                        if (ConVar.Env.time > 9 && ConVar.Env.time < 18)
                        {
                            TurnLight(false);
                        }
                    }
                    else
                    {
                        if (ConVar.Env.time < 9 || ConVar.Env.time > 18)
                        {
                            TurnLight(true);
                        }
                    }
                }
            }

            void TurnLight(bool enable)
            {
                isLightEnable = enable;

                foreach (BaseEntity entity in lightEntities)
                    if (entity.IsExists())
                        entity.SetFlag(BaseEntity.Flags.On, enable);
            }

            void TryUpdateSignEntities()
            {
                if (ins._config.customizationConfig.isNeonSignsEnable && Time.realtimeSinceStartup - lastNeonUpdateTime > 30)
                {
                    lastNeonUpdateTime = Time.realtimeSinceStartup;
                    foreach (NeonSign neonSign in neonSigns)
                    {
                        if (neonSign.IsExists())
                        {
                            neonSign.limitNetworking = true;
                            neonSign.limitNetworking = false;
                        }
                    }
                }
            }

            internal void DestroyCustomizator()
            {
                if (updateCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(updateCoroutine);
            }

            class ItemThrower
            {
                TrainCar trainCar;
                Vector3 localThrowingPosition;
                float lastThrowItemTime = Time.realtimeSinceStartup;
                float nextItemThrowDelay;

                internal ItemThrower(TrainCar trainCar, Vector3 localThrowingPosition)
                {
                    this.trainCar = trainCar;
                    this.localThrowingPosition = localThrowingPosition;

                    nextItemThrowDelay = UnityEngine.Random.Range(ins._config.customizationConfig.giftCannonSetting.minTimeBetweenItems, ins._config.customizationConfig.giftCannonSetting.maxTimeBetweenItems);
                }

                internal void UpdateItemThrower()
                {
                    if (Time.realtimeSinceStartup - lastThrowItemTime > nextItemThrowDelay)
                    {
                        lastThrowItemTime = Time.realtimeSinceStartup;
                        nextItemThrowDelay = UnityEngine.Random.Range(ins._config.customizationConfig.giftCannonSetting.minTimeBetweenItems, ins._config.customizationConfig.giftCannonSetting.maxTimeBetweenItems);
                        ThrowItem();
                    }
                }

                void ThrowItem()
                {
                    LootItemConfig itemForThrow = GetItemForThrowing();

                    if (itemForThrow == null)
                        return;

                    Item dropppedItem = CreateItemByItemConfig(itemForThrow);

                    if (dropppedItem != null)
                    {
                        Vector3 startPosition = PositionDefiner.GetGlobalPosition(trainCar.transform, localThrowingPosition) + Vector3.up;
                        Vector3 velocity = trainCar.GetWorldVelocity() + new Vector3(UnityEngine.Random.Range(-5, 5), UnityEngine.Random.Range(7, 20), UnityEngine.Random.Range(-5, 5));
                        Quaternion randomItemRotation = Quaternion.Euler(new Vector3(UnityEngine.Random.Range(0, 360), UnityEngine.Random.Range(0, 360), UnityEngine.Random.Range(0, 360)));

                        dropppedItem.Drop(startPosition, velocity, randomItemRotation);
                    }
                }

                LootItemConfig GetItemForThrowing()
                {
                    int counter = 0;

                    while (counter < 100)
                    {
                        LootItemConfig itemConfig = ins._config.customizationConfig.giftCannonSetting.items.GetRandom();

                        if (UnityEngine.Random.Range(0.0f, 100.0f) <= itemConfig.chance)
                            return itemConfig;
                        counter++;
                    }

                    return ins._config.customizationConfig.giftCannonSetting.items.Max(x => x.chance);
                }

                Item CreateItemByItemConfig(LootItemConfig itemConfig)
                {
                    int amount = UnityEngine.Random.Range(itemConfig.minAmount, itemConfig.maxAmount + 1);

                    Item newItem;
                    if (itemConfig.isBlueprint)
                    {
                        newItem = ItemManager.CreateByName("blueprintbase");

                        ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemConfig.shortname);
                        if (itemDefinition != null)
                            newItem.blueprintTarget = itemDefinition.itemid;
                    }
                    else
                    {
                        newItem = ItemManager.CreateByName(itemConfig.shortname, amount, itemConfig.skin);
                    }

                    return newItem;
                }
            }

            class CustomFirework
            {
                TrainCar trainCar;
                PatternFirework patternFirework;
                Vector3 localFireworkPosition;
                float lastFireTime = Time.realtimeSinceStartup;

                internal CustomFirework(TrainCar trainCar, Vector3 localFireworkPosition)
                {
                    this.trainCar = trainCar;
                    this.localFireworkPosition = localFireworkPosition;
                }

                internal void UpdateCustomFirework()
                {
                    if ((!ins._config.customizationConfig.fireworksSettings.isNighFireworks || IsNightNow()) && Time.realtimeSinceStartup - lastFireTime >= ins._config.customizationConfig.fireworksSettings.timeBetweenFireworks)
                    {
                        lastFireTime = Time.realtimeSinceStartup;
                        ActivateFireWork();
                    }
                }

                static bool IsNightNow()
                {
                    return ConVar.Env.time < 7 || ConVar.Env.time > 20;
                }

                void ActivateFireWork()
                {
                    if (patternFirework.IsExists())
                        patternFirework.Kill();

                    HashSet<FireworkConfig> suitableFireworkConfigs = customizeProfile.fireworkConfigs.Where(x => x.isEnabled);
                    if (suitableFireworkConfigs == null)
                        return;

                    FireworkConfig fireworkConfig = suitableFireworkConfigs.ToList().GetRandom();
                    if (fireworkConfig == null)
                        return;

                    patternFirework = BuildManager.SpawnChildEntity(trainCar, "assets/prefabs/deployable/fireworks/mortarpattern.prefab", localFireworkPosition, Vector3.zero, 0, false) as PatternFirework;
                    UpdateFireworkPaint(fireworkConfig);
                    patternFirework.TryLightFuse();
                }

                void UpdateFireworkPaint(FireworkConfig fireworkConfig)
                {
                    patternFirework.maxRepeats = ins._config.customizationConfig.fireworksSettings.numberShotsInSalvo;

                    patternFirework.Design?.Dispose();
                    patternFirework.MaxStars = 1000;
                    patternFirework.Design = new ProtoBuf.PatternFirework.Design();
                    patternFirework.Design.stars = new List<Star>();
                    Vector3 color = fireworkConfig.color.ToVector3();
                    foreach (string coord in fireworkConfig.paintCoordinates)
                    {
                        Vector3 position = coord.ToVector3();

                        ProtoBuf.PatternFirework.Star star = new ProtoBuf.PatternFirework.Star
                        {
                            color = new Color(color.x, color.y, color.z),
                            position = new Vector2(position.x, position.y)
                        };

                        patternFirework.Design.stars.Add(star);
                    }
                }
            }

            internal static class MapSaver
            {
                static Dictionary<string, string> colliderPrefabNames = new Dictionary<string, string>
                {
                    ["fence_a"] = "assets/prefabs/misc/xmas/icewalls/icewall.prefab",
                    ["christmas_present_LOD0"] = "assets/prefabs/misc/xmas/sleigh/presentdrop.prefab",
                    ["snowman_LOD1"] = "assets/prefabs/misc/xmas/snowman/snowman.deployed.prefab",
                    ["giftbox_LOD0"] = "assets/prefabs/misc/xmas/giftbox/giftbox_loot.prefab"
                };

                internal static void CreateOrAddNewWagonToData(string customizationPresetName, string wagonShortPrefabName)
                {
                    CustomizeProfile newCustomizeProfile = LoadProfile(customizationPresetName);
                    if (newCustomizeProfile == null || newCustomizeProfile.wagonPresets == null)
                    {
                        newCustomizeProfile = new CustomizeProfile
                        {
                            wagonPresets = new List<WagonCustomizationData>(),
                            npcPresets = GetNewNpcCustomizeConfig(),
                            fireworkConfigs = GetFireWorksConfig()
                        };
                    }

                    WagonCustomizationData wagonCustomizationData = SaveWagonFromMap(wagonShortPrefabName);
                    newCustomizeProfile.wagonPresets.Add(wagonCustomizationData);
                    SaveProfile(newCustomizeProfile, customizationPresetName);
                }

                static WagonCustomizationData SaveWagonFromMap(string wagonShortPrefabName)
                {
                    WagonCustomizationData wagonCustomizationData = new WagonCustomizationData
                    {
                        shortPrefabName = wagonShortPrefabName,
                        isEnabled = true,
                        wagonOnly = new HashSet<string>(),
                        wagonExceptions = new HashSet<string>(),
                        trainExceptions = new HashSet<string>(),
                        decorEntityConfigs = new HashSet<DecorEntityConfig>(),
                        signConfigs = new HashSet<PaintedSignConfig>()
                    };

                    CheckAndSaveColliders(ref wagonCustomizationData);
                    return wagonCustomizationData;
                }

                static void CheckAndSaveColliders(ref WagonCustomizationData wagonCustomizationData)
                {
                    List<Collider> colliders = Physics.OverlapSphere(Vector3.zero, 50).OrderBy(x => x.transform.position.z);

                    foreach (Collider collider in colliders)
                        TrySaveCollder(collider, ref wagonCustomizationData);
                }

                static void TrySaveCollder(Collider collider, ref WagonCustomizationData wagonCustomizationData)
                {
                    BaseEntity entity = collider.ToBaseEntity();

                    if (entity == null)
                        SaveCollider(collider, ref wagonCustomizationData);
                    else if (IsCustomizingEntity(entity))
                    {
                        NeonSign neonSign = entity as NeonSign;

                        if (neonSign != null)
                            SaveNeonSign(neonSign, ref wagonCustomizationData);
                        else
                            SaveRegularEntity(entity, ref wagonCustomizationData);
                    }
                }

                static bool IsCustomizingEntity(BaseEntity entity)
                {
                    if (entity == null)
                        return false;
                    else if (entity is ResourceEntity || entity is BasePlayer)
                        return false;

                    if (entity is LootContainer)
                    {

                        return false;
                    }

                    return true;
                }

                static void SaveNeonSign(NeonSign neonSign, ref WagonCustomizationData wagonCustomizationData)
                {
                    PaintedSignConfig paintedSignConfig = GetPaintedSignConfig(neonSign);

                    if (paintedSignConfig != null && !wagonCustomizationData.signConfigs.Any(x => x.prefabName == paintedSignConfig.prefabName && x.position == paintedSignConfig.position && x.rotation == paintedSignConfig.rotation))
                        wagonCustomizationData.signConfigs.Add(paintedSignConfig);
                }

                static PaintedSignConfig GetPaintedSignConfig(NeonSign neonSign)
                {
                    return new PaintedSignConfig
                    {
                        prefabName = neonSign.PrefabName,
                        skin = 0,
                        position = $"({neonSign.transform.position.x}, {neonSign.transform.position.y}, {neonSign.transform.position.z})",
                        rotation = neonSign.transform.eulerAngles.ToString(),
                        imageName = ""
                    };
                }

                static void SaveRegularEntity(BaseEntity entity, ref WagonCustomizationData wagonCustomizationData)
                {
                    DecorEntityConfig decorLocationConfig = GetDecorEntityConfig(entity);

                    if (decorLocationConfig != null && !wagonCustomizationData.decorEntityConfigs.Any(x => x.prefabName == decorLocationConfig.prefabName && x.position == decorLocationConfig.position && x.rotation == decorLocationConfig.rotation))
                        wagonCustomizationData.decorEntityConfigs.Add(decorLocationConfig);
                }

                static DecorEntityConfig GetDecorEntityConfig(BaseEntity entity)
                {
                    ulong skin = entity.skinID;
                    if (entity.ShortPrefabName == "rug.deployed")
                    {
                        skin = 2349822120;
                    }
                    else if (entity.ShortPrefabName == "rug.bear.deployed")
                    {
                        skin = 91053011;
                    }
                    else if (entity.ShortPrefabName == "barricade.sandbags")
                    {
                        skin = 809144507;
                    }
                    else if (entity.ShortPrefabName == "barricade.concrete")
                    {
                        skin = 3103508242;
                    }

                    return new DecorEntityConfig
                    {
                        prefabName = entity.PrefabName,
                        skin = skin,
                        position = $"({entity.transform.position.x}, {entity.transform.position.y}, {entity.transform.position.z})",
                        rotation = entity.transform.eulerAngles.ToString()
                    };
                }

                static void SaveCollider(Collider collider, ref WagonCustomizationData wagonCustomizationData)
                {
                    DecorEntityConfig colliderEntityConfig = GetColliderConfigAsBaseEntity(collider);

                    if (colliderEntityConfig != null && !wagonCustomizationData.decorEntityConfigs.Any(x => x.prefabName == colliderEntityConfig.prefabName && x.position == colliderEntityConfig.position && x.rotation == colliderEntityConfig.rotation))
                        wagonCustomizationData.decorEntityConfigs.Add(colliderEntityConfig);
                }

                static DecorEntityConfig GetColliderConfigAsBaseEntity(Collider collider)
                {
                    string prefabName = "";

                    if (!colliderPrefabNames.TryGetValue(collider.name, out prefabName))
                        return null;

                    return new DecorEntityConfig
                    {
                        prefabName = prefabName,
                        skin = 0,
                        position = $"({collider.transform.position.x}, {collider.transform.position.y}, {collider.transform.position.z})",
                        rotation = collider.transform.eulerAngles.ToString()
                    };
                }

                static void SaveProfile(CustomizeProfile customizeData, string name)
                {
                    Interface.Oxide.DataFileSystem.WriteObject($"{ins.Name}/{name}", customizeData);
                }

                static List<CustomizeNpcConfig> GetNewNpcCustomizeConfig()
                {
                    return GetNYNpcConfigs();
                }

                static List<CustomizeNpcConfig> GetHalloweenNpcConfigs()
                {
                    return new List<CustomizeNpcConfig>
                        {
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "pumpkin",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "gloweyes",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "metal.plate.torso",
                                        skinID = 2624420786
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "roadsign.kilt",
                                        skinID = 1539570583
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "hoodie",
                                        skinID = 2963939240
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "pants",
                                        skinID = 2963934001
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "shoes.boots",
                                        skinID = 3047756539
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "roadsign.gloves",
                                        skinID = 3044771291
                                    }
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "metal.facemask",
                                        skinID = 882453233
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "gloweyes",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "hoodie",
                                        skinID = 2256109331
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "pants",
                                        skinID = 2256110716
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "shoes.boots",
                                        skinID = 811633396
                                    },
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "mask.balaclava",
                                        skinID = 2873514778
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "gloweyes",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.trousers",
                                        skinID = 2873788586
                                    },

                                    new CustomWearItem
                                    {
                                        shortName = "burlap.shirt",
                                        skinID = 2873786685
                                    },

                                    new CustomWearItem
                                    {
                                        shortName = "shoes.boots",
                                        skinID = 1644270941
                                    },
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.01.head",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "gloweyes",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.01.torso",
                                        skinID = 0
                                    },

                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.01.legs",
                                        skinID = 0
                                    }
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.02.head",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "gloweyes",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.02.torso",
                                        skinID = 0
                                    },

                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.02.legs",
                                        skinID = 0
                                    }
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.03.head",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "gloweyes",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.03.torso",
                                        skinID = 0
                                    },

                                    new CustomWearItem
                                    {
                                        shortName = "frankensteins.monster.03.legs",
                                        skinID = 0
                                    }
                                }
                            },
                        };
                }

                static List<CustomizeNpcConfig> GetNYNpcConfigs()
                {
                    return new List<CustomizeNpcConfig>
                        {
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.shirt",
                                        skinID = 1587743344
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.trousers",
                                        skinID = 1587746365
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.gloves",
                                        skinID = 784676585
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "roadsign.jacket",
                                        skinID = 1935355816
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "roadsign.kilt",
                                        skinID = 1935355440
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "shoes.boots",
                                        skinID = 2675531117
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "metal.facemask",
                                        skinID = 1170471712
                                    },
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "attire.snowman.helmet",
                                        skinID = 0
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "metal.plate.torso",
                                        skinID = 1934946028
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "pants",
                                        skinID = 2728153861
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "shoes.boots",
                                        skinID = 1158967113
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "hoodie",
                                        skinID = 2728150332
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "roadsign.gloves",
                                        skinID = 2950127861
                                    }
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.shirt",
                                        skinID = 1229561297
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.trousers",
                                        skinID = 1229552157
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.gloves",
                                        skinID = 784676585
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "shoes.boots",
                                        skinID = 2675531117
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "santahat",
                                        skinID = 2675531117
                                    }
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "shoes.boots",
                                        skinID = 2675531117
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "burlap.gloves",
                                        skinID = 784676585
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "roadsign.kilt",
                                        skinID = 2320295405
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "pants",
                                        skinID = 1587846022
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "metal.facemask",
                                        skinID = 2684259722
                                    },
                                    new CustomWearItem
                                    {
                                        shortName = "jacket.snow",
                                        skinID = 1229047763
                                    },
                                }
                            },
                            new CustomizeNpcConfig
                            {
                                enable = true,
                                customWearItems = new List<CustomWearItem>
                                {
                                    new CustomWearItem
                                    {
                                        shortName = "gingerbreadsuit",
                                        skinID = 0
                                    }
                                }
                            },
                        };
                }

                static List<FireworkConfig> GetFireWorksConfig()
                {
                    return GetNYFireWorksConfig();
                }

                static List<FireworkConfig> GetNYFireWorksConfig()
                {
                    return new List<FireworkConfig>
                        {
                            new FireworkConfig
                            {
                                presetName = "2024",
                                isEnabled = true,
                                color = "(0, 1, 0)",
                                paintCoordinates = new HashSet<string>
                                {
                                    "(0.01, 0.85, 0.00)",
                                    "(0.00, 1.00, 0.00)",
                                    "(0.13, 1.00, 0.00)",
                                    "(0.28, 1.00, 0.00)",
                                    "(0.43, 1.00, 0.00)",
                                    "(0.43, 0.85, 0.00)",
                                    "(0.43, 0.71, 0.00)",
                                    "(0.33, 0.59, 0.00)",
                                    "(0.22, 0.50, 0.00)",
                                    "(0.11, 0.41, 0.00)",
                                    "(0.04, 0.29, 0.00)",
                                    "(0.04, 0.15, 0.00)",
                                    "(0.04, 0.02, 0.00)",
                                    "(0.19, 0.02, 0.00)",
                                    "(0.33, 0.03, 0.00)",
                                    "(0.48, 0.04, 0.00)",
                                    "(0.74, 0.85, 0.00)",
                                    "(0.73, 1.00, 0.00)",
                                    "(0.86, 1.00, 0.00)",
                                    "(1.01, 1.00, 0.00)",
                                    "(1.16, 1.00, 0.00)",
                                    "(1.16, 0.85, 0.00)",
                                    "(1.17, 0.71, 0.00)",
                                    "(0.74, 0.71, 0.00)",
                                    "(0.75, 0.56, 0.00)",
                                    "(0.76, 0.42, 0.00)",
                                    "(0.77, 0.29, 0.00)",
                                    "(0.77, 0.15, 0.00)",
                                    "(0.77, 0.02, 0.00)",
                                    "(0.92, 0.02, 0.00)",
                                    "(1.06, 0.03, 0.00)",
                                    "(1.19, 0.03, 0.00)",
                                    "(1.17, 0.56, 0.00)",
                                    "(1.18, 0.41, 0.00)",
                                    "(1.19, 0.26, 0.00)",
                                    "(1.19, 0.16, 0.00)",
                                    "(1.45, 0.85, 0.00)",
                                    "(1.44, 1.00, 0.00)",
                                    "(1.57, 1.00, 0.00)",
                                    "(1.72, 1.00, 0.00)",
                                    "(1.87, 1.00, 0.00)",
                                    "(1.87, 0.85, 0.00)",
                                    "(1.87, 0.71, 0.00)",
                                    "(1.77, 0.59, 0.00)",
                                    "(1.66, 0.50, 0.00)",
                                    "(1.55, 0.41, 0.00)",
                                    "(1.48, 0.29, 0.00)",
                                    "(1.48, 0.15, 0.00)",
                                    "(1.48, 0.02, 0.00)",
                                    "(1.63, 0.02, 0.00)",
                                    "(1.77, 0.03, 0.00)",
                                    "(1.92, 0.04, 0.00)",
                                    "(2.18, 0.85, 0.00)",
                                    "(2.17, 1.00, 0.00)",
                                    "(2.49, 0.56, 0.00)",
                                    "(2.34, 0.56, 0.00)",
                                    "(2.60, 1.00, 0.00)",
                                    "(2.60, 0.85, 0.00)",
                                    "(2.61, 0.71, 0.00)",
                                    "(2.18, 0.71, 0.00)",
                                    "(2.19, 0.56, 0.00)",
                                    "(2.63, 0.03, 0.00)",
                                    "(2.61, 0.56, 0.00)",
                                    "(2.62, 0.41, 0.00)",
                                    "(2.63, 0.26, 0.00)",
                                    "(2.63, 0.16, 0.00)"
                                }
                            }
                        };
                }
            }

            static class SignPainter
            {
                static string imagePath = $"{ins.Name}/Images/";

                internal static void UpdateNeonSign(NeonSign neonSign, string imageName)
                {
                    if (imageName != "")
                        ServerMgr.Instance.StartCoroutine(LoadImage(neonSign, imageName));

                    neonSign.UpdateFromInput(100, 0);
                }

                static IEnumerator LoadImage(NeonSign neonSign, string imageName)
                {
                    string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + imagePath + imageName + ".png";
                    using (WWW www = new WWW(url))
                    {
                        yield return www;

                        if (www.error == null)
                        {
                            neonSign.EnsureInitialized();
                            Texture2D tex = www.texture;
                            byte[] bt = tex.EncodeToPNG();
                            Array.Resize(ref neonSign.textureIDs, 1);
                            uint textureIndex = 0;
                            uint textureId = FileStorage.server.Store(bt, FileStorage.Type.png, neonSign.net.ID, textureIndex);

                            neonSign.textureIDs[textureIndex] = textureId;
                            neonSign.SendNetworkUpdate();
                        }
                        else
                        {
                            ins.PrintError($"{imageName} file was not found in the data/ArmoredTrain/Images folder");
                        }
                    }
                }
            }

            internal static class PatternFireworkSignSaver
            {
                static HashSet<string> symbol2 = new HashSet<string>()
                    {
                        "(0.01, 0.85, 0.00)",
                        "(0.00, 1.00, 0.00)",
                        "(0.13, 1.00, 0.00)",
                        "(0.28, 1.00, 0.00)",
                        "(0.43, 1.00, 0.00)",
                        "(0.43, 0.85, 0.00)",
                        "(0.43, 0.71, 0.00)",
                        "(0.33, 0.59, 0.00)",
                        "(0.22, 0.50, 0.00)",
                        "(0.11, 0.41, 0.00)",
                        "(0.04, 0.29, 0.00)",
                        "(0.04, 0.15, 0.00)",
                        "(0.04, 0.02, 0.00)",
                        "(0.19, 0.02, 0.00)",
                        "(0.33, 0.03, 0.00)",
                        "(0.48, 0.04, 0.00)",
                    };

                static HashSet<string> symbol0 = new HashSet<string>()
                    {
                        "(0.01, 0.85, 0.00)",
                        "(0.00, 1.00, 0.00)",
                        "(0.13, 1.00, 0.00)",
                        "(0.28, 1.00, 0.00)",
                        "(0.43, 1.00, 0.00)",
                        "(0.43, 0.85, 0.00)",
                        "(0.44, 0.71, 0.00)",
                        "(0.01, 0.71, 0.00)",
                        "(0.02, 0.56, 0.00)",
                        "(0.03, 0.42, 0.00)",
                        "(0.04, 0.29, 0.00)",
                        "(0.04, 0.15, 0.00)",
                        "(0.04, 0.02, 0.00)",
                        "(0.19, 0.02, 0.00)",
                        "(0.33, 0.03, 0.00)",
                        "(0.46, 0.03, 0.00)",
                        "(0.44, 0.56, 0.00)",
                        "(0.45, 0.41, 0.00)",
                        "(0.46, 0.26, 0.00)",
                        "(0.46, 0.16, 0.00)",
                    };

                static HashSet<string> symbol4 = new HashSet<string>()
                    {
                        "(0.01, 0.85, 0.00)",
                        "(0.00, 1.00, 0.00)",
                        "(0.32, 0.56, 0.00)",
                        "(0.17, 0.56, 0.00)",
                        "(0.43, 1.00, 0.00)",
                        "(0.43, 0.85, 0.00)",
                        "(0.44, 0.71, 0.00)",
                        "(0.01, 0.71, 0.00)",
                        "(0.02, 0.56, 0.00)",
                        "(0.46, 0.03, 0.00)",
                        "(0.44, 0.56, 0.00)",
                        "(0.45, 0.41, 0.00)",
                        "(0.46, 0.26, 0.00)",
                        "(0.46, 0.16, 0.00)",
                    };

                internal static void UpdatePatternFirework(PatternFirework patternFirework)
                {
                    patternFirework.Design?.Dispose();
                    patternFirework.MaxStars = 1000;
                    patternFirework.Design = new ProtoBuf.PatternFirework.Design();
                    patternFirework.Design.stars = new List<Star>();

                    Print2024(patternFirework);
                    patternFirework.SendNetworkUpdateImmediate();
                }

                static void Print2024(PatternFirework patternFirework)
                {
                    float x0 = -2;

                    PrintSymbol(symbol2, patternFirework, ref x0);
                    PrintSymbol(symbol0, patternFirework, ref x0);
                    PrintSymbol(symbol2, patternFirework, ref x0);
                    PrintSymbol(symbol4, patternFirework, ref x0);
                }

                static void PrintSymbol(HashSet<string> symbol, PatternFirework patternFirework, ref float x0)
                {
                    float newx0 = float.MinValue;

                    foreach (string coord in symbol)
                    {
                        Vector3 position = coord.ToVector3();

                        if (position.x + x0 > newx0)
                            newx0 = position.x + x0;

                        patternFirework.Design.stars.Add
                        (
                            new ProtoBuf.PatternFirework.Star
                            {
                                color = new Color(1, 0, 0),
                                position = new Vector2(position.x + x0, position.y)
                            }
                        );
                    }

                    x0 = newx0 + 0.25f;
                }

                internal static void ShowStarsCoordinatesOfRegularPaint(PatternFirework patternFirework)
                {
                    foreach (Star start in patternFirework.Design.stars)
                    {
                        Vector3 starPosition = new Vector3(start.position.x, start.position.y, 0) + new Vector3(1, 0, 0);
                        ins.Puts(starPosition.ToString());
                    }
                }

                internal static void ShowStarsCoordinatesOfCustomPaint(PatternFirework patternFirework)
                {
                    foreach (Star start in patternFirework.Design.stars)
                    {
                        Vector3 starPosition = new Vector3(start.position.x, start.position.y, 0) + new Vector3(2, 0, 0);
                        ins.Puts(starPosition.ToString());
                    }
                }
            }
        }

        class EventHeli : FacepunchBehaviour
        {
            static EventHeli eventHeli;

            internal HeliConfig heliConfig;
            PatrolHelicopter patrolHelicopter;
            Vector3 patrolPosition;
            int ounsideTime;
            bool isFollowing;
            internal ulong lastAttackedPlayer;

            internal static EventHeli SpawnHeli(HeliConfig heliConfig)
            {
                Vector3 position = ins.eventController.GetEventPosition() + Vector3.up * heliConfig.height;

                PatrolHelicopter patrolHelicopter = BuildManager.SpawnRegularEntity("assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab", position, Quaternion.identity) as PatrolHelicopter;
                patrolHelicopter.transform.position = position;
                eventHeli = patrolHelicopter.gameObject.AddComponent<EventHeli>();
                eventHeli.Init(heliConfig, patrolHelicopter);
                return eventHeli;
            }

            internal static EventHeli GetEventHeliByNetId(ulong netID)
            {
                if (eventHeli != null && eventHeli.patrolHelicopter.IsExists() && eventHeli.patrolHelicopter.net != null && eventHeli.patrolHelicopter.net.ID.Value == netID)
                    return eventHeli;
                else
                    return null;
            }

            internal static EventHeli GetClosestHeli(Vector3 position)
            {
                return eventHeli;
            }

            internal static bool IsEventHeliAlive()
            {
                return eventHeli != null && eventHeli.patrolHelicopter.IsExists();
            }

            internal static HashSet<ulong> GetAliveHeliesNetIDS()
            {
                HashSet<ulong> helies = new HashSet<ulong>();

                if (eventHeli != null && eventHeli.patrolHelicopter != null && eventHeli.patrolHelicopter.net != null)
                    helies.Add(eventHeli.patrolHelicopter.net.ID.Value);

                return helies;
            }

            void Init(HeliConfig heliConfig, PatrolHelicopter patrolHelicopter)
            {
                this.heliConfig = heliConfig;
                this.patrolHelicopter = patrolHelicopter;
                UpdateHelicopter();
                StartFollowing();
                patrolHelicopter.InvokeRepeating(UpdatePosition, 1, 1);
            }

            void UpdateHelicopter()
            {
                patrolHelicopter.startHealth = heliConfig.hp;
                patrolHelicopter.InitializeHealth(heliConfig.hp, heliConfig.hp);
                patrolHelicopter.maxCratesToSpawn = heliConfig.cratesAmount;
                patrolHelicopter.bulletDamage = heliConfig.bulletDamage;
                patrolHelicopter.bulletSpeed = heliConfig.bulletSpeed;

                var weakspots = patrolHelicopter.weakspots;
                if (weakspots != null && weakspots.Length > 1)
                {
                    weakspots[0].maxHealth = heliConfig.mainRotorHealth;
                    weakspots[0].health = heliConfig.mainRotorHealth;
                    weakspots[1].maxHealth = heliConfig.rearRotorHealth;
                    weakspots[1].health = heliConfig.rearRotorHealth;
                }
            }

            void UpdatePosition()
            {
                patrolHelicopter.myAI.spawnTime = UnityEngine.Time.realtimeSinceStartup;

                if (patrolHelicopter.myAI._currentState == PatrolHelicopterAI.aiState.DEATH || patrolHelicopter.myAI._currentState == PatrolHelicopterAI.aiState.STRAFE)
                    return;

                if (ins.eventController.IsStopped())
                {
                    if (isFollowing)
                        StartPatrol();
                }
                else if (!isFollowing)
                    StartFollowing();

                if (isFollowing)
                    DoFollowing();
                else
                    DoPatrol();
            }

            void DoFollowing()
            {
                Vector3 position = ins.eventController.GetEventPosition() + Vector3.up * heliConfig.height;
                patrolHelicopter.myAI.State_Move_Enter(position);
            }

            void DoPatrol()
            {
                if (patrolHelicopter.myAI.leftGun.HasTarget() || patrolHelicopter.myAI.rightGun.HasTarget())
                {
                    if (Vector3.Distance(patrolPosition, patrolHelicopter.transform.position) > heliConfig.distance)
                    {
                        ounsideTime++;

                        if (ounsideTime > heliConfig.outsideTime)
                        {
                            patrolHelicopter.myAI.State_Move_Enter(patrolPosition);
                        }
                    }
                    else
                    {
                        ounsideTime = 0;
                        patrolHelicopter.myAI.ClearAimTarget();
                        patrolHelicopter.myAI.leftGun.ClearTarget();
                        patrolHelicopter.myAI.rightGun.ClearTarget();
                    }
                }
                else if (Vector3.Distance(patrolPosition, patrolHelicopter.transform.position) > heliConfig.distance)
                {
                    patrolHelicopter.myAI.State_Move_Enter(patrolPosition);
                    ounsideTime = 0;
                }
                else
                    ounsideTime = 0;
            }

            void StartFollowing()
            {
                isFollowing = true;
            }

            void StartPatrol()
            {
                isFollowing = false;
                ounsideTime = 0;
                patrolPosition = ins.eventController.GetEventPosition() + Vector3.up * heliConfig.height;
            }

            internal bool IsHeliCanTarget()
            {
                return ins.eventController.IsAgressive();
            }

            internal void OnHeliAttacked(ulong userId)
            {
                if (patrolHelicopter.myAI.isDead)
                    return;
                else
                    lastAttackedPlayer = userId;
            }

            internal void Kill()
            {
                if (patrolHelicopter.IsExists())
                    patrolHelicopter.Kill();
            }

            internal static void ClearData()
            {
                if (eventHeli != null)
                    eventHeli.Kill();

                eventHeli = null;
            }
        }

        class TurretOptimizer : FacepunchBehaviour
        {
            AutoTurret autoTurret;
            float targetRadius;

            internal static void Attach(AutoTurret autoTurret, float targetRadius)
            {
                TurretOptimizer turretTargetOptimizer = autoTurret.gameObject.AddComponent<TurretOptimizer>();
                turretTargetOptimizer.Init(autoTurret, targetRadius);
            }

            void Init(AutoTurret autoTurret, float targetRadius)
            {
                this.autoTurret = autoTurret;
                this.targetRadius = targetRadius;
                AutoTurret.interferenceUpdateList.Remove(autoTurret);

                SphereCollider sphereCollider = autoTurret.targetTrigger.GetComponent<SphereCollider>();
                sphereCollider.enabled = false;

                autoTurret.Invoke(() =>
                {
                    autoTurret.CancelInvoke(autoTurret.ServerTick);
                    autoTurret.SetTarget(null);
                }, 1.1f);

                autoTurret.InvokeRepeating(new Action(OptimizedServerTick), UnityEngine.Random.Range(1.2f, 2.2f), 0.015f);
                autoTurret.InvokeRepeating(ScanTargets, 3f, 1f);
            }

            private void ScanTargets()
            {
                if (autoTurret.targetTrigger.entityContents == null)
                    autoTurret.targetTrigger.entityContents = new HashSet<BaseEntity>();
                else
                    autoTurret.targetTrigger.entityContents.Clear();

                if (!ins.eventController.IsAgressive())
                    return;

                int count = BaseEntity.Query.Server.GetPlayersInSphereFast(transform.position, targetRadius, AIBrainSenses.playerQueryResults, IsPlayerCanBeTargeted);

                if (count == 0)
                    return;

                autoTurret.authDirty = true;

                for (int i = 0; i < count; i++)
                {
                    BasePlayer player = AIBrainSenses.playerQueryResults[i];

                    if (Interface.CallHook("OnEntityEnter", autoTurret.targetTrigger, player) != null)
                        continue;

                    if (player.IsSleeping() || (player.InSafeZone() && !player.IsHostile()))
                        continue;

                    autoTurret.targetTrigger.entityContents.Add(player);
                }
            }

            public void OptimizedServerTick()
            {
                if (autoTurret.isClient || autoTurret.IsDestroyed)
                    return;

                float timeSinceLastServerTick = (float)autoTurret.timeSinceLastServerTick;
                autoTurret.timeSinceLastServerTick = 0;

                if (!autoTurret.IsOnline())
                {
                    autoTurret.OfflineTick();
                }
                else if (!autoTurret.IsBeingControlled)
                {
                    if (!autoTurret.HasTarget())
                    {
                        autoTurret.IdleTick(timeSinceLastServerTick);
                    }
                    else
                    {
                        OptimizedTargetTick();
                    }
                }

                autoTurret.UpdateFacingToTarget(timeSinceLastServerTick);

                if (autoTurret.totalAmmoDirty && Time.time > autoTurret.nextAmmoCheckTime)
                {
                    autoTurret.UpdateTotalAmmo();
                    autoTurret.totalAmmoDirty = false;
                    autoTurret.nextAmmoCheckTime = Time.time + 0.5f;
                }
            }

            public void OptimizedTargetTick()
            {
                if (UnityEngine.Time.realtimeSinceStartup >= autoTurret.nextVisCheck)
                {
                    autoTurret.nextVisCheck = UnityEngine.Time.realtimeSinceStartup + UnityEngine.Random.Range(0.2f, 0.3f);
                    autoTurret.targetVisible = autoTurret.ObjectVisible(autoTurret.target);
                    if (autoTurret.targetVisible)
                    {
                        autoTurret.lastTargetSeenTime = UnityEngine.Time.realtimeSinceStartup;
                    }
                }

                autoTurret.EnsureReloaded();
                BaseProjectile attachedWeapon = autoTurret.GetAttachedWeapon();

                if (UnityEngine.Time.time >= autoTurret.nextShotTime && autoTurret.targetVisible && Mathf.Abs(autoTurret.AngleToTarget(autoTurret.target, autoTurret.currentAmmoGravity != 0f)) < autoTurret.GetMaxAngleForEngagement())
                {
                    if ((bool)attachedWeapon)
                    {
                        if (attachedWeapon.primaryMagazine.contents > 0)
                        {
                            autoTurret.FireGun(autoTurret.AimOffset(autoTurret.target), autoTurret.aimCone, null);
                            float delay = (attachedWeapon.isSemiAuto ? (attachedWeapon.repeatDelay * 1.5f) : attachedWeapon.repeatDelay);
                            delay = attachedWeapon.ScaleRepeatDelay(delay);
                            autoTurret.nextShotTime = UnityEngine.Time.time + delay;
                        }
                        else
                        {
                            autoTurret.nextShotTime = UnityEngine.Time.time + 5f;
                        }
                    }
                    else if (autoTurret.HasFallbackWeapon())
                    {
                        autoTurret.FireGun(autoTurret.AimOffset(autoTurret.target), autoTurret.aimCone, null, autoTurret.target);
                        autoTurret.nextShotTime = UnityEngine.Time.time + 0.115f;
                    }
                    else if (autoTurret.HasGenericFireable())
                    {
                        autoTurret.AttachedWeapon.ServerUse();
                        autoTurret.nextShotTime = UnityEngine.Time.time + 0.115f;
                    }
                    else
                    {
                        autoTurret.nextShotTime = UnityEngine.Time.time + 1f;
                    }
                }

                BasePlayer targetPlayer = autoTurret.target as BasePlayer;

                if (autoTurret.target != null && (!targetPlayer.IsRealPlayer() || autoTurret.target.IsDead() || UnityEngine.Time.realtimeSinceStartup - autoTurret.lastTargetSeenTime > 3f || Vector3.Distance(autoTurret.transform.position, autoTurret.target.transform.position) > autoTurret.sightRange || (autoTurret.PeacekeeperMode() && !autoTurret.IsEntityHostile(autoTurret.target))))
                    autoTurret.SetTarget(null);
            }

            bool IsPlayerCanBeTargeted(BasePlayer player)
            {
                if (!player.IsRealPlayer())
                    return false;

                if (player.IsDead() || player.IsSleeping() || player.IsWounded())
                    return false;

                if (player.InSafeZone() || player._limitedNetworking)
                    return false;

                return true;
            }
        }

        class CustomBradley : BradleyAPC
        {
            internal BradleyConfig bradleyConfig;

            internal static CustomBradley SpawnCustomBradley(Vector3 localPosition, Vector3 localRotation, BaseEntity parentEntity, BradleyConfig bradleyConfig)
            {
                BradleyAPC bradley = GameManager.server.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab", parentEntity.transform.position, Quaternion.identity) as BradleyAPC;
                bradley.skinID = 755446;
                bradley.enableSaving = false;
                bradley.ScientistSpawns.Clear();

                CustomBradley customBradley = bradley.gameObject.AddComponent<CustomBradley>();
                BuildManager.CopySerializableFields(bradley, customBradley);
                bradley.StopAllCoroutines();
                UnityEngine.GameObject.DestroyImmediate(bradley, true);

                customBradley.bradleyConfig = bradleyConfig;
                BuildManager.SetParent(parentEntity, customBradley, localPosition, localRotation);
                customBradley.Spawn();

                TriggerHurtNotChild[] triggerHurts = customBradley.GetComponentsInChildren<TriggerHurtNotChild>();
                foreach (TriggerHurtNotChild triggerHurt in triggerHurts)
                    triggerHurt.enabled = false;

                return customBradley;
            }

            new void FixedUpdate()
            {
                SetFlag(Flags.Reserved5, TOD_Sky.Instance.IsNight);

                if (ins.eventController.IsAgressive())
                {
                    UpdateTarget();
                    DoWeapons();
                }

                DoHealing();
                DoWeaponAiming();
                SendNetworkUpdate();

                if (mainGunTarget == null)
                    desiredAimVector = transform.forward;
            }

            void UpdateTarget()
            {
                if (targetList.Count > 0)
                {
                    TargetInfo targetInfo = targetList[0];

                    if (targetInfo == null)
                    {
                        mainGunTarget = null;
                        return;
                    }

                    BasePlayer player = targetInfo.entity as BasePlayer;

                    if (player.IsRealPlayer() && targetInfo.IsVisible())
                        mainGunTarget = targetList[0].entity as BaseCombatEntity;
                    else
                        mainGunTarget = null;
                }
                else
                {
                    mainGunTarget = null;
                }
            }
        }

        static class SpawnPositionFinder
        {
            internal static PositionData GetSpawnPositionData(bool isUnderGround)
            {
                PositionData positionData = null;

                if (ins._config.mainConfig.customSpawnPointConfig.isEnabled)
                {
                    positionData = CustomSpawnPointFinder.GetSpawnPosition();

                    if (positionData == null)
                        NotifyManager.PrintError(null, "CustomSpawnPoint_Exeption");
                }

                if (positionData == null && !isUnderGround)
                    positionData = AboveGroundPositionFinder.GetSpawnPosition();

                if (positionData == null)
                    positionData = UnderGroundPositionFinder.GetSpawnPosition();

                if (positionData == null)
                    NotifyManager.PrintError(null, "Rail_Exeption");

                return positionData;
            }

            internal static bool IsRailsInPosition(Vector3 position)
            {
                TrainTrackSpline trainTrackSpline;
                float distance;

                return TrainTrackSpline.TryFindTrackNear(position, 1, out trainTrackSpline, out distance);
            }

            static bool IsSpawnPositionExist(Vector3 position)
            {
                foreach (Collider collider in UnityEngine.Physics.OverlapSphere(position, 10))
                {
                    BaseEntity entity = collider.ToBaseEntity();

                    if (!entity.IsExists())
                        continue;

                    if (entity is TrainCar)
                        return false;
                }

                return true;
            }

            static void ClearSpawnPoint(Vector3 position)
            {
                foreach (Collider collider in UnityEngine.Physics.OverlapSphere(position, 10))
                {
                    TrainCar trainCar = collider.ToBaseEntity() as TrainCar;

                    if (!trainCar.IsExists())
                        continue;

                    EntityFuelSystem entityFuelSystem = trainCar.GetFuelSystem() as EntityFuelSystem;

                    if (entityFuelSystem != null && entityFuelSystem.cachedHasFuel)
                        continue;

                    trainCar.Kill();
                }
            }

            static class AboveGroundPositionFinder
            {
                internal static PositionData GetSpawnPosition()
                {
                    if (TerrainMeta.Path.Rails == null || TerrainMeta.Path.Rails.Count == 0)
                        return null;

                    PositionData positionData = null;
                    PathList pathList = TerrainMeta.Path.Rails.Max(x => x.Path.Length);

                    if (pathList == null || pathList.Path == null || pathList.Path.Points.Length == 0)
                        return null;

                    for (int i = 0; i < 100 && positionData == null; i++)
                        positionData = TryGetSpawnPositionDataOnPathList(pathList);

                    return positionData;
                }

                static PositionData TryGetSpawnPositionDataOnPathList(PathList pathList)
                {
                    Vector3 position = pathList.Path.Points.GetRandom();
                    Quaternion rotation;

                    TrainTrackSpline trainTrackSpline;
                    float distance;

                    if (!TrainTrackSpline.TryFindTrackNear(position, 1, out trainTrackSpline, out distance))
                        return null;

                    float length = trainTrackSpline.GetLength();

                    if (length < 65)
                        return null;

                    float randomLength = UnityEngine.Random.Range(60f, length - 60f);
                    Vector3 rotationVector;
                    position = trainTrackSpline.GetPointAndTangentCubicHermiteWorld(randomLength, out rotationVector) + (Vector3.up * 0.5f);
                    rotation = Quaternion.LookRotation(rotationVector);

                    if (!IsSpawnPositionExist(position))
                        return null;

                    return new PositionData(position, rotation);
                }
            }

            static class UnderGroundPositionFinder
            {
                internal static PositionData GetSpawnPosition()
                {
                    PositionData positionData = null;

                    for (int i = 0; i < 100 && positionData == null; i++)
                    {
                        DungeonGridCell dungeonGridCell = TerrainMeta.Path.DungeonGridCells.GetRandom();
                        positionData = TryGetPositionDataFromDoungeon(dungeonGridCell);
                    }

                    return positionData;
                }

                static PositionData TryGetPositionDataFromDoungeon(DungeonGridCell dungeonGridCell)
                {
                    TrainTrackSpline trainTrackSpline;
                    float distance;

                    if (!TrainTrackSpline.TryFindTrackNear(dungeonGridCell.transform.position, 3, out trainTrackSpline, out distance))
                        return null;

                    float length = trainTrackSpline.GetLength();

                    if (length < 65)
                        return null;

                    float randomLength = UnityEngine.Random.Range(60f, length - 60f);
                    Vector3 rotationVector;
                    Vector3 position = trainTrackSpline.GetPointAndTangentCubicHermiteWorld(randomLength, out rotationVector) + (Vector3.up * 0.5f);
                    Quaternion rotation = Quaternion.LookRotation(rotationVector);

                    if (!IsSpawnPositionExist(position))
                        return null;

                    return new PositionData(position, rotation);
                }
            }

            static class CustomSpawnPointFinder
            {
                internal static PositionData GetSpawnPosition()
                {
                    List<LocationConfig> suitablePoints = Pool.GetList<LocationConfig>();

                    foreach (LocationConfig locationConfig in ins._config.mainConfig.customSpawnPointConfig.points)
                    {
                        Vector3 position = locationConfig.position.ToVector3();

                        if (IsRailsInPosition(position) && IsSpawnPositionExist(position))
                            suitablePoints.Add(locationConfig);
                    }

                    if (suitablePoints.Count == 0)
                        foreach (LocationConfig locationConfig in ins._config.mainConfig.customSpawnPointConfig.points)
                            if (IsRailsInPosition(locationConfig.position.ToVector3()))
                                suitablePoints.Add(locationConfig);

                    PositionData positionData = null;
                    LocationConfig randomLocationConfig = suitablePoints.GetRandom();

                    if (randomLocationConfig != null)
                    {
                        Vector3 position = randomLocationConfig.position.ToVector3();
                        ClearSpawnPoint(position);
                        positionData = new PositionData(position, Quaternion.Euler(randomLocationConfig.rotation.ToVector3()));
                    }

                    Pool.FreeList(ref suitablePoints);
                    return positionData;
                }
            }
        }

        class ZoneController : FacepunchBehaviour
        {
            static ZoneController zoneController;
            SphereCollider sphereCollider;
            Coroutine zoneUpdateCorountine;
            HashSet<BaseEntity> spheres = new HashSet<BaseEntity>();
            HashSet<BasePlayer> playersInZone = new HashSet<BasePlayer>();

            internal static void CreateZone(BasePlayer externalOwner = null)
            {
                TryDeleteZone();
                Vector3 position = ins.eventController.GetEventPosition();

                if (position == Vector3.zero)
                    return;

                GameObject gameObject = new GameObject();
                gameObject.transform.position = position;
                gameObject.layer = (int)Rust.Layer.Reserved1;

                zoneController = gameObject.AddComponent<ZoneController>();
                zoneController.Init(externalOwner);
            }

            internal static bool IsZoneCreated()
            {
                return zoneController != null;
            }

            internal static bool IsPlayerInZone(ulong userID)
            {
                return zoneController != null && zoneController.playersInZone.Any(x => x != null && x.userID == userID);
            }

            internal static bool IsAnyPlayerInEventZone()
            {
                return zoneController != null && zoneController.playersInZone.Any(x => x.IsExists() && !x.IsSleeping());
            }

            internal static void OnPlayerLeaveZone(BasePlayer player)
            {
                if (zoneController == null)
                    return;

                Interface.CallHook($"OnPlayerExit{ins.Name}", player);
                zoneController.playersInZone.Remove(player);
                GuiManager.DestroyGui(player);

                if (ins._config.zoneConfig.isPVPZone)
                {
                    if (ins.plugins.Exists("DynamicPVP") && (bool)ins.DynamicPVP.Call("IsPlayerInPVPDelay", (ulong)player.userID))
                        return;

                    NotifyManager.SendMessageToPlayer(player, "ExitPVP", ins._config.prefix);
                }
            }

            internal static bool IsEventPosition(Vector3 position)
            {
                Vector3 eventPosition = ins.eventController.GetEventPosition();
                return Vector3.Distance(position, eventPosition) < ins.eventController.eventConfig.zoneRadius;
            }

            internal static HashSet<BasePlayer> GetAllPlayersInZone()
            {
                if (zoneController == null)
                    return new HashSet<BasePlayer>();
                else
                    return zoneController.playersInZone;
            }

            void Init(BasePlayer externalOwner)
            {
                CreateTriggerSphere();
                CreateSpheres();

                if (PveModeManager.IsPveModeReady())
                    PveModeManager.CreatePveModeZone(this.transform.position, externalOwner);

                zoneUpdateCorountine = ServerMgr.Instance.StartCoroutine(ZoneUpdateCorountine());
            }

            void CreateTriggerSphere()
            {
                sphereCollider = gameObject.AddComponent<SphereCollider>();
                sphereCollider.isTrigger = true;
                sphereCollider.radius = ins.eventController.eventConfig.zoneRadius;
            }

            void CreateSpheres()
            {
                if (ins._config.zoneConfig.isDome)
                    for (int i = 0; i < ins._config.zoneConfig.darkening; i++)
                        CreateSphere("assets/prefabs/visualization/sphere.prefab");

                if (ins._config.zoneConfig.isColoredBorder)
                {
                    string spherePrefab = ins._config.zoneConfig.borderColor == 0 ? "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab" : ins._config.zoneConfig.borderColor == 1 ? "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" :
                         ins._config.zoneConfig.borderColor == 2 ? "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab" : "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab";

                    for (int i = 0; i < ins._config.zoneConfig.brightness; i++)
                        CreateSphere(spherePrefab);
                }
            }

            void CreateSphere(string prefabName)
            {
                BaseEntity sphere = GameManager.server.CreateEntity(prefabName, gameObject.transform.position);
                SphereEntity entity = sphere.GetComponent<SphereEntity>();
                entity.currentRadius = ins.eventController.eventConfig.zoneRadius * 2;
                entity.lerpSpeed = 0f;
                sphere.enableSaving = false;
                sphere.Spawn();
                spheres.Add(sphere);
            }

            void OnTriggerEnter(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();
                if (player.IsRealPlayer())
                {
                    Interface.CallHook($"OnPlayerEnter{ins.Name}", player);
                    playersInZone.Add(player);

                    if (ins._config.zoneConfig.isPVPZone)
                        NotifyManager.SendMessageToPlayer(player, "EnterPVP", ins._config.prefix);

                    GuiManager.CreateGui(player, NotifyManager.GetTimeMessage(player.UserIDString, ins.eventController.GetEventTime()), LootManager.GetCountOfUnlootedCrates().ToString(), NpcSpawnManager.GetEventNpcCount().ToString());
                }
            }

            void OnTriggerExit(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();

                if (player.IsRealPlayer())
                    OnPlayerLeaveZone(player);
            }

            IEnumerator ZoneUpdateCorountine()
            {
                while (zoneController != null)
                {
                    int countOfCrates = LootManager.GetCountOfUnlootedCrates();
                    int countOfGuardNpc = NpcSpawnManager.GetEventNpcCount();

                    foreach (BasePlayer player in playersInZone)
                        if (player != null)
                            GuiManager.CreateGui(player, NotifyManager.GetTimeMessage(player.UserIDString, ins.eventController.GetEventTime()), countOfCrates.ToString(), countOfGuardNpc.ToString());

                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            internal static void TryDeleteZone()
            {
                if (zoneController != null)
                    zoneController.DeleteZone();
            }

            void DeleteZone()
            {
                foreach (BaseEntity sphere in spheres)
                    if (sphere != null && !sphere.IsDestroyed)
                        sphere.Kill();

                if (zoneUpdateCorountine != null)
                    ServerMgr.Instance.StopCoroutine(zoneUpdateCorountine);

                GuiManager.DestroyAllGui();
                PveModeManager.DeletePveModeZone();
                UnityEngine.GameObject.Destroy(gameObject);
            }
        }

        static class PveModeManager
        {
            static HashSet<ulong> pveModeOwners = new HashSet<ulong>();
            static BasePlayer owner;
            static float lastZoneDeleteTime;

            internal static bool IsPveModeReady()
            {
                return ins._config.supportedPluginsConfig.pveMode.enable && ins.plugins.Exists("PveMode");
            }

            internal static BasePlayer UpdateAndGetEventOwner()
            {
                if (ins.eventController.IsStopped())
                    return owner;

                float timeScienceLastZoneDelete = Time.realtimeSinceStartup - lastZoneDeleteTime;

                if (timeScienceLastZoneDelete > ins._config.supportedPluginsConfig.pveMode.timeExitOwner)
                    owner = null;

                return owner;
            }

            internal static void CreatePveModeZone(Vector3 position, BasePlayer externalOwner)
            {
                Dictionary<string, object> config = GetPveModeConfig();

                HashSet<ulong> npcs = NpcSpawnManager.GetEventNpcNetIds();
                HashSet<ulong> bradleys = ins.eventController.GetAliveBradleysNetIDS();
                HashSet<ulong> helicopters = EventHeli.GetAliveHeliesNetIDS();
                HashSet<ulong> crates = LootManager.GetEventCratesNetIDs();
                HashSet<ulong> turrets = ins.eventController.GetAliveTurretsNetIDS();

                BasePlayer playerOwner = GetEventOwner();

                if (playerOwner == null)
                    playerOwner = externalOwner;

                ins.PveMode.Call("EventAddPveMode", ins.Name, config, position, ins.eventController.eventConfig.zoneRadius, crates, npcs, bradleys, helicopters, turrets, pveModeOwners, playerOwner);
            }

            static BasePlayer GetEventOwner()
            {
                BasePlayer playerOwner = null;

                float timeScienceLastZoneDelete = Time.realtimeSinceStartup - lastZoneDeleteTime;

                if (owner != null && (ins.eventController.IsStopped() || timeScienceLastZoneDelete < ins._config.supportedPluginsConfig.pveMode.timeExitOwner))
                    playerOwner = owner;

                return playerOwner;
            }

            static Dictionary<string, object> GetPveModeConfig()
            {
                return new Dictionary<string, object>
                {
                    ["Damage"] = ins._config.supportedPluginsConfig.pveMode.damage,
                    ["ScaleDamage"] = ins._config.supportedPluginsConfig.pveMode.scaleDamage,
                    ["LootCrate"] = ins._config.supportedPluginsConfig.pveMode.lootCrate,
                    ["HackCrate"] = ins._config.supportedPluginsConfig.pveMode.hackCrate,
                    ["LootNpc"] = ins._config.supportedPluginsConfig.pveMode.lootNpc,
                    ["DamageNpc"] = ins._config.supportedPluginsConfig.pveMode.damageNpc,
                    ["DamageTank"] = ins._config.supportedPluginsConfig.pveMode.damageTank,
                    ["DamageHelicopter"] = ins._config.supportedPluginsConfig.pveMode.damageHeli,
                    ["DamageTurret"] = ins._config.supportedPluginsConfig.pveMode.damageTurret,
                    ["TargetNpc"] = ins._config.supportedPluginsConfig.pveMode.targetNpc,
                    ["TargetTank"] = ins._config.supportedPluginsConfig.pveMode.targetTank,
                    ["TargetHelicopter"] = ins._config.supportedPluginsConfig.pveMode.targetHeli,
                    ["TargetTurret"] = ins._config.supportedPluginsConfig.pveMode.targetTurret,
                    ["CanEnter"] = ins._config.supportedPluginsConfig.pveMode.canEnter,
                    ["CanEnterCooldownPlayer"] = ins._config.supportedPluginsConfig.pveMode.canEnterCooldownPlayer,
                    ["TimeExitOwner"] = ins._config.supportedPluginsConfig.pveMode.timeExitOwner,
                    ["AlertTime"] = ins._config.supportedPluginsConfig.pveMode.alertTime,
                    ["RestoreUponDeath"] = ins._config.supportedPluginsConfig.pveMode.restoreUponDeath,
                    ["CooldownOwner"] = ins._config.supportedPluginsConfig.pveMode.cooldown,
                    ["Darkening"] = 0
                };
            }

            internal static void DeletePveModeZone()
            {
                if (!IsPveModeReady())
                    return;

                lastZoneDeleteTime = Time.realtimeSinceStartup;
                pveModeOwners = (HashSet<ulong>)ins.PveMode.Call("GetEventOwners", ins.Name);

                if (pveModeOwners == null)
                    pveModeOwners = new HashSet<ulong>();

                ulong userId = (ulong)ins.PveMode.Call("GetEventOwner", ins.Name);
                OnNewOwnerSet(userId);

                ins.PveMode.Call("EventRemovePveMode", ins.Name, false);
            }

            static void OnNewOwnerSet(ulong userId)
            {
                if (userId == 0)
                    return;

                BasePlayer player = BasePlayer.FindByID(userId);
                OnNewOwnerSet(player);
            }

            internal static void OnNewOwnerSet(BasePlayer player)
            {
                owner = player;
            }

            internal static void OnOwnerDeleted()
            {
                owner = null;
            }

            internal static void OnEventEnd()
            {
                if (IsPveModeReady())
                    ins.PveMode.Call("EventAddCooldown", ins.Name, pveModeOwners, ins._config.supportedPluginsConfig.pveMode.cooldown);

                lastZoneDeleteTime = 0;
                pveModeOwners.Clear();
                owner = null;
            }

            internal static bool IsPveModeBlockAction(BasePlayer player)
            {
                if (IsPveModeReady())
                    return ins.PveMode.Call("CanActionEvent", ins.Name, player) != null;

                return false;
            }

            internal static bool IsPveModeBlockInterract(BasePlayer player)
            {
                if (!IsPveModeReady())
                    return false;

                BasePlayer eventOwner = GetEventOwner();

                if ((ins._config.supportedPluginsConfig.pveMode.noInterractIfCooldownAndNoOwners && eventOwner == null) || ins._config.supportedPluginsConfig.pveMode.noDealDamageIfCooldownAndTeamOwner)
                    return !(bool)ins.PveMode.Call("CanTimeOwner", ins.Name, (ulong)player.userID, ins._config.supportedPluginsConfig.pveMode.cooldown);

                return false;
            }

            internal static bool IsPveModeBlockLooting(BasePlayer player)
            {
                if (!IsPveModeReady())
                    return false;

                BasePlayer eventOwner = GetEventOwner();

                if (eventOwner == null)
                    return false;

                if (ins._config.supportedPluginsConfig.pveMode.canLootOnlyOwner && !IsTeam(player, eventOwner.userID))
                    return true;

                return false;
            }

            static bool IsTeam(BasePlayer player, ulong targetId)
            {
                if (player.userID == targetId)
                    return true;

                if (player.currentTeam != 0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);

                    if (playerTeam == null)
                        return false;

                    if (playerTeam.members.Contains(targetId))
                        return true;
                }
                return false;
            }
        }

        class EventMapMarker : FacepunchBehaviour
        {
            static EventMapMarker eventMapMarker;

            MapMarkerGenericRadius radiusMarker;
            VendingMachineMapMarker vendingMarker;
            Coroutine updateCounter;

            internal static EventMapMarker CreateMarker()
            {
                if (!ins._config.markerConfig.enable)
                    return null;

                GameObject gameObject = new GameObject();
                gameObject.layer = (int)Rust.Layer.Reserved1;
                eventMapMarker = gameObject.AddComponent<EventMapMarker>();
                eventMapMarker.Init();
                return eventMapMarker;
            }

            void Init()
            {
                Vector3 eventPosition = ins.eventController.GetEventPosition();
                CreateRadiusMarker(eventPosition);
                CreateVendingMarker(eventPosition);
                updateCounter = ServerMgr.Instance.StartCoroutine(MarkerUpdateCounter());
            }

            void CreateRadiusMarker(Vector3 position)
            {
                if (!ins._config.markerConfig.useRingMarker)
                    return;

                radiusMarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", position) as MapMarkerGenericRadius;
                radiusMarker.enableSaving = false;
                radiusMarker.Spawn();
                radiusMarker.radius = ins._config.markerConfig.radius;
                radiusMarker.alpha = ins._config.markerConfig.alpha;
                radiusMarker.color1 = new Color(ins._config.markerConfig.color1.r, ins._config.markerConfig.color1.g, ins._config.markerConfig.color1.b);
                radiusMarker.color2 = new Color(ins._config.markerConfig.color2.r, ins._config.markerConfig.color2.g, ins._config.markerConfig.color2.b);
            }

            void CreateVendingMarker(Vector3 position)
            {
                if (!ins._config.markerConfig.useShopMarker)
                    return;

                vendingMarker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", position) as VendingMachineMapMarker;
                vendingMarker.Spawn();
                vendingMarker.markerShopName = $"{ins.eventController.eventConfig.displayName} ({NotifyManager.GetTimeMessage(null, ins.eventController.GetEventTime())})";
            }

            IEnumerator MarkerUpdateCounter()
            {
                while (EventLauncher.IsEventActive())
                {
                    Vector3 position = ins.eventController.GetEventPosition();
                    UpdateVendingMarker(position);
                    UpdateRadiusMarker(position);
                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            void UpdateRadiusMarker(Vector3 position)
            {
                if (!radiusMarker.IsExists())
                    return;

                radiusMarker.transform.position = position;
                radiusMarker.SendUpdate();
                radiusMarker.SendNetworkUpdate();
            }

            void UpdateVendingMarker(Vector3 position)
            {
                if (!vendingMarker.IsExists())
                    return;

                vendingMarker.transform.position = position;
                BasePlayer pveModeEventOwner = PveModeManager.UpdateAndGetEventOwner();
                string displayEventOwnerName = ins._config.supportedPluginsConfig.pveMode.showEventOwnerNameOnMap && pveModeEventOwner != null ? GetMessage("Marker_EventOwner", null, pveModeEventOwner.displayName) : "";
                vendingMarker.markerShopName = $"{ins.eventController.eventConfig.displayName} ({NotifyManager.GetTimeMessage(null, ins.eventController.GetEventTime())}) {displayEventOwnerName}";
                vendingMarker.SetFlag(BaseEntity.Flags.Busy, pveModeEventOwner == null);
                vendingMarker.SendNetworkUpdate();
            }

            internal static void DeleteMapMarker()
            {
                if (eventMapMarker != null)
                    eventMapMarker.Delete();
            }

            void Delete()
            {
                if (radiusMarker.IsExists())
                    radiusMarker.Kill();

                if (vendingMarker.IsExists())
                    vendingMarker.Kill();

                if (updateCounter != null)
                    ServerMgr.Instance.StopCoroutine(updateCounter);

                Destroy(eventMapMarker.gameObject);
            }
        }

        static class NpcSpawnManager
        {
            internal static HashSet<ScientistNPC> eventNpcs = new HashSet<ScientistNPC>();

            internal static int GetEventNpcCount()
            {
                return eventNpcs.Where(x => x.IsExists() && !x.isMounted).Count;
            }

            internal static void OnEventNpcKill()
            {

            }

            internal static HashSet<ulong> GetEventNpcNetIds()
            {
                HashSet<ulong> result = new HashSet<ulong>();

                foreach (ScientistNPC scientistNPC in eventNpcs)
                    if (scientistNPC != null && scientistNPC.net != null)
                        result.Add(scientistNPC.net.ID.Value);

                return result;
            }

            internal static ScientistNPC GetScientistByNetId(ulong netId)
            {
                return eventNpcs.FirstOrDefault(x => x != null && x.net != null && x.net.ID.Value == netId);
            }

            internal static bool IsNpcSpawnReady()
            {
                if (!ins.plugins.Exists("NpcSpawn"))
                {
                    ins.PrintError("NpcSpawn plugin doesn`t exist! Please read the file ReadMe.txt. NPCs will not spawn!");
                    ins.NextTick(() => Interface.Oxide.UnloadPlugin(ins.Name));
                    return false;
                }
                else
                    return true;
            }

            internal static bool IsEventNpc(ScientistNPC scientistNPC)
            {
                return scientistNPC != null && ins._config.npcConfigs.Any(x => x.displayName == scientistNPC.displayName);
            }

            internal static ScientistNPC SpawnScientistNpc(string npcPresetName, Vector3 position, float healthFraction, bool isStationary, bool isPassive = false)
            {
                NpcConfig npcConfig = GetNpcConfigByPresetName(npcPresetName);
                if (npcConfig == null)
                {
                    NotifyManager.PrintError(null, "PresetNotFound_Exeption", npcPresetName);
                    return null;
                }

                ScientistNPC scientistNPC = SpawnScientistNpc(npcConfig, position, healthFraction, isStationary, isPassive);

                if (isStationary)
                    UpdateClothesWeight(scientistNPC);

                return scientistNPC;
            }

            internal static ScientistNPC SpawnScientistNpc(NpcConfig npcConfig, Vector3 position, float healthFraction, bool isStationary, bool isPassive)
            {
                JObject baseNpcConfigObj = GetBaseNpcConfig(npcConfig, healthFraction, isStationary, isPassive);
                ScientistNPC scientistNPC = (ScientistNPC)ins.NpcSpawn.Call("SpawnNpc", position, baseNpcConfigObj, isPassive);
                eventNpcs.Add(scientistNPC);
                return scientistNPC;
            }

            internal static NpcConfig GetNpcConfigByDisplayName(string displayName)
            {
                return ins._config.npcConfigs.FirstOrDefault(x => x.displayName == displayName);
            }

            static NpcConfig GetNpcConfigByPresetName(string npcPresetName)
            {
                return ins._config.npcConfigs.FirstOrDefault(x => x.presetName == npcPresetName);
            }

            static JObject GetBaseNpcConfig(NpcConfig config, float healthFraction, bool isStationary, bool isPassive)
            {
                JArray wearItems = WagonCustomizator.GetCustomizeNpcWearSet();

                if (wearItems == null)
                {
                    wearItems = new JArray
                    {
                        config.wearItems.Select(x => new JObject
                        {
                            ["ShortName"] = x.shortName,
                            ["SkinID"] = x.skinID
                        })
                    };
                }

                return new JObject
                {
                    ["Name"] = config.displayName,
                    ["WearItems"] = wearItems,
                    ["BeltItems"] = isPassive ? new JArray() : new JArray { config.beltItems.Select(x => new JObject { ["ShortName"] = x.shortName, ["Amount"] = x.amount, ["SkinID"] = x.skinID, ["mods"] = new JArray { x.mods.ToHashSet() }, ["Ammo"] = x.ammo }) },
                    ["Kit"] = config.kit,
                    ["Health"] = config.health * healthFraction,
                    ["RoamRange"] = isStationary ? 0 : config.roamRange,
                    ["ChaseRange"] = isStationary ? 0 : config.chaseRange,
                    ["SenseRange"] = config.senseRange,
                    ["ListenRange"] = config.senseRange / 2,
                    ["AttackRangeMultiplier"] = config.attackRangeMultiplier,
                    ["CheckVisionCone"] = true,
                    ["VisionCone"] = config.visionCone,
                    ["HostileTargetsOnly"] = false,
                    ["DamageScale"] = config.damageScale,
                    ["TurretDamageScale"] = config.turretDamageScale,
                    ["AimConeScale"] = config.aimConeScale,
                    ["DisableRadio"] = config.disableRadio,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = isStationary ? 0 : config.speed,
                    ["AreaMask"] = 1,
                    ["AgentTypeID"] = -1372625422,
                    ["HomePosition"] = string.Empty,
                    ["MemoryDuration"] = config.memoryDuration,
                    ["States"] = isPassive ? new JArray() : isStationary ? new JArray { "IdleState", "CombatStationaryState" } : config.beltItems.Any(x => x.shortName == "rocket.launcher" || x.shortName == "explosive.timed") ? new JArray { "RaidState", "RoamState", "ChaseState", "CombatState" } : new JArray { "RoamState", "ChaseState", "CombatState" }
                };
            }

            static void UpdateClothesWeight(ScientistNPC scientistNPC)
            {
                foreach (Item item in scientistNPC.inventory.containerWear.itemList)
                {
                    ItemModWearable component = item.info.GetComponent<ItemModWearable>();

                    if (component != null)
                        component.weight = 0;
                }
            }

            internal static void ClearData(bool shoudKillNpcs)
            {
                if (shoudKillNpcs)
                    foreach (ScientistNPC scientistNPC in eventNpcs)
                        if (scientistNPC.IsExists())
                            scientistNPC.Kill();

                eventNpcs.Clear();
            }
        }

        class PositionData
        {
            internal Vector3 position;
            internal Quaternion rotation;

            internal PositionData(Vector3 position, Quaternion rotation)
            {
                this.position = position;
                this.rotation = rotation;
            }
        }

        static class LootManager
        {
            static HashSet<ulong> lootedContainersUids = new HashSet<ulong>();
            static HashSet<StorageContainerData> storageContainers = new HashSet<StorageContainerData>();
            static int countOfUnlootedCrates;

            internal static int GetCountOfUnlootedCrates()
            {
                return countOfUnlootedCrates;
            }

            internal static void UpdateCountOfUnlootedCrates()
            {
                countOfUnlootedCrates = storageContainers.Where(x => x != null && x.storageContainer.IsExists() && x.storageContainer.net != null && !IsCrateLooted(x.storageContainer.net.ID.Value)).Count;
            }

            internal static void OnHeliCrateSpawned(LockedByEntCrate lockedByEntCrate)
            {
                EventHeli eventHeli = EventHeli.GetClosestHeli(lockedByEntCrate.transform.position);

                if (eventHeli == null)
                    return;

                if (Vector3.Distance(lockedByEntCrate.transform.position, eventHeli.transform.position) <= 10)
                {
                    lockedByEntCrate.Invoke(() =>
                    {
                        UpdateBaseLootTable(lockedByEntCrate.inventory, eventHeli.heliConfig.baseLootTableConfig, eventHeli.heliConfig.baseLootTableConfig.clearDefaultItemList);

                        if (eventHeli.heliConfig.instCrateOpen)
                        {
                            lockedByEntCrate.SetLockingEnt(null);
                            lockedByEntCrate.SetLocked(false);
                        }

                        if (eventHeli.heliConfig.cratesLifeTime > 0)
                        {
                            lockedByEntCrate.CancelInvoke(lockedByEntCrate.RemoveMe);
                            lockedByEntCrate.Invoke(lockedByEntCrate.RemoveMe, eventHeli.heliConfig.cratesLifeTime);
                        }

                    }, 1f);

                    if (PveModeManager.IsPveModeReady())
                        ins.PveMode.Call("EventAddCrates", ins.Name, new HashSet<ulong> { lockedByEntCrate.net.ID.Value });
                }
            }

            internal static void OnEventCrateLooted(StorageContainer storageContainer, ulong userId)
            {
                if (storageContainer.net == null)
                    return;

                if (!IsCrateLooted(storageContainer.net.ID.Value))
                {
                    double cratePoint;

                    if (ins._config.supportedPluginsConfig.economicsConfig.crates.TryGetValue(storageContainer.PrefabName, out cratePoint))
                        EconomyManager.AddBalance(userId, cratePoint);

                    lootedContainersUids.Add(storageContainer.net.ID.Value);
                }

                UpdateCountOfUnlootedCrates();
            }

            internal static bool IsCrateLooted(ulong netID)
            {
                return lootedContainersUids.Contains(netID);
            }

            internal static bool IsEventCrate(ulong netID)
            {
                return GetContainerDataByNetId(netID) != null;
            }

            internal static StorageContainerData GetContainerDataByNetId(ulong netID)
            {
                return storageContainers.FirstOrDefault(x => x != null && x.storageContainer.IsExists() && x.storageContainer.net != null && x.storageContainer.net.ID.Value == netID);
            }

            internal static HashSet<ulong> GetEventCratesNetIDs()
            {
                HashSet<ulong> eventCrates = new HashSet<ulong>();

                foreach (StorageContainerData storageContainerData in storageContainers)
                    if (storageContainerData != null && storageContainerData.storageContainer != null && storageContainerData.storageContainer.net != null)
                        eventCrates.Add(storageContainerData.storageContainer.net.ID.Value);

                return eventCrates;
            }

            internal static CrateConfig GetCrateConfigByPresetName(string presetName)
            {
                return ins._config.crateConfigs.FirstOrDefault(x => x.presetName == presetName);
            }

            internal static void InitialLootManagerUpdate()
            {
                LootPrefabController.FindPrefabs();
                UpdateLootTables();
            }

            static void UpdateLootTables()
            {
                foreach (CrateConfig crateConfig in ins._config.crateConfigs)
                    UpdateBaseLootTable(crateConfig.lootTableConfig);

                foreach (NpcConfig npcConfig in ins._config.npcConfigs)
                    UpdateBaseLootTable(npcConfig.lootTableConfig);

                foreach (HeliConfig heliConfig in ins._config.heliConfigs)
                    UpdateBaseLootTable(heliConfig.baseLootTableConfig);

                ins.SaveConfig();
            }

            static void UpdateBaseLootTable(BaseLootTableConfig baseLootTableConfig)
            {
                for (int i = 0; i < baseLootTableConfig.items.Count; i++)
                {
                    LootItemConfig lootItemConfig = baseLootTableConfig.items[i];

                    if (lootItemConfig.chance <= 0)
                        baseLootTableConfig.items.RemoveAt(i);
                }

                baseLootTableConfig.items = baseLootTableConfig.items.OrderByQuickSort(x => x.chance);

                if (baseLootTableConfig.maxItemsAmount > baseLootTableConfig.items.Count)
                    baseLootTableConfig.maxItemsAmount = baseLootTableConfig.items.Count;

                if (baseLootTableConfig.minItemsAmount > baseLootTableConfig.maxItemsAmount)
                    baseLootTableConfig.minItemsAmount = baseLootTableConfig.maxItemsAmount;
            }

            internal static void UpdateItemContainer(ItemContainer itemContainer, LootTableConfig lootTableConfig, bool deleteItems = false)
            {
                UpdateLootTable(itemContainer, lootTableConfig, deleteItems);
            }

            internal static void UpdateStorageContainer(StorageContainer storageContainer, CrateConfig crateConfig)
            {
                storageContainer.onlyAcceptCategory = ItemCategory.All;
                UpdateLootTable(storageContainer.inventory, crateConfig.lootTableConfig, false);
            }

            internal static void AddContainerData(StorageContainer storageContainer, CrateConfig crateConfig)
            {
                storageContainers.Add(new StorageContainerData(storageContainer, crateConfig.presetName));
            }

            internal static void UpdateLootContainer(LootContainer lootContainer, CrateConfig crateConfig)
            {
                HackableLockedCrate hackableLockedCrate = lootContainer as HackableLockedCrate;
                if (hackableLockedCrate != null)
                {
                    if (hackableLockedCrate.mapMarkerInstance.IsExists())
                    {
                        hackableLockedCrate.mapMarkerInstance.Kill();
                        hackableLockedCrate.mapMarkerInstance = null;
                    }

                    hackableLockedCrate.Invoke(() => DelayUpdateHackableLockedCrate(hackableLockedCrate, crateConfig), 1f);
                }

                SupplyDrop supplyDrop = lootContainer as SupplyDrop;
                if (supplyDrop != null)
                {
                    supplyDrop.RemoveParachute();
                    supplyDrop.MakeLootable();
                }

                FreeableLootContainer freeableLootContainer = lootContainer as FreeableLootContainer;
                if (freeableLootContainer != null)
                    freeableLootContainer.SetFlag(BaseEntity.Flags.Reserved8, false);

                lootContainer.Invoke(() => UpdateLootTable(lootContainer.inventory, crateConfig.lootTableConfig, crateConfig.lootTableConfig.clearDefaultItemList), 2f);
            }

            static void DelayUpdateHackableLockedCrate(HackableLockedCrate hackableLockedCrate, CrateConfig crateConfig)
            {
                if (hackableLockedCrate == null || crateConfig.hackTime < 0)
                    return;

                hackableLockedCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - crateConfig.hackTime;
                UpdateLootTable(hackableLockedCrate.inventory, crateConfig.lootTableConfig, crateConfig.lootTableConfig.clearDefaultItemList);
                hackableLockedCrate.CancelInvoke(hackableLockedCrate.DelayedDestroy);
                hackableLockedCrate.InvokeRepeating(() => hackableLockedCrate.SendNetworkUpdate(), 1f, 1f);
            }

            internal static void UpdateCrateHackTime(HackableLockedCrate hackableLockedCrate, string cratePresetName)
            {
                CrateConfig crateConfig = GetCrateConfigByPresetName(cratePresetName);

                if (crateConfig.hackTime < 0)
                    return;

                hackableLockedCrate.Invoke(() => hackableLockedCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - crateConfig.hackTime, 1.1f);
            }

            static void UpdateLootTable(ItemContainer itemContainer, LootTableConfig lootTableConfig, bool clearContainer)
            {
                if (itemContainer == null)
                    return;

                UpdateBaseLootTable(itemContainer, lootTableConfig, clearContainer || !string.IsNullOrEmpty(lootTableConfig.alphaLootPresetName));

                if (!string.IsNullOrEmpty(lootTableConfig.alphaLootPresetName))
                {
                    if (ins.plugins.Exists("AlphaLoot") && (bool)ins.AlphaLoot.Call("ProfileExists", lootTableConfig.alphaLootPresetName))
                    {
                        ins.AlphaLoot.Call("PopulateLoot", itemContainer, lootTableConfig.alphaLootPresetName);
                    }
                }
            }

            static void UpdateBaseLootTable(ItemContainer itemContainer, BaseLootTableConfig baseLootTableConfig, bool clearContainer)
            {
                if (itemContainer == null)
                    return;

                if (clearContainer)
                    ClearItemsContainer(itemContainer);

                LootPrefabController.TryAddLootFromPrefabs(itemContainer, baseLootTableConfig.prefabConfigs);
                RandomItemsFiller.TryAddItemsToContainer(itemContainer, baseLootTableConfig);

                if (itemContainer.capacity < itemContainer.itemList.Count)
                    itemContainer.capacity = itemContainer.itemList.Count;
            }

            static void ClearItemsContainer(ItemContainer container)
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    item.RemoveFromContainer();
                    item.Remove();
                }
            }

            internal static void ClearLootData(bool shoudKillCrates = false)
            {
                if (shoudKillCrates)
                    foreach (StorageContainerData storageContainerData in storageContainers)
                        if (storageContainerData != null && storageContainerData.storageContainer.IsExists())
                            storageContainerData.storageContainer.Kill();

                lootedContainersUids.Clear();
                storageContainers.Clear();
            }

            class LootPrefabController
            {
                static HashSet<LootPrefabController> lootPrefabDatas = new HashSet<LootPrefabController>();

                string prefabName;
                LootContainer.LootSpawnSlot[] lootSpawnSlot;
                LootSpawn lootDefinition;
                int maxDefinitionsToSpawn;
                int scrapAmount;

                internal static void TryAddLootFromPrefabs(ItemContainer itemContainer, PrefabLootTableConfigs prefabLootTableConfig)
                {
                    if (!prefabLootTableConfig.isEnable)
                        return;

                    PrefabConfig prefabConfig = prefabLootTableConfig.prefabs.GetRandom();

                    if (prefabConfig == null)
                        return;

                    int multiplicator = UnityEngine.Random.Range(prefabConfig.minLootScale, prefabConfig.maxLootScale + 1);
                    TryFillContainerByPrefab(itemContainer, prefabConfig.prefabName, multiplicator);
                }

                internal static void FindPrefabs()
                {
                    foreach (CrateConfig crateConfig in ins._config.crateConfigs.Where(x => x.lootTableConfig.prefabConfigs.isEnable))
                        foreach (PrefabConfig prefabConfig in crateConfig.lootTableConfig.prefabConfigs.prefabs)
                            TrySaveLootPrefab(prefabConfig.prefabName);

                    foreach (NpcConfig npcConfig in ins._config.npcConfigs.Where(x => x.lootTableConfig.prefabConfigs.isEnable))
                        foreach (PrefabConfig prefabConfig in npcConfig.lootTableConfig.prefabConfigs.prefabs)
                            TrySaveLootPrefab(prefabConfig.prefabName);

                    foreach (HeliConfig heliConfig in ins._config.heliConfigs.Where(x => x.baseLootTableConfig.prefabConfigs.isEnable))
                        foreach (PrefabConfig prefabConfig in heliConfig.baseLootTableConfig.prefabConfigs.prefabs)
                            TrySaveLootPrefab(prefabConfig.prefabName);
                }

                internal static void TrySaveLootPrefab(string prefabName)
                {
                    if (lootPrefabDatas.Any(x => x.prefabName == prefabName))
                        return;

                    GameObject gameObject = GameManager.server.FindPrefab(prefabName);

                    if (gameObject == null)
                        return;

                    LootContainer lootContainer = gameObject.GetComponent<LootContainer>();

                    if (lootContainer != null)
                    {
                        SaveLootPrefabData(prefabName, lootContainer.LootSpawnSlots, lootContainer.scrapAmount, lootContainer.lootDefinition, lootContainer.maxDefinitionsToSpawn);
                        return;
                    }

                    global::HumanNPC humanNPC = gameObject.GetComponent<global::HumanNPC>();

                    if (humanNPC != null && humanNPC.LootSpawnSlots.Length > 0)
                    {
                        SaveLootPrefabData(prefabName, humanNPC.LootSpawnSlots, 0);
                        return;
                    }

                    ScarecrowNPC scarecrowNPC = gameObject.GetComponent<ScarecrowNPC>();

                    if (scarecrowNPC != null && scarecrowNPC.LootSpawnSlots.Length > 0)
                    {
                        SaveLootPrefabData(prefabName, scarecrowNPC.LootSpawnSlots, 0);
                        return;
                    }
                }

                internal static void SaveLootPrefabData(string prefabName, LootContainer.LootSpawnSlot[] lootSpawnSlot, int scrapAmount, LootSpawn lootDefinition = null, int maxDefinitionsToSpawn = 0)
                {
                    LootPrefabController lootPrefabData = new LootPrefabController
                    {
                        prefabName = prefabName,
                        lootSpawnSlot = lootSpawnSlot,
                        lootDefinition = lootDefinition,
                        maxDefinitionsToSpawn = maxDefinitionsToSpawn,
                        scrapAmount = scrapAmount
                    };

                    lootPrefabDatas.Add(lootPrefabData);
                }

                internal static void TryFillContainerByPrefab(ItemContainer itemContainer, string prefabName, int multiplicator)
                {
                    LootPrefabController lootPrefabData = GetDataForPrefabName(prefabName);

                    if (lootPrefabData != null)
                        for (int i = 0; i < multiplicator; i++)
                            lootPrefabData.SpawnPrefabLootInCrate(itemContainer);
                }

                static LootPrefabController GetDataForPrefabName(string prefabName)
                {
                    return lootPrefabDatas.FirstOrDefault(x => x.prefabName == prefabName);
                }

                void SpawnPrefabLootInCrate(ItemContainer itemContainer)
                {
                    if (lootSpawnSlot != null && lootSpawnSlot.Length > 0)
                    {
                        foreach (LootContainer.LootSpawnSlot lootSpawnSlot in lootSpawnSlot)
                            for (int j = 0; j < lootSpawnSlot.numberToSpawn; j++)
                                if (UnityEngine.Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                                    lootSpawnSlot.definition.SpawnIntoContainer(itemContainer);
                    }
                    else if (lootDefinition != null)
                    {
                        for (int i = 0; i < maxDefinitionsToSpawn; i++)
                            lootDefinition.SpawnIntoContainer(itemContainer);
                    }

                    GenerateScrap(itemContainer);
                }

                void GenerateScrap(ItemContainer itemContainer)
                {
                    if (scrapAmount <= 0)
                        return;

                    Item item = ItemManager.CreateByName("scrap", scrapAmount, 0);

                    if (item == null)
                        return;

                    if (!item.MoveToContainer(itemContainer))
                        item.Remove();
                }
            }

            static class RandomItemsFiller
            {
                static Dictionary<char, GrowableGenetics.GeneType> charToGene = new Dictionary<char, GrowableGenetics.GeneType>
                {
                    ['g'] = GrowableGenetics.GeneType.GrowthSpeed,
                    ['y'] = GrowableGenetics.GeneType.Yield,
                    ['h'] = GrowableGenetics.GeneType.Hardiness,
                    ['w'] = GrowableGenetics.GeneType.WaterRequirement,
                };

                internal static void TryAddItemsToContainer(ItemContainer itemContainer, BaseLootTableConfig baseLootTableConfig)
                {
                    if (!baseLootTableConfig.isRandomItemsEnable)
                        return;

                    HashSet<int> includeItemIndexes = new HashSet<int>();
                    int targetItemsCount = UnityEngine.Random.Range(baseLootTableConfig.minItemsAmount, baseLootTableConfig.maxItemsAmount + 1);

                    while (includeItemIndexes.Count < targetItemsCount)
                    {
                        if (!baseLootTableConfig.items.Any(x => x.chance >= 0.1f && !includeItemIndexes.Contains(baseLootTableConfig.items.IndexOf(x))))
                            break;

                        for (int i = 0; i < baseLootTableConfig.items.Count; i++)
                        {
                            if (includeItemIndexes.Contains(i))
                                continue;

                            LootItemConfig lootItemConfig = baseLootTableConfig.items[i];
                            float chance = UnityEngine.Random.Range(0.0f, 100.0f);

                            if (chance <= lootItemConfig.chance)
                            {
                                Item item = CreateItem(lootItemConfig);
                                includeItemIndexes.Add(i);

                                if (itemContainer.itemList.Count >= itemContainer.capacity)
                                    itemContainer.capacity += 1;

                                if (item == null || !item.MoveToContainer(itemContainer))
                                    item.Remove();

                                if (includeItemIndexes.Count == targetItemsCount)
                                    return;
                            }
                        }
                    }
                }

                internal static Item CreateItem(LootItemConfig lootItemConfig)
                {
                    int amount = UnityEngine.Random.Range((int)(lootItemConfig.minAmount), (int)(lootItemConfig.maxAmount + 1));

                    if (amount <= 0)
                        amount = 1;

                    return CreateItem(lootItemConfig, amount);
                }

                internal static Item CreateItem(LootItemConfig itemConfig, int amount)
                {
                    Item item = null;

                    if (itemConfig.isBlueprint)
                    {
                        item = ItemManager.CreateByName("blueprintbase");
                        item.blueprintTarget = ItemManager.FindItemDefinition(itemConfig.shortname).itemid;
                    }
                    else
                        item = ItemManager.CreateByName(itemConfig.shortname, amount, itemConfig.skin);

                    if (item == null)
                    {
                        ins.PrintWarning($"Failed to create item! ({itemConfig.shortname})");
                        return null;
                    }

                    if (!string.IsNullOrEmpty(itemConfig.name))
                        item.name = itemConfig.name;

                    if (itemConfig.genomes != null && itemConfig.genomes.Count > 0)
                    {
                        string genome = itemConfig.genomes.GetRandom();
                        UpdateGenome(item, genome);
                    }

                    return item;
                }

                static void UpdateGenome(Item item, string genome)
                {
                    genome = genome.ToLower();
                    GrowableGenes growableGenes = new GrowableGenes();

                    for (int i = 0; i < 6 && i < genome.Length; ++i)
                    {
                        GrowableGenetics.GeneType geneType;

                        if (!charToGene.TryGetValue(genome[i], out geneType))
                            geneType = GrowableGenetics.GeneType.Empty;

                        growableGenes.Genes[i].Set(geneType, true);
                        GrowableGeneEncoding.EncodeGenesToItem(GrowableGeneEncoding.EncodeGenesToInt(growableGenes), item);
                    }

                }
            }
        }

        class StorageContainerData
        {
            public StorageContainer storageContainer;
            public string presetName;

            public StorageContainerData(StorageContainer storageContainer, string presetName)
            {
                this.storageContainer = storageContainer;
                this.presetName = presetName;
            }
        }

        static class BuildManager
        {
            internal static void UpdateMeshColliders(BaseEntity entity)
            {
                MeshCollider[] meshColliders = entity.GetComponentsInChildren<MeshCollider>();

                for (int i = 0; i < meshColliders.Length; i++)
                {
                    MeshCollider meshCollider = meshColliders[i];
                    meshCollider.convex = true;
                }
            }

            internal static BaseEntity SpawnChildEntity(BaseEntity parrentEntity, string prefabName, LocationConfig locationConfig, ulong skinId, bool isDecor)
            {
                Vector3 localPosition = locationConfig.position.ToVector3();
                Vector3 localRotation = locationConfig.rotation.ToVector3();
                return SpawnChildEntity(parrentEntity, prefabName, localPosition, localRotation, skinId, isDecor);
            }

            internal static BaseEntity SpawnRegularEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0, bool enableSaving = false)
            {
                BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, enableSaving);
                entity.Spawn();
                return entity;
            }

            internal static BaseEntity SpawnStaticEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0)
            {
                BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, false);
                DestroyUnnessesaryComponents(entity);

                StabilityEntity stabilityEntity = entity as StabilityEntity;
                if (stabilityEntity != null)
                    stabilityEntity.grounded = true;

                BaseCombatEntity baseCombatEntity = entity as BaseCombatEntity;
                if (baseCombatEntity != null)
                    baseCombatEntity.pickup.enabled = false;

                entity.Spawn();
                return entity;
            }

            internal static BaseEntity SpawnChildEntity(BaseEntity parrentEntity, string prefabName, Vector3 localPosition, Vector3 localRotation, ulong skinId = 0, bool isDecor = true, bool enableSaving = false)
            {
                BaseEntity entity = isDecor ? CreateDecorEntity(prefabName, parrentEntity.transform.position, Quaternion.identity, skinId) : CreateEntity(prefabName, parrentEntity.transform.position, Quaternion.identity, skinId, enableSaving);

                if (entity == null)
                    return null;

                SetParent(parrentEntity, entity, localPosition, localRotation);

                DestroyUnnessesaryComponents(entity);

                if (isDecor)
                    DestroyDecorComponents(entity);

                //UpdateMeshColliders(entity);
                entity.Spawn();
                return entity;
            }

            internal static void UpdateEntityMaxHealth(BaseCombatEntity baseCombatEntity, float maxHealth)
            {
                baseCombatEntity.startHealth = maxHealth;
                baseCombatEntity.InitializeHealth(maxHealth, maxHealth);
            }

            internal static BaseEntity CreateEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId, bool enableSaving)
            {
                BaseEntity entity = GameManager.server.CreateEntity(prefabName, position, rotation);
                entity.enableSaving = enableSaving;
                entity.skinID = skinId;
                return entity;
            }

            static BaseEntity CreateDecorEntity(string prefabName, Vector3 position, Quaternion rotation, ulong skinId = 0, bool enableSaving = false)
            {
                BaseEntity entity = CreateEntity(prefabName, position, rotation, skinId, enableSaving);

                BaseEntity trueBaseEntity = entity.gameObject.AddComponent<BaseEntity>();
                CopySerializableFields(entity, trueBaseEntity);
                UnityEngine.Object.DestroyImmediate(entity, true);
                entity.SetFlag(BaseEntity.Flags.Busy, true);
                entity.SetFlag(BaseEntity.Flags.Locked, true);

                return trueBaseEntity;
            }

            internal static void SetParent(BaseEntity parrentEntity, BaseEntity childEntity, Vector3 localPosition, Vector3 localRotation)
            {
                childEntity.SetParent(parrentEntity, true, false);
                childEntity.transform.localPosition = localPosition;
                childEntity.transform.localEulerAngles = localRotation;
            }

            static void DestroyDecorComponents(BaseEntity entity)
            {
                Component[] components = entity.GetComponentsInChildren<Component>();

                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];

                    EntityCollisionMessage entityCollisionMessage = component as EntityCollisionMessage;

                    if (entityCollisionMessage != null || (component != null && component.name != entity.PrefabName))
                    {
                        Transform transform = component as Transform;
                        if (transform != null)
                            continue;

                        Collider collider = component as Collider;
                        if (collider != null && collider is MeshCollider == false)
                            continue;

                        if (component is Model)
                            continue;

                        UnityEngine.GameObject.DestroyImmediate(component as UnityEngine.Object);
                    }
                }
            }

            internal static void DestroyUnnessesaryComponents(BaseEntity entity)
            {
                DestroyEntityConponent<GroundWatch>(entity);
                DestroyEntityConponent<DestroyOnGroundMissing>(entity);
                DestroyEntityConponent<TriggerHurtEx>(entity);

                if (entity is BradleyAPC == false)
                    DestroyEntityConponent<Rigidbody>(entity);
            }

            internal static void DestroyEntityConponent<TypeForDestroy>(BaseEntity entity)
            {
                if (entity == null)
                    return;

                TypeForDestroy component = entity.GetComponent<TypeForDestroy>();
                if (component != null)
                    UnityEngine.GameObject.DestroyImmediate(component as UnityEngine.Object);
            }

            internal static void DestroyEntityConponents<TypeForDestroy>(BaseEntity entity)
            {
                if (entity == null)
                    return;

                TypeForDestroy[] components = entity.GetComponentsInChildren<TypeForDestroy>();

                for (int i = 0; i < components.Length; i++)
                {
                    TypeForDestroy component = components[i];

                    if (component != null)
                        UnityEngine.GameObject.DestroyImmediate(component as UnityEngine.Object);
                }
            }

            internal static void CopySerializableFields<T>(T src, T dst)
            {
                FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo field in srcFields)
                {
                    object value = field.GetValue(src);
                    field.SetValue(dst, value);
                }
            }
        }

        static class PositionDefiner
        {
            internal static Vector3 GetGlobalPosition(Transform parentTransform, Vector3 position)
            {
                return parentTransform.transform.TransformPoint(position);
            }

            internal static Vector3 GetGroundPositionInPoint(Vector3 position)
            {
                position.y = 100;
                RaycastHit raycastHit;

                if (Physics.Raycast(position, Vector3.down, out raycastHit, 500, 1 << 16 | 1 << 23))
                    position.y = raycastHit.point.y;

                return position;
            }

            internal static Quaternion GetGlobalRotation(Transform parentTransform, Vector3 rotation)
            {
                return parentTransform.rotation * Quaternion.Euler(rotation);
            }

            internal static bool GetNavmeshInPoint(Vector3 position, float radius, out NavMeshHit navMeshHit)
            {
                return NavMesh.SamplePosition(position, out navMeshHit, radius, 1);
            }
        }

        static class NotifyManager
        {
            internal static void PrintInfoMessage(BasePlayer player, string langKey, params object[] args)
            {
                if (player == null)
                    ins.PrintWarning(ClearColorAndSize(GetMessage(langKey, null, args)));
                else
                    ins.PrintToChat(player, GetMessage(langKey, player.UserIDString, args));
            }

            internal static void PrintError(BasePlayer player, string langKey, params object[] args)
            {
                if (player == null)
                    ins.PrintError(ClearColorAndSize(GetMessage(langKey, null, args)));
                else
                    ins.PrintToChat(player, GetMessage(langKey, player.UserIDString, args));
            }

            internal static void PrintLogMessage(string langKey, params object[] args)
            {
                for (int i = 0; i < args.Length; i++)
                    if (args[i] is int)
                        args[i] = GetTimeMessage(null, (int)args[i]);

                ins.Puts(ClearColorAndSize(GetMessage(langKey, null, args)));
            }

            internal static void PrintWarningMessage(string langKey, params object[] args)
            {
                ins.PrintWarning(ClearColorAndSize(GetMessage(langKey, null, args)));
            }

            internal static string ClearColorAndSize(string message)
            {
                message = message.Replace("</color>", string.Empty);
                message = message.Replace("</size>", string.Empty);
                while (message.Contains("<color="))
                {
                    int index = message.IndexOf("<color=");
                    message = message.Remove(index, message.IndexOf(">", index) - index + 1);
                }
                while (message.Contains("<size="))
                {
                    int index = message.IndexOf("<size=");
                    message = message.Remove(index, message.IndexOf(">", index) - index + 1);
                }
                return message;
            }

            internal static void SendMessageToAll(string langKey, params object[] args)
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    if (player != null)
                        SendMessageToPlayer(player, langKey, args);

                TrySendDiscordMessage(langKey, args);
            }

            internal static void SendMessageToPlayer(BasePlayer player, string langKey, params object[] args)
            {
                for (int i = 0; i < args.Length; i++)
                    if (args[i] is int)
                        args[i] = GetTimeMessage(player.UserIDString, (int)args[i]);

                string playerMessage = GetMessage(langKey, player.UserIDString, args);

                if (ins._config.notifyConfig.isChatEnable)
                    ins.PrintToChat(player, playerMessage);

                if (ins._config.notifyConfig.gameTipConfig.isEnabled)
                    player.SendConsoleCommand("gametip.showtoast", ins._config.notifyConfig.gameTipConfig.style, ClearColorAndSize(playerMessage), string.Empty);

                if (ins._config.supportedPluginsConfig.guiAnnouncementsConfig.isEnabled && ins.plugins.Exists("guiAnnouncementsConfig"))
                    ins.GUIAnnouncements?.Call("CreateAnnouncement", ClearColorAndSize(playerMessage), ins._config.supportedPluginsConfig.guiAnnouncementsConfig.bannerColor, ins._config.supportedPluginsConfig.guiAnnouncementsConfig.textColor, player, ins._config.supportedPluginsConfig.guiAnnouncementsConfig.apiAdjustVPosition);

                if (ins._config.supportedPluginsConfig.notifyPluginConfig.isEnabled && ins.plugins.Exists("Notify"))
                    ins.Notify?.Call("SendNotify", player, ins._config.supportedPluginsConfig.notifyPluginConfig.type, ClearColorAndSize(playerMessage));
            }

            internal static string GetTimeMessage(string userIDString, int seconds)
            {
                string message = "";

                TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
                if (timeSpan.Hours > 0) message += $" {timeSpan.Hours} {GetMessage("Hours", userIDString)}";
                if (timeSpan.Minutes > 0) message += $" {timeSpan.Minutes} {GetMessage("Minutes", userIDString)}";
                if (message == "") message += $" {timeSpan.Seconds} {GetMessage("Seconds", userIDString)}";

                return message;
            }

            static void TrySendDiscordMessage(string langKey, params object[] args)
            {
                if (CanSendDiscordMessage(langKey))
                {
                    for (int i = 0; i < args.Length; i++)
                        if (args[i] is int)
                            args[i] = GetTimeMessage(null, (int)args[i]);

                    object fields = new[] { new { name = ins.Title, value = ClearColorAndSize(GetMessage(langKey, null, args)), inline = false } };
                    ins.DiscordMessages?.Call("API_SendFancyMessage", ins._config.supportedPluginsConfig.discordMessagesConfig.webhookUrl, "", ins._config.supportedPluginsConfig.discordMessagesConfig.embedColor, JsonConvert.SerializeObject(fields), null, ins);
                }
            }

            static bool CanSendDiscordMessage(string langKey)
            {
                return ins._config.supportedPluginsConfig.discordMessagesConfig.keys.Contains(langKey) && ins._config.supportedPluginsConfig.discordMessagesConfig.isEnabled && !string.IsNullOrEmpty(ins._config.supportedPluginsConfig.discordMessagesConfig.webhookUrl) && ins._config.supportedPluginsConfig.discordMessagesConfig.webhookUrl != "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";
            }
        }

        static class GuiManager
        {
            static bool isLoadingImageFailed;
            const float tabWidth = 109;
            const float tabHeigth = 25;
            static ImageInfo tabImageInfo = new ImageInfo("Tab_Adem");
            static List<ImageInfo> iconImageInfos = new List<ImageInfo>
            {
                new ImageInfo("Clock_Adem"),
                new ImageInfo("Crates_Adem"),
                new ImageInfo("Soldiers_Adem"),
            };

            internal static void LoadImages()
            {
                ServerMgr.Instance.StartCoroutine(LoadImagesCoroutine());
            }

            static IEnumerator LoadImagesCoroutine()
            {
                yield return LoadTabCoroutine();

                if (!isLoadingImageFailed)
                    yield return LoadIconsCoroutine();
            }

            static IEnumerator LoadTabCoroutine()
            {
                string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "Images/" + tabImageInfo.imageName + ".png";

                using (WWW www = new WWW(url))
                {
                    yield return www;

                    if (www.error != null)
                    {
                        OnImageSaveFailed(tabImageInfo.imageName);
                        isLoadingImageFailed = true;
                    }
                    else
                    {
                        Texture2D texture = www.texture;
                        uint imageId = FileStorage.server.Store(texture.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                        tabImageInfo.imageId = imageId.ToString();
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }

            static IEnumerator LoadIconsCoroutine()
            {
                for (int i = 0; i < iconImageInfos.Count; i++)
                {
                    ImageInfo imageInfo = iconImageInfos[i];
                    string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "Images/" + imageInfo.imageName + ".png";

                    using (WWW www = new WWW(url))
                    {
                        yield return www;

                        if (www.error != null)
                        {
                            OnImageSaveFailed(imageInfo.imageName);
                            break;
                        }
                        else
                        {
                            Texture2D texture = www.texture;
                            uint imageId = FileStorage.server.Store(texture.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                            imageInfo.imageId = imageId.ToString();
                            UnityEngine.Object.DestroyImmediate(texture);
                        }
                    }
                }
            }

            static void OnImageSaveFailed(string imageName)
            {
                NotifyManager.PrintError(null, $"Image {imageName} was not found. Maybe you didn't upload it to the .../oxide/data/Images/ folder");
                Interface.Oxide.UnloadPlugin(ins.Name);
            }

            internal static void CreateGui(BasePlayer player, params string[] args)
            {
                CuiHelper.DestroyUi(player, "Tabs_Adem");
                CuiElementContainer container = new CuiElementContainer();
                float halfWidth = tabWidth / 2 + tabWidth / 2 * (iconImageInfos.Count - 1);

                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-halfWidth} {ins._config.guiConfig.offsetMinY}", OffsetMax = $"{halfWidth} {ins._config.guiConfig.offsetMinY + tabHeigth}" },
                    CursorEnabled = false,
                }, "Under", "Tabs_Adem");

                float xmin = 0;

                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    DrawTab(ref container, i, arg, xmin);
                    xmin += tabWidth;
                }

                CuiHelper.AddUi(player, container);
            }

            static void DrawTab(ref CuiElementContainer container, int index, string text, float xmin)
            {
                ImageInfo imageInfo = iconImageInfos[index];

                container.Add(new CuiElement
                {
                    Name = $"Tab_{index}_Adem",
                    Parent = "Tabs_Adem",
                    Components =
                    {
                        new CuiRawImageComponent { Png = tabImageInfo.imageId },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{xmin} 0", OffsetMax = $"{xmin + tabWidth} {tabHeigth}" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{index}_Adem",
                    Components =
                    {
                        new CuiRawImageComponent { Png = imageInfo.imageId },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "9 5", OffsetMax = "23 19" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{index}_Adem",
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = text, Align = TextAnchor.MiddleCenter, FontSize = 10, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "23 5", OffsetMax = $"{tabWidth - 9} 19" }
                    }
                });
            }

            internal static void DestroyAllGui()
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    if (player != null)
                        DestroyGui(player);
            }

            internal static void DestroyGui(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, "Tabs_Adem");
            }

            class ImageInfo
            {
                public string imageName;
                public string imageId;

                internal ImageInfo(string imageName)
                {
                    this.imageName = imageName;
                }
            }
        }

        static class EconomyManager
        {
            static readonly Dictionary<ulong, double> playersBalance = new Dictionary<ulong, double>();

            internal static void AddBalance(ulong playerId, double balance)
            {
                if (balance == 0 || playerId == 0)
                    return;

                if (playersBalance.ContainsKey(playerId))
                    playersBalance[playerId] += balance;
                else
                    playersBalance.Add(playerId, balance);
            }

            internal static void OnEventEnd()
            {
                DefineEventWinner();

                if (!ins._config.supportedPluginsConfig.economicsConfig.enable || playersBalance.Count == 0)
                {
                    playersBalance.Clear();
                    return;
                }

                SendBalanceToPlayers();
                playersBalance.Clear();
            }

            static void DefineEventWinner()
            {
                var winnerPair = playersBalance.Max(x => (float)x.Value);

                if (winnerPair.Value > 0)
                    Interface.CallHook($"On{ins.Name}EventWin", winnerPair.Key);

                if (winnerPair.Value >= ins._config.supportedPluginsConfig.economicsConfig.minCommandPoint)
                    foreach (string command in ins._config.supportedPluginsConfig.economicsConfig.commands)
                        ins.Server.Command(command.Replace("{steamid}", $"{winnerPair.Key}"));
            }

            static void SendBalanceToPlayers()
            {
                foreach (KeyValuePair<ulong, double> pair in playersBalance)
                    SendBalanceToPlayer(pair.Key, pair.Value);
            }

            static void SendBalanceToPlayer(ulong userID, double amount)
            {
                if (amount < ins._config.supportedPluginsConfig.economicsConfig.minEconomyPiont)
                    return;

                int intAmount = Convert.ToInt32(amount);

                if (intAmount <= 0)
                    return;

                if (ins._config.supportedPluginsConfig.economicsConfig.plugins.Contains("Economics") && ins.plugins.Exists("Economics"))
                    ins.Economics.Call("Deposit", userID.ToString(), amount);

                if (ins._config.supportedPluginsConfig.economicsConfig.plugins.Contains("Server Rewards") && ins.plugins.Exists("ServerRewards"))
                    ins.ServerRewards.Call("AddPoints", userID, intAmount);

                if (ins._config.supportedPluginsConfig.economicsConfig.plugins.Contains("IQEconomic") && ins.plugins.Exists("IQEconomic"))
                    ins.IQEconomic.Call("API_SET_BALANCE", userID, intAmount);

                BasePlayer player = BasePlayer.FindByID(userID);
                if (player != null)
                    NotifyManager.SendMessageToPlayer(player, "SendEconomy", ins._config.prefix, amount);
            }
        }
        #endregion Classes

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["EventActive_Exeption"] = "Ивент в данный момент активен, сначала завершите текущий ивент (<color=#ce3f27>/atrainstop</color>)!",
                ["ConfigurationNotFound_Exeption"] = "<color=#ce3f27>Не удалось</color> найти конфигурацию ивента!",
                ["PresetNotFound_Exeption"] = "Пресет {0} <color=#ce3f27>не найден</color> в конфиге!",
                ["NavMesh_Exeption"] = "Навигационная сетка не найдена!",

                ["SuccessfullyLaunched"] = "Ивент <color=#738d43>успешно</color> запущен!",
                ["PreStartTrain"] = "{0} <color=#738d43>{1}</color> появится через {2}!",
                ["StartTrain"] = "{0} <color=#738d43>{1}</color> был обнаружен в квадрате <color=#738d43>{2}</color>",
                ["PlayerStopTrain"] = "{0} <color=#ce3f27>{1}</color> остановил поезд!",
                ["RemainTime"] = "{0} Поезд будет уничтожен через <color=#ce3f27>{1}</color>!",
                ["EndEvent"] = "{0} Перевозка груза <color=#ce3f27>окончена</color>!",
                ["NeedStopTrain"] = "{0} Необходимо <color=#ce3f27>остановить</color> поезд!",
                ["NeedKillGuards"] = "{0} Необходимо <color=#ce3f27>уничтожить</color> охрану поезда!",
                ["EnterPVP"] = "{0} Вы <color=#ce3f27>вошли</color> в PVP зону, теперь другие игроки <color=#ce3f27>могут</color> наносить вам урон!",
                ["ExitPVP"] = "{0} Вы <color=#738d43>вышли</color> из PVP зоны, теперь другие игроки <color=#738d43>не могут</color> наносить вам урон!",
                ["DamageDistance"] = "{0} Подойдите <color=#ce3f27>ближе</color>!",
                ["Looted"] = "{0} <color=#738d43>{1}</color> был <color=#ce3f27>ограблен</color>!",

                ["SendEconomy"] = "{0} Вы <color=#738d43>получили</color> <color=#55aaff>{1}</color> баллов в экономику за прохождение ивента",

                ["Hours"] = "ч.",
                ["Minutes"] = "м.",
                ["Seconds"] = "с.",

                ["PveMode_BlockAction"] = "{0} Вы <color=#ce3f27>не можете</color> взаимодействовать с ивентом из-за кулдауна!",
                ["PveMode_YouAreNoOwner"] = "{0} Вы <color=#ce3f27>не являетесь</color> владельцем ивента!",
            }, this, "ru");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["EventActive_Exeption"] = "This event is active now. Finish the current event! (<color=#ce3f27>/atrainstop</color>)!",
                ["ConfigurationNotFound_Exeption"] = "The event configuration <color=#ce3f27>could not</color> be found!",
                ["PresetNotFound_Exeption"] = "{0} preset was <color=#ce3f27>not found</color> in the config!",
                ["EntitySpawn_Exeption"] = "Failed to spawn the entity (prefabName - {0})",
                ["LocomotiveSpawn_Exeption"] = "Failed to spawn the locomotive (presetName - {0})",
                ["FileNotFound_Exeption"] = "Data file not found or corrupted! ({0}.json)!",
                ["DataFileNotFound_Exeption"] = "Could not find a data file for customization ({0}.json). Empty the [Customization preset] in the config or upload the data file",
                ["RouteNotFound_Exeption"] = "The route could not be generated! Try to increase the minimum road length or change the route type!",
                ["NavMesh_Exeption"] = "The navigation grid was not found!",
                ["Rail_Exeption"] = "The rails could not be found! Try using custom spawn points",
                ["CustomSpawnPoint_Exeption"] = "Couldn't find a suitable custom spawn point!",

                ["SuccessfullyLaunched"] = "The event has been <color=#738d43>successfully</color> launched!",
                ["PreStartTrain"] = "{0} The <color=#738d43>{1}</color> will spawn in {2}!",
                ["StartTrain"] = "{0} <color=#738d43>{1}</color> is spawned at grid <color=#738d43>{2}</color>",
                ["PlayerStopTrain"] = "{0} <color=#ce3f27>{1}</color> stopped the train!",
                ["RemainTime"] = "{0} The train will be destroyed in <color=#ce3f27>{1}</color>!",
                ["EndEvent"] = "{0} The event is <color=#ce3f27>over</color>!",
                ["NeedStopTrain"] = "{0} You must <color=#ce3f27>stop</color> the train!",
                ["NeedKillGuards"] = "{0} You must <color=#ce3f27>kill</color> train guards!",
                ["EnterPVP"] = "{0} You <color=#ce3f27>have entered</color> the PVP zone, now other players <color=#ce3f27>can damage</color> you!",
                ["ExitPVP"] = "{0} You <color=#738d43>have gone out</color> the PVP zone, now other players <color=#738d43>can't damage</color> you!",
                ["DamageDistance"] = "{0} Come <color=#ce3f27>closer</color>!",
                ["Looted"] = "{0} <color=#738d43>{1}</color> has been <color=#ce3f27>looted</color>!",

                ["SendEconomy"] = "{0} You <color=#738d43>have earned</color> <color=#55aaff>{1}</color> points in economics for participating in the event",

                ["Hours"] = "h.",
                ["Minutes"] = "m.",
                ["Seconds"] = "s.",

                ["Marker_EventOwner"] = "Event Owner: {0}",

                ["EventStart_Log"] = "The event has begun! (Preset name - {0})",
                ["EventStop_Log"] = "The event is over!",

                ["PveMode_BlockAction"] = "{0} You <color=#ce3f27>can't interact</color> with the event because of the cooldown!",
                ["PveMode_YouAreNoOwner"] = "{0} You are not the <color=#ce3f27>owner</color> of the event!",
            }, this);
        }

        internal static string GetMessage(string langKey, string userID) => ins.lang.GetMessage(langKey, ins, userID);

        internal static string GetMessage(string langKey, string userID, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userID) : string.Format(GetMessage(langKey, userID), args);
        #endregion Lang

        #region Config  
        private PluginConfig _config;

        protected override void LoadDefaultConfig() => _config = PluginConfig.DefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            Config.WriteObject(_config, true);
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #region CustomizationConfig
        public class CustomizeProfile
        {
            [JsonProperty("Wagons presets")] public List<WagonCustomizationData> wagonPresets { get; set; }
            [JsonProperty("Npc presets")] public List<CustomizeNpcConfig> npcPresets { get; set; }
            [JsonProperty("Fireworks Presets")] public List<FireworkConfig> fireworkConfigs { get; set; }
        }

        public class WagonCustomizationData
        {
            [JsonProperty("Preset Name")] public string presetName { get; set; }
            [JsonProperty("Enable [true/false]")] public bool isEnabled { get; set; }
            [JsonProperty("Short prefab name of the wagon to which customization will be applied")] public string shortPrefabName { get; set; }
            [JsonProperty("Presets of wagons to which this preset will be applied (leave empty for all presets)")] public HashSet<string> wagonOnly { get; set; }
            [JsonProperty("Presets of wagons that will NOT be customized")] public HashSet<string> wagonExceptions { get; set; }
            [JsonProperty("Presets of trains that will NOT be customized")] public HashSet<string> trainExceptions { get; set; }
            [JsonProperty("Disable the basic decoration on the carriage [true/false]")] public bool isBaseDecorDisable { get; set; }
            [JsonProperty("List of decorations")] public HashSet<DecorEntityConfig> decorEntityConfigs { get; set; }
            [JsonProperty("List of signs")] public HashSet<PaintedSignConfig> signConfigs { get; set; }
        }

        public class DecorEntityConfig
        {
            [JsonProperty("Prefab")] public string prefabName { get; set; }
            [JsonProperty("Skin")] public ulong skin { get; set; }
            [JsonProperty("Position")] public string position { get; set; }
            [JsonProperty("Rotation")] public string rotation { get; set; }
        }

        public class PaintedSignConfig : DecorEntityConfig
        {
            [JsonProperty("Image Name")] public string imageName { get; set; }
        }

        public class CustomizeNpcConfig
        {
            [JsonProperty("Enable [true/false]")] public bool enable { get; set; }
            [JsonProperty("Wear Items")] public List<CustomWearItem> customWearItems { get; set; }
        }

        public class CustomWearItem
        {
            [JsonProperty("ShortName")] public string shortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong skinID { get; set; }
        }

        public class FireworkConfig
        {
            [JsonProperty("Preset Name")] public string presetName { get; set; }
            [JsonProperty("Enable [true/false]")] public bool isEnabled { get; set; }
            [JsonProperty("Color (r, g, b)")] public string color { get; set; }
            [JsonProperty("Coordinates for the drawing")] public HashSet<string> paintCoordinates { get; set; }
        }
        #endregion CustomizationConfig

        public class MainConfig
        {
            [JsonProperty(en ? "Enable automatic event holding [true/false]" : "Включить автоматическое проведение ивента [true/false]")] public bool isAutoEvent { get; set; }
            [JsonProperty(en ? "Minimum time between events [sec]" : "Минимальное вермя между ивентами [sec]")] public int minTimeBetweenEvents { get; set; }
            [JsonProperty(en ? "Maximum time between events [sec]" : "Максимальное вермя между ивентами [sec]")] public int maxTimeBetweenEvents { get; set; }
            [JsonProperty(en ? "The probability of holding an event underground [0 - 100]" : "Вероятность проведения ивента под землей [0 - 100]")] public float undergroundChance { get; set; }
            [JsonProperty(en ? "The train attacks first [true/false]" : "Поезд атакует первым [true/false]")] public bool isAggressive { get; set; }
            [JsonProperty(en ? "The time for which the train becomes aggressive after taking damage [sec]" : "Время, на которое поезд становится враждебным, после получения урона [sec]")] public int agressiveTime { get; set; }
            [JsonProperty(en ? "The crates can only be opened when the train is stopped [true/false]" : "Ящики можно открыть только на остановленном поезеде [true/false]")] public bool needStopTrain { get; set; }
            [JsonProperty(en ? "It is necessary to kill all the NPCs to loot the crates [true/false]" : "Необходимо убить всех NPC чтобы залутать ящики [true/false]")] public bool needKillNpc { get; set; }
            [JsonProperty(en ? "It is necessary to kill all the Bradleys to loot the crates [true/false]" : "Необходимо убить все Bradley чтобы залутать ящики [true/false]")] public bool needKillBradleys { get; set; }
            [JsonProperty(en ? "It is necessary to kill all the Turrets to loot the crates [true/false]" : "Необходимо убить все турели чтобы залутать ящики [true/false]")] public bool needKillTurrets { get; set; }
            [JsonProperty(en ? "It is necessary to kill the Heli to loot the crates [true/false]" : "Необходимо убить Вертолет чтобы залутать ящики [true/false]")] public bool needKillHeli { get; set; }
            [JsonProperty(en ? "Stop the train after taking damage [true/false]" : "Останавливать поезд после получения урона [true/false]")] public bool stopTrainAfterReceivingDamage { get; set; }
            [JsonProperty(en ? "Restore the stop time when the train receives damage/loot crates [true/false]" : "Восстанавливать время остановки при получении поездом урона/лутании ящиков [true/false]")] public bool isRestoreStopTimeAfterDamageOrLoot { get; set; }
            [JsonProperty(en ? "Destroy the train after opening all the crates [true/false]" : "Уничтожать поезд после открытия всех ящиков [true/false]")] public bool killTrainAfterLoot { get; set; }
            [JsonProperty(en ? "Time to destroy the train after opening all the crates [sec]" : "Время до уничтожения поезда после открытия всех ящиков [sec]")] public int endAfterLootTime { get; set; }
            [JsonProperty(en ? "Destroy wagons in front of the train [true/false]" : "Уничтожать вагоны перед поездом [true/false]")] public bool destrroyWagons { get; set; }
            [JsonProperty(en ? "Allow damage to the train driver [true/false]" : "Разрешить урон по водителю поезда [true/false]")] public bool allowDriverDamage { get; set; }
            [JsonProperty(en ? "To revive the train driver if he was killed? [true/false]" : "Возрождать водителя поезда, если он был убит [true/false]")] public bool reviveTrainDriver { get; set; }
            [JsonProperty(en ? "Enable logging of the start and end of the event? [true/false]" : "Включить логирование начала и окончания ивента? [true/false]")] public bool enableStartStopLogs { get; set; }
            [JsonProperty(en ? "The turrets of the train will drop loot after destruction? [true/false]" : "Турели поезда будут оставлять лут после уничтожения? [true/false]")] public bool isTurretDropWeapon { get; set; }
            [JsonProperty(en ? "Maximum range for damage to turrets/NPCs/mines (-1 - do not limit)" : "Максимальная дистанция для нанесения урона по турелям/нпс/минам (-1 - не ограничивать)")] public int maxGroundDamageDistance { get; set; }
            [JsonProperty(en ? "Maximum range for damage to heli (-1 - do not limit)" : "Максимальная дистанция для нанесения урона по вертолету (-1 - не ограничивать)")] public int maxHeliDamageDistance { get; set; }
            [JsonProperty(en ? "Allow players to attach wagons to the front of the train [true/false]" : "Разрешить игрокам присоединять вагоны спереди поезда [true/false]")] public bool enableFrontConnector { get; set; }
            [JsonProperty(en ? "Allow players to attach wagons to the back of the train [true/false]" : "Разрешить игрокам присоединять вагоны сзади поезда [true/false]")] public bool enableBackConnector { get; set; }
            [JsonProperty(en ? "Allow the player to resume the movement of the train using a emergency brake [true/false]" : "Разрешить игроку возобновлять движение поезда при помощи стоп крана [true/false]")] public bool allowEnableMovingByHandbrake { get; set; }
            [JsonProperty(en ? "The NPC will jump to the ground when the train stops (above ground)" : "НПС будет спрыгивать на землю при остановке поезда (на поверхности)")] public bool isNpcJumpOnSurface { get; set; }
            [JsonProperty(en ? "The NPC will jump to the ground when the train stops (underground)" : "НПС будет спрыгивать на землю при остановке поезда (в метро)")] public bool isNpcJumpInSubway { get; set; }
            [JsonProperty(en ? "The event will not end if there are players in the event zone [true/false]" : "Ивент не будет заканчиваться, если в зоне ивента есть игроки [true/false]")] public bool dontStopEventIfPlayerInZone { get; set; }
            [JsonProperty(en ? "Setting up custom spawn points" : "Настройка кастомных тоек спавна")] public CustomSpawnPointConfig customSpawnPointConfig { get; set; }
        }

        public class CustomSpawnPointConfig
        {
            [JsonProperty(en ? "Use custom spawn points [true/false]" : "Использовать кастомные точки спавна [true/false]")] public bool isEnabled { get; set; }
            [JsonProperty(en ? "Custom points for the spawn of the train (/atrainpoint)" : "Кастомные точки для спавна поезда (/atrainpoint)")] public HashSet<LocationConfig> points { get; set; }
        }

        public class CustomizationConfig
        {
            [JsonProperty(en ? "Customization preset (Empty - use standard wagons)" : "Пресет кастомизации (оставить пустым - использовать стандартые вагоны)")] public string profileName { get; set; }
            [JsonProperty(en ? "Turn on the electric furnaces (high impact on performance) [true/false]" : "Включить свечение электрических печей (высокое влияение на производительность) [true/false]")] public bool isElectricFurnacesEnable { get; set; }
            [JsonProperty(en ? "Turn on the boilers (medium impact on performance) [true/false]" : "Включить свечение котлов (среднее влияение на производительность) [true/false]")] public bool isBoilersEnable { get; set; }
            [JsonProperty(en ? "Turn on the fire (medium impact on performance) [true/false]" : "Включить огонь (среднее влияение на производительность) [true/false]")] public bool isFireEnable { get; set; }
            [JsonProperty(en ? "Turn on the lighting entities only at night [true/false]" : "Включать предметы освещения только ночью [true/false]")] public bool isLightOnlyAtNight { get; set; }
            [JsonProperty(en ? "Turn on the Neon Signs [true/false]" : "Включить неоновые таблички [true/false]")] public bool isNeonSignsEnable { get; set; }
            [JsonProperty(en ? "Setting up the Gift cannon" : "Настройка пушки подарков")] public GiftCannonSetting giftCannonSetting { get; set; }
            [JsonProperty(en ? "Setting up fireworks" : "Настройка фейрверков")] public FireworksSetting fireworksSettings { get; set; }
        }

        public class GiftCannonSetting
        {
            [JsonProperty(en ? "Enable throwing gifts out of the cannon [true/false]" : "Включить выбрасывание подарков из пушки [true/false]")] public bool isGiftCannonEnable { get; set; }
            [JsonProperty(en ? "Minimum time between throwing gifts [sec]" : "Минимальное время между выбрасываниями подарков [sec]")] public int minTimeBetweenItems { get; set; }
            [JsonProperty(en ? "Maximum time between throwing gifts [sec]" : "Максимальное время между выбрасываниями подарков [sec]")] public int maxTimeBetweenItems { get; set; }
            [JsonProperty(en ? "List of gifts" : "Список подарков")] public List<LootItemConfig> items { get; set; }
        }

        public class FireworksSetting
        {
            [JsonProperty(en ? "Turn on the fireworks [true/false]" : "Включить фейерверки [true/false]")] public bool isFireworksOn { get; set; }
            [JsonProperty(en ? "The time between fireworks salvos [s]" : "Время между залпами фейерверков [s]")] public int timeBetweenFireworks { get; set; }
            [JsonProperty(en ? "The number of shots in a salvo" : "Количество выстрелов в залпе")] public int numberShotsInSalvo { get; set; }
            [JsonProperty(en ? "Activate fireworks only at night [true/false]" : "Активировать фейерверки только ночью [true/false]")] public bool isNighFireworks { get; set; }
        }

        public class EventConfig
        {
            [JsonProperty(en ? "Preset Name" : "Название пресета")] public string presetName { get; set; }
            [JsonProperty(en ? "Train Name" : "Название поезда")] public string displayName { get; set; }
            [JsonProperty(en ? "Event time" : "Время ивента")] public int eventTime { get; set; }
            [JsonProperty(en ? "Allow automatic startup? [true/false]" : "Разрешить автоматический запуск? [true/false]")] public bool isAutoStart { get; set; }
            [JsonProperty(en ? "Probability of a preset [0.0-100.0]" : "Вероятность пресета [0.0-100.0]")] public float chance { get; set; }
            [JsonProperty(en ? "The minimum time after the server's wipe when this preset can be selected automatically [sec]" : "Минимальное время после вайпа сервера, когда этот пресет может быть выбран автоматически [sec]")] public int minTimeAfterWipe { get; set; }
            [JsonProperty(en ? "The maximum time after the server's wipe when this preset can be selected automatically [sec] (-1 - do not use this parameter)" : "Максимальное время после вайпа сервера, когда этот пресет может быть выбран автоматически [sec] (-1 - не использовать)")] public int maxTimeAfterWipe { get; set; }
            [JsonProperty(en ? "Radius of the event zone" : "Радиус зоны ивента")] public float zoneRadius { get; set; }
            [JsonProperty(en ? "Train can be spawned underground [true/false]" : "Поезд может появляться под землей [true/false]")] public bool isUndergroundTrain { get; set; }
            [JsonProperty(en ? "Train Stop time" : "Время на которое останавливается поезд")] public int stopTime { get; set; }
            [JsonProperty(en ? "Locomotive Preset" : "Пресет локомотива")] public string locomotivePreset { get; set; }
            [JsonProperty(en ? "Order of wagons" : "Порядок вагонов")] public List<string> wagonsPreset { get; set; }
            [JsonProperty(en ? "Heli preset" : "Пресет вертолета")] public string heliPreset { get; set; }
        }

        public class LocomotiveConfig : WagonConfig
        {
            [JsonProperty(en ? "Engine force" : "Мощность двигателя", Order = 8)] public float engineForce { get; set; }
            [JsonProperty(en ? "Max speed" : "Максимальная скорость", Order = 9)] public float maxSpeed { get; set; }
            [JsonProperty(en ? "Driver name" : "Имя водителя", Order = 10)] public string driverName { get; set; }
            [JsonProperty(en ? "Setting up the emergency brake" : "Настройка стоп-крана", Order = 11)] public EntitySpawnConfig handleBrakeConfig { get; set; }
            [JsonProperty(en ? "Setting up a timer that displays the event time" : "Настройка таймера времени ивента", Order = 12)] public EntitySpawnConfig eventTimerConfig { get; set; }
            [JsonProperty(en ? "Setting up a timer that displays the stop time" : "Настройка таймера времени остановки", Order = 12)] public EntitySpawnConfig stopTimerConfig { get; set; }
        }

        public class EntitySpawnConfig
        {
            [JsonProperty(en ? "Enable spawn? [true/false]" : "Включить спавн? [true/false]", Order = 8)] public bool isEnable { get; set; }
            [JsonProperty(en ? "Location" : "Расположение", Order = 8)] public LocationConfig location { get; set; }
        }

        public class WagonConfig
        {
            [JsonProperty(en ? "Preset name" : "Название пресета", Order = 0)] public string presetName { get; set; }
            [JsonProperty(en ? "Prefab name" : "Префаба", Order = 1)] public string prefabName { get; set; }
            [JsonProperty(en ? "Bradley preset - locations" : "Пресет бредли - расположения", Order = 2)] public Dictionary<string, HashSet<LocationConfig>> brradleys { get; set; }
            [JsonProperty(en ? "Turret preset - locations" : "Пресет турели - расположения", Order = 3)] public Dictionary<string, HashSet<LocationConfig>> turrets { get; set; }
            [JsonProperty(en ? "SamSite preset - locations" : "Пресет SamSite - расположения", Order = 4)] public Dictionary<string, HashSet<LocationConfig>> samsites { get; set; }
            [JsonProperty(en ? "Crate preset - locations" : "Пресет крейта - расположения", Order = 6)] public Dictionary<string, HashSet<LocationConfig>> crates { get; set; }
            [JsonProperty(en ? "NPC preset - locations" : "Пресет NPC - расположения", Order = 5)] public Dictionary<string, HashSet<LocationConfig>> npcs { get; set; }
            [JsonProperty(en ? "Decorative prefab - locations" : "Префаб декоративного блока - расположения", Order = 7)] public Dictionary<string, HashSet<LocationConfig>> decors { get; set; }
        }

        public class MarkerConfig
        {
            [JsonProperty(en ? "Do you use the Marker? [true/false]" : "Использовать ли маркер? [true/false]")] public bool enable { get; set; }
            [JsonProperty(en ? "Use a vending marker? [true/false]" : "Добавить маркер магазина? [true/false]")] public bool useShopMarker { get; set; }
            [JsonProperty(en ? "Use a circular marker? [true/false]" : "Добавить круговой маркер? [true/false]")] public bool useRingMarker { get; set; }
            [JsonProperty(en ? "Radius" : "Радиус")] public float radius { get; set; }
            [JsonProperty(en ? "Alpha" : "Прозрачность")] public float alpha { get; set; }
            [JsonProperty(en ? "Marker color" : "Цвет маркера")] public ColorConfig color1 { get; set; }
            [JsonProperty(en ? "Outline color" : "Цвет контура")] public ColorConfig color2 { get; set; }
        }

        public class ColorConfig
        {
            [JsonProperty("r")] public float r { get; set; }
            [JsonProperty("g")] public float g { get; set; }
            [JsonProperty("b")] public float b { get; set; }
        }

        public class BradleyConfig
        {
            [JsonProperty(en ? "Preset Name" : "Название пресета")] public string presetName { get; set; }
            [JsonProperty("HP")] public float hp { get; set; }
            [JsonProperty(en ? "Number of crates" : "Количество крейтов")] public int crateCount { get; set; }
            [JsonProperty(en ? "Scale damage" : "Множитель урона")] public float scaleDamage { get; set; }
            [JsonProperty(en ? "The viewing distance" : "Дальность обзора")] public float viewDistance { get; set; }
            [JsonProperty(en ? "Radius of search" : "Радиус поиска")] public float searchDistance { get; set; }
            [JsonProperty(en ? "The multiplier of Machine-gun aim cone" : "Множитель разброса пулемёта")] public float coaxAimCone { get; set; }
            [JsonProperty(en ? "The multiplier of Machine-gun fire rate" : "Множитель скорострельности пулемёта")] public float coaxFireRate { get; set; }
            [JsonProperty(en ? "Amount of Machine-gun burst shots" : "Кол-во выстрелов очереди пулемёта")] public int coaxBurstLength { get; set; }
            [JsonProperty(en ? "The time between shots of the main gun [sec.]" : "Время между залпами основного орудия [sec.]")] public float nextFireTime { get; set; }
            [JsonProperty(en ? "The time between shots of the main gun in a fire rate [sec.]" : "Время между выстрелами основного орудия в залпе [sec.]")] public float topTurretFireRate { get; set; }
        }

        public class TurretConfig
        {
            [JsonProperty(en ? "Preset Name" : "Название пресета")] public string presetName { get; set; }
            [JsonProperty(en ? "Health" : "Кол-во ХП")] public float hp { get; set; }
            [JsonProperty(en ? "Weapon ShortName" : "ShortName оружия")] public string shortNameWeapon { get; set; }
            [JsonProperty(en ? "Ammo ShortName" : "ShortName патронов")] public string shortNameAmmo { get; set; }
            [JsonProperty(en ? "Number of ammo" : "Кол-во патронов")] public int countAmmo { get; set; }
            [JsonProperty(en ? "Target detection range (0 - do not change)" : "Дальность обнаружения цели (0 - не изменять)")] public float targetDetectionRange { get; set; }
            [JsonProperty(en ? "Target loss range (0 - do not change)" : "Дальность потери цели (0 - не изменять)")] public float targetLossRange { get; set; }
        }

        public class SamSiteConfig
        {
            [JsonProperty(en ? "Preset Name" : "Название пресета")] public string presetName { get; set; }
            [JsonProperty(en ? "Health" : "Кол-во ХП")] public float hp { get; set; }
            [JsonProperty(en ? "Number of ammo" : "Количество патронов")] public int countAmmo { get; set; }
        }

        public class HeliConfig
        {
            [JsonProperty(en ? "Name" : "Название пресета")] public string presetName { get; set; }
            [JsonProperty("HP")] public float hp { get; set; }
            [JsonProperty(en ? "HP of the main rotor" : "HP главного винта")] public float mainRotorHealth { get; set; }
            [JsonProperty(en ? "HP of tail rotor" : "HP хвостового винта")] public float rearRotorHealth { get; set; }
            [JsonProperty(en ? "Flying height" : "Высота полета")] public float height { get; set; }
            [JsonProperty(en ? "Bullet speed" : "Скорость пуль")] public float bulletSpeed { get; set; }
            [JsonProperty(en ? "Bullet Damage" : "Урон пуль")] public float bulletDamage { get; set; }
            [JsonProperty(en ? "The distance to which the helicopter can move away from the convoy" : "Дистанция, на которую вертолет может отдаляться от конвоя")] public float distance { get; set; }
            [JsonProperty(en ? "Speed" : "Скорость")] public float speed { get; set; }
            [JsonProperty(en ? "The time for which the helicopter can leave the train to attack the target [sec.]" : "Время, на которое верталет может покидать поезд для атаки цели [sec.]")] public float outsideTime { get; set; }
            [JsonProperty(en ? "Numbers of crates" : "Количество ящиков")] public int cratesAmount { get; set; }
            [JsonProperty(en ? "The helicopter will not aim for the nearest monument at death [true/false]" : "Вертолет не будет стремиться к ближайшему монументу при смерти [true/false]")] public bool immediatelyKill { get; set; }
            [JsonProperty(en ? "Open the crates immediately after spawn" : "Открывать ящики сразу после спавна")] public bool instCrateOpen { get; set; }
            [JsonProperty(en ? "Lifetime of crates [sec]" : "Время жизни крейтов [sec]")] public float cratesLifeTime { get; set; }
            [JsonProperty(en ? "Own loot table" : "Собственная таблица предметов")] public BaseLootTableConfig baseLootTableConfig { get; set; }
        }

        public class NpcConfig
        {
            [JsonProperty(en ? "Preset Name" : "Название пресета")] public string presetName { get; set; }
            [JsonProperty("Name")] public string displayName { get; set; }
            [JsonProperty(en ? "Health" : "Кол-во ХП")] public float health { get; set; }
            [JsonProperty(en ? "Wear items" : "Одежда")] public List<NpcWear> wearItems { get; set; }
            [JsonProperty(en ? "Belt items" : "Быстрые слоты")] public List<NpcBelt> beltItems { get; set; }
            [JsonProperty(en ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float attackRangeMultiplier { get; set; }
            [JsonProperty(en ? "Speed" : "Скорость")] public float speed { get; set; }
            [JsonProperty(en ? "Roam Range" : "Дальность патрулирования местности")] public float roamRange { get; set; }
            [JsonProperty(en ? "Chase Range" : "Дальность погони за целью")] public float chaseRange { get; set; }
            [JsonProperty(en ? "Sense Range" : "Радиус обнаружения цели")] public float senseRange { get; set; }
            [JsonProperty(en ? "Memory duration [sec.]" : "Длительность памяти цели [sec.]")] public float memoryDuration { get; set; }
            [JsonProperty(en ? "Scale damage" : "Множитель урона")] public float damageScale { get; set; }
            [JsonProperty(en ? "Turret damage scale" : "Множитель урона от турелей")] public float turretDamageScale { get; set; }
            [JsonProperty(en ? "Aim Cone Scale" : "Множитель разброса")] public float aimConeScale { get; set; }
            [JsonProperty(en ? "Detect the target only in the NPC's viewing vision cone?" : "Обнаруживать цель только в углу обзора NPC? [true/false]")] public bool checkVisionCone { get; set; }
            [JsonProperty(en ? "Vision Cone" : "Угол обзора")] public float visionCone { get; set; }
            [JsonProperty(en ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")] public bool disableRadio { get; set; }
            [JsonProperty("Kit")] public string kit { get; set; }
            [JsonProperty(en ? "Should remove the corpse?" : "Удалять труп?")] public bool deleteCorpse { get; set; }
            [JsonProperty(en ? "Own loot table" : "Собственная таблица лута")] public LootTableConfig lootTableConfig { get; set; }
        }

        public class NpcWear
        {
            [JsonProperty(en ? "ShortName" : "ShortName")] public string shortName { get; set; }
            [JsonProperty(en ? "skinID (0 - default)" : "SkinID (0 - default)")] public ulong skinID { get; set; }
        }

        public class NpcBelt
        {
            [JsonProperty(en ? "ShortName" : "ShortName")] public string shortName { get; set; }
            [JsonProperty(en ? "Amount" : "Кол-во")] public int amount { get; set; }
            [JsonProperty(en ? "skinID (0 - default)" : "SkinID (0 - default)")] public ulong skinID { get; set; }
            [JsonProperty(en ? "Mods" : "Модификации на оружие")] public HashSet<string> mods { get; set; }
            [JsonProperty(en ? "Ammo" : "Патроны")] public string ammo { get; set; }
        }

        public class CrateConfig
        {
            [JsonProperty(en ? "Preset Name" : "Название пресета")] public string presetName { get; set; }
            [JsonProperty("Prefab")] public string prefab { get; set; }
            [JsonProperty("Skin")] public ulong skin { get; set; }
            [JsonProperty(en ? "Time to unlock the crates (LockedCrate) [sec.]" : "Время до открытия заблокированного ящика (LockedCrate) [sec.]")] public float hackTime { get; set; }
            [JsonProperty(en ? "Own loot table" : "Собственная таблица предметов")] public LootTableConfig lootTableConfig { get; set; }
        }

        public class LootTableConfig : BaseLootTableConfig
        {
            [JsonProperty(en ? "Allow the AlphaLoot plugin to spawn items in this crate" : "Разрешить плагину AlphaLoot спавнить предметы в этом ящике")] public bool isAlphaLoot { get; set; }
            [JsonProperty(en ? "The name of the loot preset for AlphaLoot" : "Название пресета лута AlphaLoot")] public string alphaLootPresetName { get; set; }
            [JsonProperty(en ? "Allow the CustomLoot plugin to spawn items in this crate" : "Разрешить плагину CustomLoot спавнить предметы в этом ящике")] public bool isCustomLoot { get; set; }
            [JsonProperty(en ? "Allow the Loot Table Stacksize GUI plugin to spawn items in this crate" : "Разрешить плагину Loot Table Stacksize GUI спавнить предметы в этом ящике")] public bool isLootTablePLugin { get; set; }
        }

        public class BaseLootTableConfig
        {
            [JsonProperty(en ? "Clear the standard content of the crate" : "Отчистить стандартное содержимое крейта")] public bool clearDefaultItemList { get; set; }
            [JsonProperty(en ? "Setting up loot from the loot table" : "Настройка лута из лутовой таблицы")] public PrefabLootTableConfigs prefabConfigs { get; set; }
            [JsonProperty(en ? "Enable spawn of items from the list" : "Включить спавн предметов из списка")] public bool isRandomItemsEnable { get; set; }
            [JsonProperty(en ? "Minimum numbers of items" : "Минимальное кол-во элементов")] public int minItemsAmount { get; set; }
            [JsonProperty(en ? "Maximum numbers of items" : "Максимальное кол-во элементов")] public int maxItemsAmount { get; set; }
            [JsonProperty(en ? "List of items" : "Список предметов")] public List<LootItemConfig> items { get; set; }
        }

        public class PrefabLootTableConfigs
        {
            [JsonProperty(en ? "Enable spawn loot from prefabs" : "Включить спавн лута из префабов")] public bool isEnable { get; set; }
            [JsonProperty(en ? "List of prefabs (one is randomly selected)" : "Список префабов (выбирается один рандомно)")] public List<PrefabConfig> prefabs { get; set; }
        }

        public class PrefabConfig
        {
            [JsonProperty(en ? "Prefab displayName" : "Название префаба")] public string prefabName { get; set; }
            [JsonProperty(en ? "Minimum Loot multiplier" : "Минимальный множитель лута")] public int minLootScale { get; set; }
            [JsonProperty(en ? "Maximum Loot multiplier" : "Максимальный множитель лута")] public int maxLootScale { get; set; }
        }

        public class LootItemConfig
        {
            [JsonProperty("ShortName")] public string shortname { get; set; }
            [JsonProperty(en ? "Minimum" : "Минимальное кол-во")] public int minAmount { get; set; }
            [JsonProperty(en ? "Maximum" : "Максимальное кол-во")] public int maxAmount { get; set; }
            [JsonProperty(en ? "Chance [0.0-100.0]" : "Шанс выпадения предмета [0.0-100.0]")] public float chance { get; set; }
            [JsonProperty(en ? "Is this a blueprint? [true/false]" : "Это чертеж? [true/false]")] public bool isBlueprint { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong skin { get; set; }
            [JsonProperty(en ? "Name (empty - default)" : "Название (empty - default)")] public string name { get; set; }
            [JsonProperty(en ? "List of genomes" : "Список геномов")] public List<string> genomes { get; set; }
        }

        public class LocationConfig
        {
            [JsonProperty(en ? "Position" : "Позиция")] public string position { get; set; }
            [JsonProperty(en ? "Rotation" : "Вращение")] public string rotation { get; set; }
        }

        public class ZoneConfig
        {
            [JsonProperty(en ? "Create a PVP zone in the convoy isStop zone? (only for those who use the TruePVE plugin)[true/false]" : "Создавать зону PVP в зоне проведения ивента? (только для тех, кто использует плагин TruePVE) [true/false]")] public bool isPVPZone { get; set; }
            [JsonProperty(en ? "Use the dome? [true/false]" : "Использовать ли купол? [true/false]")] public bool isDome { get; set; }
            [JsonProperty(en ? "Darkening the dome" : "Затемнение купола")] public int darkening { get; set; }
            [JsonProperty(en ? "Use a colored border? [true/false]" : "Использовать цветную границу? [true/false]")] public bool isColoredBorder { get; set; }
            [JsonProperty(en ? "Border color (0 - blue, 1 - green, 2 - purple, 3 - red)" : "Цвет границы (0 - синий, 1 - зеленый, 2 - фиолетовый, 3 - красный)")] public int borderColor { get; set; }
            [JsonProperty(en ? "Brightness of the color border" : "Яркость цветной границы")] public int brightness { get; set; }
            [JsonProperty(en ? "Radius" : "Радиус")] public float radius { get; set; }
        }

        public class GUIConfig
        {
            [JsonProperty(en ? "Use the Countdown GUI? [true/false]" : "Использовать ли GUI обратного отсчета? [true/false]")] public bool isEnable { get; set; }
            [JsonProperty(en ? "Vertical offset" : "Смещение по вертикали")] public int offsetMinY { get; set; }
        }

        public class NotifyConfig
        {
            [JsonProperty(en ? "The time from the notification to the start of the event [sec]" : "Время от оповещения до начала ивента [sec]")] public int preStartTime { get; set; }
            [JsonProperty(en ? "Use a chat? [true/false]" : "Использовать ли чат? [true/false]")] public bool isChatEnable { get; set; }
            [JsonProperty(en ? "The time until the end of the event, when a message is displayed about the time until the end of the event [sec]" : "Время до конца ивента, когда выводится сообщение о сокром окончании ивента [sec]")] public HashSet<int> timeNotifications { get; set; }
            [JsonProperty(en ? "Facepunch Game Tips setting" : "Настройка сообщений Facepunch Game Tip")] public GameTipConfig gameTipConfig { get; set; }
        }

        public class GameTipConfig
        {
            [JsonProperty(en ? "Use Facepunch Game Tips (notification bar above hotbar)? [true/false]" : "Использовать ли Facepunch Game Tip (оповещения над слотами быстрого доступа игрока)? [true/false]")] public bool isEnabled { get; set; }
            [JsonProperty(en ? "Style (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)" : "Стиль (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)")] public int style { get; set; }
        }

        public class PveModeConfig
        {
            [JsonProperty(en ? "Use the PVE mode of the plugin? [true/false]" : "Использовать PVE режим работы плагина? [true/false]")] public bool enable { get; set; }
            [JsonProperty(en ? "Allow administrators to loot crates and cause damage? [true/false]" : "Разрешить администраторам лутать ящики и наносить урон? [true/false]")] public bool ignoreAdmin { get; set; }
            [JsonProperty(en ? "The owner of the event will be the one who stopped the event? [true/false]" : "Владельцем ивента будет становиться тот кто остановил ивент? [true/false]")] public bool ownerIsStopper { get; set; }
            [JsonProperty(en ? "If a player has a cooldown and the event has NO OWNERS, then he will not be able to interact with the event? [true/false]" : "Если у игрока кулдаун, а у ивента НЕТ ВЛАДЕЛЬЦЕВ, то он не сможет взаимодействовать с ивентом? [true/false]")] public bool noInterractIfCooldownAndNoOwners { get; set; }
            [JsonProperty(en ? "If a player has a cooldown, and the event HAS AN OWNER, then he will not be able to interact with the event, even if he is on a team with the owner? [true/false]" : "Если у игрока кулдаун, а у ивента ЕСТЬ ВЛАДЕЛЕЦ, то он не сможет взаимодействовать с ивентом, даже если находится в команде с владельцем? [true/false]")] public bool noDealDamageIfCooldownAndTeamOwner { get; set; }
            [JsonProperty(en ? "Allow only the owner or his teammates to loot crates? [true/false]" : "Разрешить лутать ящики только владельцу или его тиммейтам? [true/false]")] public bool canLootOnlyOwner { get; set; }
            [JsonProperty(en ? "Show the displayName of the event owner on a marker on the map? [true/false]" : "Отображать имя владелца ивента на маркере на карте? [true/false]")] public bool showEventOwnerNameOnMap { get; set; }
            [JsonProperty(en ? "The amount of damage that the player has to do to become the Event Owner" : "Кол-во урона, которое должен нанести игрок, чтобы стать владельцем ивента")] public float damage { get; set; }
            [JsonProperty(en ? "Damage coefficients for calculate to become the Event Owner." : "Коэффициенты урона для подсчета, чтобы стать владельцем события.")] public Dictionary<string, float> scaleDamage { get; set; }
            [JsonProperty(en ? "Can the non-owner of the event loot the crates? [true/false]" : "Может ли не владелец ивента грабить ящики? [true/false]")] public bool lootCrate { get; set; }
            [JsonProperty(en ? "Can the non-owner of the event hack locked crates? [true/false]" : "Может ли не владелец ивента взламывать заблокированные ящики? [true/false]")] public bool hackCrate { get; set; }
            [JsonProperty(en ? "Can the non-owner of the event loot NPC corpses? [true/false]" : "Может ли не владелец ивента грабить трупы NPC? [true/false]")] public bool lootNpc { get; set; }
            [JsonProperty(en ? "Can an Npc attack a non-owner of the event? [true/false]" : "Может ли Npc атаковать не владельца ивента? [true/false]")] public bool targetNpc { get; set; }
            [JsonProperty(en ? "Can Bradley attack a non-owner of the event? [true/false]" : "Может ли Bradley атаковать не владельца ивента? [true/false]")] public bool targetTank { get; set; }
            [JsonProperty(en ? "Can Helicopter attack a non-owner of the event? [true/false]" : "Может ли Вертолет атаковать не владельца ивента? [true/false]")] public bool targetHeli { get; set; }
            [JsonProperty(en ? "Can Turret attack a non-owner of the event? [true/false]" : "Может ли Турель атаковать не владельца ивента? [true/false]")] public bool targetTurret { get; set; }
            [JsonProperty(en ? "Can the non-owner of the event deal damage to the NPC? [true/false]" : "Может ли не владелец ивента наносить урон по NPC? [true/false]")] public bool damageNpc { get; set; }
            [JsonProperty(en ? "Can the non-owner of the event do damage to Helicopter? [true/false]" : "Может ли не владелец ивента наносить урон по Вертолету? [true/false]")] public bool damageHeli { get; set; }
            [JsonProperty(en ? "Can the non-owner of the event do damage to Bradley? [true/false]" : "Может ли не владелец ивента наносить урон по Bradley? [true/false]")] public bool damageTank { get; set; }
            [JsonProperty(en ? "Can the non-owner of the event do damage to Turret? [true/false]" : "Может ли не владелец ивента наносить урон по Турелям? [true/false]")] public bool damageTurret { get; set; }
            [JsonProperty(en ? "Allow the non-owner of the event to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента не владельцу ивента? [true/false]")] public bool canEnter { get; set; }
            [JsonProperty(en ? "Allow a player who has an active cooldown of the Event Owner to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента игроку, у которого активен кулдаун на получение статуса владельца ивента? [true/false]")] public bool canEnterCooldownPlayer { get; set; }
            [JsonProperty(en ? "The time that the Event Owner may not be inside the event zone [sec.]" : "Время, которое владелец ивента может не находиться внутри зоны ивента [сек.]")] public int timeExitOwner { get; set; }
            [JsonProperty(en ? "The time until the end of Event Owner status when it is necessary to warn the player [sec.]" : "Время таймера до окончания действия статуса владельца ивента, когда необходимо предупредить игрока [сек.]")] public int alertTime { get; set; }
            [JsonProperty(en ? "Prevent the actions of the RestoreUponDeath plugin in the event zone? [true/false]" : "Запрещать работу плагина RestoreUponDeath в зоне действия ивента? [true/false]")] public bool restoreUponDeath { get; set; }
            [JsonProperty(en ? "The time that the player can`t become the Event Owner, after the end of the event and the player was its owner [sec.]" : "Время, которое игрок не сможет стать владельцем ивента, после того как ивент окончен и игрок был его владельцем [sec.]")] public double cooldown { get; set; }
            [JsonProperty(en ? "Darkening the dome (0 - disables the dome)" : "Затемнение купола (0 - отключает купол)")] public int darkening { get; set; }
        }

        public class ScaleDamageConfig
        {
            [JsonProperty(en ? "Type of target" : "Тип цели")] public string Type { get; set; }
            [JsonProperty(en ? "Damage Multiplier" : "Множитель урона")] public float Scale { get; set; }
        }

        public class EconomyConfig
        {
            [JsonProperty(en ? "Enable economy" : "Включить экономику?")] public bool enable { get; set; }
            [JsonProperty(en ? "Which economy plugins do you want to use? (Economics, Server Rewards, IQEconomic)" : "Какие плагины экономики вы хотите использовать? (Economics, Server Rewards, IQEconomic)")] public HashSet<string> plugins { get; set; }
            [JsonProperty(en ? "The minimum value that a player must collect to get points for the economy" : "Минимальное значение, которое игрок должен заработать, чтобы получить баллы за экономику")] public double minEconomyPiont { get; set; }
            [JsonProperty(en ? "The minimum value that a winner must collect to make the commands work" : "Минимальное значение, которое победитель должен заработать, чтобы сработали команды")] public double minCommandPoint { get; set; }
            [JsonProperty(en ? "Looting of crates" : "Ограбление ящиков")] public Dictionary<string, double> crates { get; set; }
            [JsonProperty(en ? "Killing an NPC" : "Убийство NPC")] public double npcPoint { get; set; }
            [JsonProperty(en ? "Killing an Bradley" : "Уничтожение Bradley")] public double bradleyPoint { get; set; }
            [JsonProperty(en ? "Killing an Turret" : "Уничтожение Турели")] public double turretPoint { get; set; }
            [JsonProperty(en ? "Killing an Heli" : "Уничтожение Вертолета")] public double heliPoint { get; set; }
            [JsonProperty(en ? "Hacking a locked crate" : "Взлом заблокированного ящика")] public double hackCratePoint { get; set; }
            [JsonProperty(en ? "List of commands that are executed in the console at the end of the event ({steamid} - the player who collected the highest number of points)" : "Список команд, которые выполняются в консоли по окончанию ивента ({steamid} - игрок, который набрал наибольшее кол-во баллов)")] public HashSet<string> commands { get; set; }
        }

        public class GUIAnnouncementsConfig
        {
            [JsonProperty(en ? "Do you use the GUI Announcements? [true/false]" : "Использовать ли GUI Announcements? [true/false]")] public bool isEnabled { get; set; }
            [JsonProperty(en ? "Banner color" : "Цвет баннера")] public string bannerColor { get; set; }
            [JsonProperty(en ? "Text color" : "Цвет текста")] public string textColor { get; set; }
            [JsonProperty(en ? "Adjust Vertical Position" : "Отступ от верхнего края")] public float apiAdjustVPosition { get; set; }
        }

        public class NotifyPluginConfig
        {
            [JsonProperty(en ? "Do you use the Notify? [true/false]" : "Использовать ли Notify? [true/false]")] public bool isEnabled { get; set; }
            [JsonProperty(en ? "Type" : "Тип")] public int type { get; set; }
        }

        public class DiscordConfig
        {
            [JsonProperty(en ? "Do you use the Discord? [true/false]" : "Использовать ли Discord? [true/false]")] public bool isEnabled { get; set; }
            [JsonProperty("Webhook URL")] public string webhookUrl { get; set; }
            [JsonProperty(en ? "Embed Color (DECIMAL)" : "Цвет полосы (DECIMAL)")] public int embedColor { get; set; }
            [JsonProperty(en ? "Keys of required messages" : "Ключи необходимых сообщений")] public HashSet<string> keys { get; set; }
        }

        public class SupportedPluginsConfig
        {
            [JsonProperty(en ? "PVE Mode Setting" : "Настройка PVE Mode")] public PveModeConfig pveMode { get; set; }
            [JsonProperty(en ? "Economy Setting" : "Настройка экономики")] public EconomyConfig economicsConfig { get; set; }
            [JsonProperty(en ? "GUI Announcements setting" : "Настройка GUI Announcements")] public GUIAnnouncementsConfig guiAnnouncementsConfig { get; set; }
            [JsonProperty(en ? "Notify setting" : "Настройка Notify")] public NotifyPluginConfig notifyPluginConfig { get; set; }
            [JsonProperty(en ? "DiscordMessages setting" : "Настройка DiscordMessages")] public DiscordConfig discordMessagesConfig { get; set; }
            [JsonProperty(en ? "BetterNpc setting" : "Настройка BetterNpc")] public BetterNpcConfig betterNpcConfig { get; set; }
        }

        public class BetterNpcConfig
        {
            [JsonProperty(en ? "Allow Npc spawn after destroying Heli" : "Разрешить спавн Npc после уничтожения Вертолета")] public bool isHeliNpc { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(en ? "Version" : "Версия")] public VersionNumber versionConfig { get; set; }
            [JsonProperty(en ? "Prefix of chat messages" : "Префикс в чате")] public string prefix { get; set; }
            [JsonProperty(en ? "Main Setting" : "Основные настройки")] public MainConfig mainConfig { get; set; }
            [JsonProperty(en ? "Customization Settings" : "Настройки кастомизации")] public CustomizationConfig customizationConfig { get; set; }
            [JsonProperty(en ? "Train presets" : "Пресеты поездов")] public HashSet<EventConfig> eventConfigs { get; set; }
            [JsonProperty(en ? "Locomotive presets" : "Пресеты локомотивов")] public HashSet<LocomotiveConfig> locomotiveConfigs { get; set; }
            [JsonProperty(en ? "Wagon presets" : "Пресеты вагонов")] public HashSet<WagonConfig> wagonConfigs { get; set; }
            [JsonProperty(en ? "Bradley presets" : "Пресеты бредли")] public HashSet<BradleyConfig> bradleysConfigs { get; set; }
            [JsonProperty(en ? "Turrets presets" : "Пресеты турелей")] public HashSet<TurretConfig> turretConfigs { get; set; }
            [JsonProperty(en ? "Samsite presets" : "Пресеты Samsite")] public HashSet<SamSiteConfig> samsiteConfigs { get; set; }
            [JsonProperty(en ? "Crate presets" : "Пресеты ящиков")] public HashSet<CrateConfig> crateConfigs { get; set; }
            [JsonProperty(en ? "Heli presets" : "Пресеты вертолетов")] public HashSet<HeliConfig> heliConfigs { get; set; }
            [JsonProperty(en ? "NPC presets" : "Пресеты NPC")] public HashSet<NpcConfig> npcConfigs { get; set; }
            [JsonProperty(en ? "Marker Setting" : "Настройки маркера")] public MarkerConfig markerConfig { get; set; }
            [JsonProperty(en ? "Zone Setting" : "Настройки зоны ивента")] public ZoneConfig zoneConfig { get; set; }
            [JsonProperty(en ? "GUI Setting" : "Настройки GUI")] public GUIConfig guiConfig { get; set; }
            [JsonProperty(en ? "Notification Settings" : "Настройки уведомлений")] public NotifyConfig notifyConfig { get; set; }
            [JsonProperty(en ? "Supported Plugins" : "Поддерживаемые плагины")] public SupportedPluginsConfig supportedPluginsConfig { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig()
                {
                    versionConfig = new VersionNumber(1, 8, 1),
                    prefix = "[ArmoredTrain]",
                    mainConfig = new MainConfig
                    {
                        isAutoEvent = true,
                        minTimeBetweenEvents = 7200,
                        maxTimeBetweenEvents = 7200,
                        undergroundChance = 0,
                        isAggressive = false,
                        agressiveTime = 300,
                        needStopTrain = false,
                        needKillNpc = true,
                        needKillBradleys = false,
                        needKillTurrets = false,
                        needKillHeli = false,
                        stopTrainAfterReceivingDamage = false,
                        isRestoreStopTimeAfterDamageOrLoot = true,
                        killTrainAfterLoot = true,
                        endAfterLootTime = 300,
                        destrroyWagons = false,
                        allowDriverDamage = true,
                        reviveTrainDriver = true,
                        enableStartStopLogs = false,
                        isTurretDropWeapon = false,
                        maxGroundDamageDistance = 100,
                        maxHeliDamageDistance = 250,
                        enableFrontConnector = false,
                        enableBackConnector = true,
                        allowEnableMovingByHandbrake = true,
                        isNpcJumpOnSurface = true,
                        isNpcJumpInSubway = true,
                        dontStopEventIfPlayerInZone = false,
                        customSpawnPointConfig = new CustomSpawnPointConfig
                        {
                            isEnabled = false,
                            points = new HashSet<LocationConfig>(),
                        }
                    },
                    customizationConfig = new CustomizationConfig
                    {
                        profileName = "",
                        isElectricFurnacesEnable = false,
                        isBoilersEnable = true,
                        isFireEnable = true,
                        isNeonSignsEnable = true,
                        isLightOnlyAtNight = true,

                        giftCannonSetting = new GiftCannonSetting
                        {
                            isGiftCannonEnable = false,
                            minTimeBetweenItems = 1,
                            maxTimeBetweenItems = 60,
                            items = new List<LootItemConfig>
                            {
                                new LootItemConfig
                                {
                                    shortname = "xmas.present.small",
                                    minAmount = 1,
                                    maxAmount = 1,
                                    chance = 80,
                                    isBlueprint = false,
                                    skin = 0,
                                    name = "",
                                    genomes = new List<string>()
                                },
                                new LootItemConfig
                                {
                                    shortname = "xmas.present.medium",
                                    minAmount = 1,
                                    maxAmount = 1,
                                    chance = 15,
                                    isBlueprint = false,
                                    skin = 0,
                                    name = "",
                                    genomes = new List<string>()
                                },
                                new LootItemConfig
                                {
                                    shortname = "xmas.present.large",
                                    minAmount = 1,
                                    maxAmount = 1,
                                    chance = 5,
                                    isBlueprint = false,
                                    skin = 0,
                                    name = "",
                                    genomes = new List<string>()
                                }
                            }
                        },
                        fireworksSettings = new FireworksSetting
                        {
                            isFireworksOn = true,
                            timeBetweenFireworks = 600,
                            numberShotsInSalvo = 5,
                            isNighFireworks = true,
                        }
                    },
                    eventConfigs = new HashSet<EventConfig>
                    {
                        new EventConfig
                        {
                            presetName = "train_easy",
                            displayName = en ? "Small Train" : "Небольшой поезд",
                            isAutoStart = true,
                            chance = 40,
                            minTimeAfterWipe = 0,
                            maxTimeAfterWipe = 172800,
                            zoneRadius = 100,
                            isUndergroundTrain = true,
                            eventTime = 3600,
                            stopTime = 300,
                            locomotivePreset = "locomotive_default",
                            wagonsPreset = new List<string>
                            {
                                "wagon_crate_1"
                            },
                            heliPreset = ""
                        },
                        new EventConfig
                        {
                            presetName = "train_normal",
                            displayName = en ? "Train" : "Поезд",
                            isAutoStart = true,
                            chance = 40,
                            minTimeAfterWipe = 10800,
                            maxTimeAfterWipe = -1,
                            zoneRadius = 100,
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 300,
                            locomotivePreset = "locomotive_turret",
                            wagonsPreset = new List<string>
                            {
                                "wagon_bradley",
                                "wagon_crate_1",
                                "wagon_samsite"
                            },
                            heliPreset = ""
                        },
                        new EventConfig
                        {
                            presetName = "train_hard",
                            displayName = en ? "Giant Train" : "Огромный поезд",
                            isAutoStart = true,
                            chance = 20,
                            minTimeAfterWipe = 36000,
                            maxTimeAfterWipe = -1,
                            zoneRadius = 100,
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 300,
                            locomotivePreset = "locomotive_new",
                            wagonsPreset = new List<string>
                            {
                                "wagon_bradley",
                                "wagon_crate_2",
                                "wagon_bradley",
                                "wagon_samsite"
                            },
                            heliPreset = "heli_1"
                        },
                        new EventConfig
                        {
                            presetName = "train_caboose",
                            displayName = "Caboose",
                            isAutoStart = false,
                            chance = 0,
                            minTimeAfterWipe = 0,
                            maxTimeAfterWipe = -1,
                            zoneRadius = 100,
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 0,
                            locomotivePreset = "locomotive_default",
                            wagonsPreset = new List<string>
                            {
                                "caboose_wagon"
                            },
                            heliPreset = ""
                        },
                        new EventConfig
                        {
                            presetName = "train_halloween",
                            displayName = en ? "Halloween Train" : "Хэллоуинский Поезд",
                            isAutoStart = false,
                            chance = 0,
                            minTimeAfterWipe = 0,
                            maxTimeAfterWipe = -1,
                            zoneRadius = 100,
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 300,
                            locomotivePreset = "locomotive_new",
                            wagonsPreset = new List<string>
                            {
                                "wagon_crate_1",
                                "halloween_wagon",
                                "wagon_samsite"

                            },
                            heliPreset = ""
                        },
                        new EventConfig
                        {
                            presetName = "train_xmas_easy",
                            displayName = en ? "Small Christmas train" : "Маленький Новогодний Поезд",
                            isAutoStart = false,
                            chance = 0,
                            minTimeAfterWipe = 0,
                            maxTimeAfterWipe = -1,
                            zoneRadius = 100,
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 300,
                            locomotivePreset = "locomotive_default",
                            wagonsPreset = new List<string>
                            {
                                "xmas_wagon_1"
                            },
                            heliPreset = ""
                        },
                        new EventConfig
                        {
                            presetName = "train_xmas_medium",
                            displayName = en ? "Medium Christmas train" : "Средний Новогодний Поезд",
                            isAutoStart = true,
                            chance = 0,
                            minTimeAfterWipe = 0,
                            maxTimeAfterWipe = -1,
                            zoneRadius = 100,
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 300,
                            locomotivePreset = "locomotive_turret",
                            wagonsPreset = new List<string>
                            {
                                "xmas_wagon_1",
                                "xmas_wagon_2"
                            },
                            heliPreset = ""
                        },
                        new EventConfig
                        {
                            presetName = "train_xmas_hard",
                            displayName = en ? "Big Christmas train" : "Большой Новогодний Поезд",
                            isAutoStart = true,
                            chance = 0,
                            minTimeAfterWipe = 0,
                            maxTimeAfterWipe = -1,
                            zoneRadius = 100,
                            isUndergroundTrain = false,
                            eventTime = 3600,
                            stopTime = 300,
                            locomotivePreset = "locomotive_new",
                            wagonsPreset = new List<string>
                            {
                                "wagon_crate_2",
                                "xmas_wagon_2",
                                "wagon_bradley"
                            },
                            heliPreset = ""
                        }
                    },
                    locomotiveConfigs = new HashSet<LocomotiveConfig>
                    {
                        new LocomotiveConfig
                        {
                            presetName = "locomotive_default",
                            prefabName = "assets/content/vehicles/trains/workcart/workcart_aboveground.entity.prefab",
                            engineForce = 250000f,
                            maxSpeed = 12,
                            driverName = "traindriver",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, 4)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, 2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, 0)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.742, 1.458, -3.5)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, -3.5)",
                                        rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            handleBrakeConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.097, 2.805, 1.816)",
                                    rotation = "(0, 180, 0)"
                                }
                            },
                            eventTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.097, 2.412, 1.810)",
                                    rotation = "(0, 180, 0)"
                                }
                            },
                            stopTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.097, 3.012, 1.810)",
                                    rotation = "(0, 180, 0)"
                                }
                            }
                        },
                        new LocomotiveConfig
                        {
                            presetName = "locomotive_turret",
                            prefabName = "assets/content/vehicles/trains/workcart/workcart_aboveground2.entity.prefab",
                            engineForce = 250000f,
                            maxSpeed = 12,
                            driverName = "traindriver",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0.684, 3.845, 3.683)",
                                        rotation = "(0, 0, 0)"
                                    }
                                },
                                ["turret_m249"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0.945, 2.627, 0.556)",
                                        rotation = "(0, 313, 0)"
                                    }
                                }
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, 4)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, 2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, 0)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.742, 1.458, -3.5)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.742, 1.458, -3.5)",
                                        rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            handleBrakeConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.097, 2.805, 1.816)",
                                    rotation = "(0, 180, 0)"
                                }
                            },
                            eventTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.097, 2.412, 1.810)",
                                    rotation = "(0, 180, 0)"
                                }
                            },
                            stopTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.097, 3.012, 1.810)",
                                    rotation = "(0, 180, 0)"
                                }
                            }
                        },
                        new LocomotiveConfig
                        {
                            presetName = "locomotive_new",
                            prefabName = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab",
                            engineForce = 500000f,
                            maxSpeed = 14,
                            driverName = "traindriver",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_m249"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.554, 1.546, -8.849)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.554, 1.546, -8.849)",
                                        rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, 2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, 0)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, -2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, -4)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, -6)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, -8)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, 2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, 0)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, -2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, -4)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, -6)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, -8)",
                                        rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            handleBrakeConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.270, 2.805, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                }
                            },
                            eventTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.270, 2.412, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                }
                            },
                            stopTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.270, 3.012, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                }
                            }
                        },

                        new LocomotiveConfig
                        {
                            presetName = "locomotive_halloween",
                            prefabName = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab",
                            engineForce = 500000f,
                            maxSpeed = 14,
                            driverName = "traindriver",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, 2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, -2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.341, 1.546, -6)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, 2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, -2)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.341, 1.546, -6)",
                                        rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            handleBrakeConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.270, 2.805, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                }
                            },
                            eventTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.270, 2.412, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                }
                            },
                            stopTimerConfig = new EntitySpawnConfig
                            {
                                isEnable = true,
                                location = new LocationConfig
                                {
                                    position = "(0.270, 3.012, -7.896)",
                                    rotation = "(0, 145.462, 0)"
                                }
                            }
                        }
                    },
                    wagonConfigs = new HashSet<WagonConfig>
                    {
                        new WagonConfig
                        {
                            presetName = "wagon_crate_1",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonc.entity.prefab",

                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.940, 1.559, -6.811)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.940, 1.559, -6.811)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.940, 1.559, 6.811)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.940, 1.559, 6.811)",
                                        rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["chinooklockedcrate_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0, 1.550, 0)",
                                        rotation = "(0, 0, 0)"
                                    }
                                },
                                ["crateelite_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0.7, 1.550, -2.359)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.7, 1.550, -2.359)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.7, 1.550, 2.359)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.7, 1.550, 2.359)",
                                        rotation = "(0, 180, 0)"
                                    }
                                },
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            }
                        },
                        new WagonConfig
                        {
                            presetName = "wagon_crate_2",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonb.entity.prefab",

                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["trainnpc"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-1.177, 1.458, -2.267)",
                                        rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.177, 1.458, 0.475)",
                                        rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-1.177, 1.458, 3.202)",
                                        rotation = "(0, 270, 0)"
                                    }
                                }
                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["chinooklockedcrate_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.772, 1.550, 5.693)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.772, 1.550, -5.693)",
                                        rotation = "(0, 0, 0)"
                                    }
                                },
                                ["crateelite_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(1.076, 1.550, 1.047)",
                                        rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.076, 1.550, -0.609)",
                                        rotation = "(0, 270, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(1.076, 1.550, -2.359)",
                                        rotation = "(0, 270, 0)"
                                    }
                                },
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            }
                        },
                        new WagonConfig
                        {
                            presetName = "wagon_bradley",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonb.entity.prefab",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["bradley_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.185, 2.206, -3.36)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.185, 2.206, 3.460)",
                                        rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["assets/content/vehicles/modularcar/module_entities/2module_fuel_tank.prefab"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.772, 3.008, -4.295)",
                                        rotation = "(0, 0, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.772, 3.008, -0.232)",
                                        rotation = "(0, 0, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.812, 1.659, -3.221)",
                                        rotation = "(90, 270, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        position = "(0.772, 3.008, 4.295)",
                                        rotation = "(0, 180, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.772, 3.008, 0.232)",
                                        rotation = "(0, 180, 90)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.812, 1.659, 3.221)",
                                        rotation = "(90, 90, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        position = "(-0.757, 1.659, 3.226)",
                                        rotation = "(90, 270, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        position = "(0.516, 1.7, 5.521)",
                                        rotation = "(90, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.516, 1.7, 5.521)",
                                        rotation = "(90, 0, 0)"
                                    },

                                    new LocationConfig
                                    {
                                        position = "(0.516, 1.7, -5.521)",
                                        rotation = "(90, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.516, 1.7, -5.521)",
                                        rotation = "(90, 180, 0)"
                                    }
                                }
                            }
                        },
                        new WagonConfig
                        {
                            presetName = "wagon_samsite",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadablefuel.entity.prefab",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0, 4.296, -5.346)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0, 4.296, 5.346)",
                                        rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                            npcs = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            },
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["samsite_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0, 4.216, 0)",
                                        rotation = "(0, 180, 0)"
                                    }
                                }
                            },
                            decors = new Dictionary<string, HashSet<LocationConfig>>
                            {

                            }
                        },
                        new WagonConfig
                        {
                            presetName = "caboose_wagon",
                            prefabName = "assets/content/vehicles/trains/caboose/traincaboose.entity.prefab",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            crates = new Dictionary<string, HashSet<LocationConfig>>(),
                            samsites = new Dictionary<string, HashSet<LocationConfig>>(),
                            decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            turrets = new Dictionary<string, HashSet<LocationConfig>>()
                        },
                        new WagonConfig
                        {
                            presetName = "halloween_wagon",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["crate_normal_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0.407, 2.403, -3.401)",
                                        rotation = "(303.510, 0, 328.794)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.374, 2.416, 3.104)",
                                        rotation = "(21.106, 261.772, 352.540)"
                                    }
                                },
                                ["crate_normal2_default"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.095, 1.817, -0.217)",
                                        rotation = "(19.048, 336.704, 359.624)"
                                    }
                                }
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>(),
                            decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.940, 1.559, -6.811)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.940, 1.559, -6.811)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.940, 1.559, 6.811)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.940, 1.559, 6.811)",
                                        rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                        },

                        new WagonConfig
                        {
                            presetName = "xmas_wagon_1",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["xmas_crate"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.148, 2.707, -1.613)",
                                        rotation = "(72.214, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.221, 2.478, -2.314)",
                                        rotation = "(52.555, 180.000, 0)"
                                    }
                                }
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>(),
                            decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["turret_ak"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(-0.940, 1.559, -6.811)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.940, 1.559, -6.811)",
                                        rotation = "(0, 180, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(-0.940, 1.559, 6.811)",
                                        rotation = "(0, 0, 0)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.940, 1.559, 6.811)",
                                        rotation = "(0, 0, 0)"
                                    }
                                }
                            },
                        },
                        new WagonConfig
                        {
                            presetName = "xmas_wagon_2",
                            prefabName = "assets/content/vehicles/trains/wagons/trainwagonunloadable.entity.prefab",
                            brradleys = new Dictionary<string, HashSet<LocationConfig>>(),
                            crates = new Dictionary<string, HashSet<LocationConfig>>
                            {
                                ["xmas_crate"] = new HashSet<LocationConfig>
                                {
                                    new LocationConfig
                                    {
                                        position = "(0.027, 3.276, -3.562)",
                                        rotation = "(0.361, 355.541, 16.972)"
                                    },
                                    new LocationConfig
                                    {
                                        position = "(0.027, 3.276, 3.897)",
                                        rotation = "(334.884, 355.419, 352.239)"
                                    }
                                }
                            },
                            samsites = new Dictionary<string, HashSet<LocationConfig>>(),
                            decors = new Dictionary<string, HashSet<LocationConfig>>(),
                            npcs = new Dictionary<string, HashSet<LocationConfig>>(),
                            turrets = new Dictionary<string, HashSet<LocationConfig>>
                            {
                            },
                        }
                    },
                    bradleysConfigs = new HashSet<BradleyConfig>
                    {
                        new BradleyConfig
                        {
                            presetName = "bradley_default",
                            hp = 900f,
                            scaleDamage = 0.3f,
                            viewDistance = 100.0f,
                            searchDistance = 100.0f,
                            coaxAimCone = 1.1f,
                            coaxFireRate = 1.0f,
                            coaxBurstLength = 10,
                            nextFireTime = 10f,
                            topTurretFireRate = 0.25f
                        },
                    },
                    turretConfigs = new HashSet<TurretConfig>
                    {
                        new TurretConfig
                        {
                            presetName = "turret_ak",
                            hp = 250f,
                            shortNameWeapon = "rifle.ak",
                            shortNameAmmo = "ammo.rifle",
                            countAmmo = 200
                        },
                        new TurretConfig
                        {
                            presetName = "turret_m249",
                            hp = 300f,
                            shortNameWeapon = "lmg.m249",
                            shortNameAmmo = "ammo.rifle",
                            countAmmo = 400
                        }
                    },
                    samsiteConfigs = new HashSet<SamSiteConfig>
                    {
                        new SamSiteConfig
                        {
                            presetName = "samsite_default",
                            hp = 1000,
                            countAmmo = 100
                        }
                    },
                    crateConfigs = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            presetName = "chinooklockedcrate_default",
                            prefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab",
                            skin = 0,
                            hackTime = 0,
                            lootTableConfig = new LootTableConfig
                            {
                                clearDefaultItemList = false,
                                isAlphaLoot = false,
                                alphaLootPresetName = "",
                                isCustomLoot = false,
                                isLootTablePLugin = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                maxItemsAmount = 1,
                                minItemsAmount = 2,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        },
                        new CrateConfig
                        {
                            presetName = "crateelite_default",
                            prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_elite.prefab",
                            skin = 0,
                            hackTime = 0,
                            lootTableConfig = new LootTableConfig
                            {
                                clearDefaultItemList = false,
                                isAlphaLoot = false,
                                alphaLootPresetName = "",
                                isCustomLoot = false,
                                isLootTablePLugin = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                maxItemsAmount = 1,
                                minItemsAmount = 2,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        },
                        new CrateConfig
                        {
                            presetName = "crate_normal_default",
                            prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            skin = 0,
                            hackTime = 0,
                            lootTableConfig = new LootTableConfig
                            {
                                clearDefaultItemList = false,
                                isAlphaLoot = false,
                                alphaLootPresetName = "",
                                isCustomLoot = false,
                                isLootTablePLugin = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                maxItemsAmount = 1,
                                minItemsAmount = 2,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        },
                        new CrateConfig
                        {
                            presetName = "crate_normal2_default",
                            prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            skin = 0,
                            hackTime = 0,
                            lootTableConfig = new LootTableConfig
                            {
                                clearDefaultItemList = false,
                                isAlphaLoot = false,
                                alphaLootPresetName = "",
                                isCustomLoot = false,
                                isLootTablePLugin = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                maxItemsAmount = 1,
                                minItemsAmount = 2,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        },

                        new CrateConfig
                        {
                            presetName = "xmas_crate",
                            prefab = "assets/prefabs/missions/portal/proceduraldungeon/xmastunnels/loot/xmastunnellootbox.prefab",
                            skin = 0,
                            hackTime = 0,
                            lootTableConfig = new LootTableConfig
                            {
                                clearDefaultItemList = false,
                                isAlphaLoot = false,
                                alphaLootPresetName = "",
                                isCustomLoot = false,
                                isLootTablePLugin = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                maxItemsAmount = 1,
                                minItemsAmount = 2,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        }
                    },
                    heliConfigs = new HashSet<HeliConfig>
                    {
                        new HeliConfig
                        {
                            presetName = "heli_1",
                            hp = 10000f,
                            mainRotorHealth = 750f,
                            rearRotorHealth = 375f,
                            height = 50f,
                            bulletDamage = 20f,
                            bulletSpeed = 250f,
                            distance = 250f,
                            speed = 25f,
                            outsideTime = 30,
                            cratesAmount = 3,
                            instCrateOpen = false,
                            cratesLifeTime = 1800,
                            immediatelyKill = true,
                            baseLootTableConfig = new BaseLootTableConfig
                            {
                                clearDefaultItemList = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                maxItemsAmount = 1,
                                minItemsAmount = 2,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "scrap",
                                        minAmount = 100,
                                        maxAmount = 200,
                                        chance = 100f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        }
                    },
                    npcConfigs = new HashSet<NpcConfig>
                    {
                        new NpcConfig
                        {
                            presetName = "trainnpc",
                            displayName = "TrainNpc",
                            health = 200f,
                            speed = 5f,
                            roamRange = 10,
                            chaseRange = 110,
                            deleteCorpse = true,
                            wearItems = new List<NpcWear>
                            {
                                new NpcWear
                                {
                                    shortName = "metal.plate.torso",
                                    skinID = 1988476232
                                },
                                new NpcWear
                                {
                                    shortName = "riot.helmet",
                                    skinID = 1988478091
                                },
                                new NpcWear
                                {
                                    shortName = "pants",
                                    skinID = 1582399729
                                },
                                new NpcWear
                                {
                                    shortName = "tshirt",
                                    skinID = 1582403431
                                },
                                new NpcWear
                                {
                                    shortName = "shoes.boots",
                                    skinID = 0
                                }
                            },
                            beltItems = new List<NpcBelt>
                            {
                                new NpcBelt
                                {
                                    shortName = "rifle.lr300",
                                    amount = 1,
                                    skinID = 0,
                                    mods = new HashSet<string> { "weapon.mod.holosight" },
                                    ammo = ""
                                },
                                new NpcBelt
                                {
                                    shortName = "syringe.medical",
                                    amount = 10,
                                    skinID = 0,
                                    mods = new HashSet<string>(),
                                    ammo = ""
                                },
                                new NpcBelt
                                {
                                    shortName = "grenade.f1",
                                    amount = 10,
                                    skinID = 0,
                                    mods = new HashSet<string>(),
                                    ammo = ""
                                }
                            },
                            kit = "",
                            attackRangeMultiplier = 1f,
                            senseRange = 60f,
                            memoryDuration = 60f,
                            damageScale = 1f,
                            turretDamageScale = 1f,
                            aimConeScale = 1f,
                            checkVisionCone = false,
                            visionCone = 135f,
                            disableRadio = false,
                            lootTableConfig = new LootTableConfig
                            {
                                clearDefaultItemList = false,
                                isAlphaLoot = false,
                                alphaLootPresetName = "",
                                isCustomLoot = false,
                                isLootTablePLugin = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                minItemsAmount = 2,
                                maxItemsAmount = 4,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "rifle.semiauto",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.09f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "shotgun.pump",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.09f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "pistol.semiauto",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.09f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "largemedkit",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "smg.2",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "pistol.python",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "smg.thompson",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "shotgun.waterpipe",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "shotgun.double",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle.explosive",
                                        minAmount = 8,
                                        maxAmount = 8,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "pistol.revolver",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle.hv",
                                        minAmount = 10,
                                        maxAmount = 10,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle.incendiary",
                                        minAmount = 8,
                                        maxAmount = 8,
                                        chance = 0.5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.pistol.hv",
                                        minAmount = 10,
                                        maxAmount = 10,
                                        chance = 0.5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.pistol.fire",
                                        minAmount = 10,
                                        maxAmount = 10,
                                        chance = 1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.shotgun.slug",
                                        minAmount = 4,
                                        maxAmount = 8,
                                        chance = 5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.shotgun.fire",
                                        minAmount = 4,
                                        maxAmount = 14,
                                        chance = 5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.shotgun",
                                        minAmount = 6,
                                        maxAmount = 12,
                                        chance = 8f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "bandage",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 17f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "syringe.medical",
                                        minAmount = 1,
                                        maxAmount = 2,
                                        chance = 34f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle",
                                        minAmount = 12,
                                        maxAmount = 36,
                                        chance = 51f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.pistol",
                                        minAmount = 15,
                                        maxAmount = 45,
                                        chance = 52f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        },
                        new NpcConfig
                        {
                            presetName = "traindriver",
                            displayName = "TrainDriver",
                            health = 200f,
                            speed = 5f,
                            roamRange = 10,
                            chaseRange = 110,
                            deleteCorpse = true,
                            wearItems = new List<NpcWear>
                            {
                                new NpcWear
                                {
                                    shortName = "metal.plate.torso",
                                    skinID = 1988476232
                                },
                                new NpcWear
                                {
                                    shortName = "riot.helmet",
                                    skinID = 1988478091
                                },
                                new NpcWear
                                {
                                    shortName = "pants",
                                    skinID = 1582399729
                                },
                                new NpcWear
                                {
                                    shortName = "tshirt",
                                    skinID = 1582403431
                                },
                                new NpcWear
                                {
                                    shortName = "shoes.boots",
                                    skinID = 0
                                }
                            },
                            beltItems = new List<NpcBelt>
                            {
                                new NpcBelt
                                {
                                    shortName = "rifle.lr300",
                                    amount = 1,
                                    skinID = 0,
                                    mods = new HashSet<string> { "weapon.mod.holosight" },
                                    ammo = ""
                                },
                                new NpcBelt
                                {
                                    shortName = "syringe.medical",
                                    amount = 10,
                                    skinID = 0,
                                    mods = new HashSet<string>(),
                                    ammo = ""
                                },
                                new NpcBelt
                                {
                                    shortName = "grenade.f1",
                                    amount = 10,
                                    skinID = 0,
                                    mods = new HashSet<string>(),
                                    ammo = ""
                                }
                            },
                            kit = "",
                            attackRangeMultiplier = 1f,
                            senseRange = 60f,
                            memoryDuration = 60f,
                            damageScale = 1f,
                            turretDamageScale = 0f,
                            aimConeScale = 1f,
                            checkVisionCone = false,
                            visionCone = 135f,
                            disableRadio = false,
                            lootTableConfig = new LootTableConfig
                            {
                                clearDefaultItemList = false,
                                isAlphaLoot = false,
                                alphaLootPresetName = "",
                                isCustomLoot = false,
                                isLootTablePLugin = false,
                                prefabConfigs = new PrefabLootTableConfigs
                                {
                                    isEnable = false,
                                    prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig
                                        {
                                            minLootScale = 1,
                                            maxLootScale = 1,
                                            prefabName = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"
                                        }
                                    }
                                },
                                isRandomItemsEnable = false,
                                minItemsAmount = 2,
                                maxItemsAmount = 4,
                                items = new List<LootItemConfig>
                                {
                                    new LootItemConfig
                                    {
                                        shortname = "rifle.semiauto",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.09f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "shotgun.pump",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.09f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "pistol.semiauto",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.09f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "largemedkit",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "smg.2",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "pistol.python",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "smg.thompson",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "shotgun.waterpipe",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "shotgun.double",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle.explosive",
                                        minAmount = 8,
                                        maxAmount = 8,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "pistol.revolver",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle.hv",
                                        minAmount = 10,
                                        maxAmount = 10,
                                        chance = 0.2f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle.incendiary",
                                        minAmount = 8,
                                        maxAmount = 8,
                                        chance = 0.5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.pistol.hv",
                                        minAmount = 10,
                                        maxAmount = 10,
                                        chance = 0.5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.pistol.fire",
                                        minAmount = 10,
                                        maxAmount = 10,
                                        chance = 1f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.shotgun.slug",
                                        minAmount = 4,
                                        maxAmount = 8,
                                        chance = 5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.shotgun.fire",
                                        minAmount = 4,
                                        maxAmount = 14,
                                        chance = 5f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.shotgun",
                                        minAmount = 6,
                                        maxAmount = 12,
                                        chance = 8f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "bandage",
                                        minAmount = 1,
                                        maxAmount = 1,
                                        chance = 17f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "syringe.medical",
                                        minAmount = 1,
                                        maxAmount = 2,
                                        chance = 34f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.rifle",
                                        minAmount = 12,
                                        maxAmount = 36,
                                        chance = 51f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    },
                                    new LootItemConfig
                                    {
                                        shortname = "ammo.pistol",
                                        minAmount = 15,
                                        maxAmount = 45,
                                        chance = 52f,
                                        isBlueprint = false,
                                        skin = 0,
                                        name = "",
                                        genomes = new List<string>()
                                    }
                                }
                            }
                        }
                    },
                    markerConfig = new MarkerConfig
                    {
                        enable = true,
                        useRingMarker = true,
                        useShopMarker = true,
                        radius = 0.2f,
                        alpha = 0.6f,
                        color1 = new ColorConfig { r = 0.81f, g = 0.25f, b = 0.15f },
                        color2 = new ColorConfig { r = 0f, g = 0f, b = 0f }
                    },
                    zoneConfig = new ZoneConfig
                    {
                        isPVPZone = false,
                        isDome = false,
                        darkening = 5,
                        isColoredBorder = false,
                        brightness = 5,
                        borderColor = 2,
                        radius = 100
                    },
                    guiConfig = new GUIConfig
                    {
                        isEnable = true,
                        offsetMinY = -56
                    },
                    notifyConfig = new NotifyConfig
                    {
                        preStartTime = 0,
                        isChatEnable = true,
                        timeNotifications = new HashSet<int>
                        {
                            300,
                            60,
                            30,
                            5
                        },
                        gameTipConfig = new GameTipConfig
                        {
                            isEnabled = false,
                            style = 2,
                        }
                    },
                    supportedPluginsConfig = new SupportedPluginsConfig
                    {
                        pveMode = new PveModeConfig
                        {
                            enable = false,
                            ignoreAdmin = false,
                            ownerIsStopper = true,
                            noInterractIfCooldownAndNoOwners = true,
                            noDealDamageIfCooldownAndTeamOwner = false,
                            canLootOnlyOwner = true,
                            showEventOwnerNameOnMap = true,
                            damage = 500f,
                            scaleDamage = new Dictionary<string, float>
                            {
                                ["Npc"] = 1f,
                                ["Bradley"] = 2f,
                                ["Helicopter"] = 2f,
                                ["Turret"] = 2f,
                            },
                            lootCrate = false,
                            hackCrate = false,
                            lootNpc = false,
                            damageNpc = false,
                            targetNpc = false,
                            damageTank = false,
                            targetTank = false,
                            damageTurret = false,
                            targetTurret = false,
                            canEnter = false,
                            canEnterCooldownPlayer = true,
                            timeExitOwner = 300,
                            alertTime = 60,
                            restoreUponDeath = true,
                            cooldown = 86400,
                            darkening = 12
                        },
                        economicsConfig = new EconomyConfig
                        {
                            enable = false,
                            plugins = new HashSet<string> { "Economics", "Server Rewards", "IQEconomic" },
                            minCommandPoint = 0,
                            minEconomyPiont = 0,
                            crates = new Dictionary<string, double>
                            {
                                ["assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab"] = 0.4
                            },
                            npcPoint = 2,
                            bradleyPoint = 5,
                            hackCratePoint = 5,
                            turretPoint = 2,
                            heliPoint = 5,
                            commands = new HashSet<string>()
                        },
                        guiAnnouncementsConfig = new GUIAnnouncementsConfig
                        {
                            isEnabled = false,
                            bannerColor = "Grey",
                            textColor = "White",
                            apiAdjustVPosition = 0.03f
                        },
                        notifyPluginConfig = new NotifyPluginConfig
                        {
                            isEnabled = false,
                            type = 0
                        },
                        discordMessagesConfig = new DiscordConfig
                        {
                            isEnabled = false,
                            webhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                            embedColor = 13516583,
                            keys = new HashSet<string>
                            {
                                "PreStartTrain",
                                "PlayerStopTrain",
                                "EndEvent"
                            }
                        },
                        betterNpcConfig = new BetterNpcConfig
                        {
                            isHeliNpc = false,
                        }
                    }
                };
            }
        }
        #endregion Config
    }
}

namespace Oxide.Plugins.ArmoredTrainExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }

        public static HashSet<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            HashSet<TSource> result = new HashSet<TSource>();

            using (var enumerator = source.GetEnumerator())
                while (enumerator.MoveNext())
                    if (predicate(enumerator.Current))
                        result.Add(enumerator.Current);
            return result;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return enumerator.Current;
            return default(TSource);
        }

        public static HashSet<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(predicate(enumerator.Current));
            return result;
        }

        public static List<TResult> Select<TSource, TResult>(this IList<TSource> source, Func<TSource, TResult> predicate)
        {
            List<TResult> result = new List<TResult>();
            for (int i = 0; i < source.Count; i++)
            {
                TSource element = source[i];
                result.Add(predicate(element));
            }
            return result;
        }

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static bool IsRealPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();

        public static List<TSource> OrderBy<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            List<TSource> result = source.ToList();
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = 0; j < result.Count - 1; j++)
                {
                    if (predicate(result[j]) > predicate(result[j + 1]))
                    {
                        TSource z = result[j];
                        result[j] = result[j + 1];
                        result[j + 1] = z;
                    }
                }
            }
            return result;
        }

        public static List<TSource> ToList<TSource>(this IEnumerable<TSource> source)
        {
            List<TSource> result = new List<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static HashSet<T> OfType<T>(this IEnumerable<BaseNetworkable> source)
        {
            HashSet<T> result = new HashSet<T>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (enumerator.Current is T) result.Add((T)(object)enumerator.Current);
            return result;
        }

        public static TSource Max<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = source.ElementAt(0);
            float resultValue = predicate(result);
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue > resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static TSource Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = source.ElementAt(0);
            float resultValue = predicate(result);
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue < resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static TSource ElementAt<TSource>(this IEnumerable<TSource> source, int index)
        {
            int movements = 0;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    if (movements == index) return enumerator.Current;
                    movements++;
                }
            }
            return default(TSource);
        }

        public static TSource First<TSource>(this IList<TSource> source) => source[0];

        public static TSource Last<TSource>(this IList<TSource> source) => source[source.Count - 1];

        public static bool IsEqualVector3(this Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.1f;

        public static List<TSource> OrderByQuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate)
        {
            return source.QuickSort(predicate, 0, source.Count - 1);
        }

        private static List<TSource> QuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate, int minIndex, int maxIndex)
        {
            if (minIndex >= maxIndex) return source;

            int pivotIndex = minIndex - 1;
            for (int i = minIndex; i < maxIndex; i++)
            {
                if (predicate(source[i]) < predicate(source[maxIndex]))
                {
                    pivotIndex++;
                    source.Replace(pivotIndex, i);
                }
            }
            pivotIndex++;
            source.Replace(pivotIndex, maxIndex);

            QuickSort(source, predicate, minIndex, pivotIndex - 1);
            QuickSort(source, predicate, pivotIndex + 1, maxIndex);

            return source;
        }

        private static void Replace<TSource>(this IList<TSource> source, int x, int y)
        {
            TSource t = source[x];
            source[x] = source[y];
            source[y] = t;
        }
    }
}