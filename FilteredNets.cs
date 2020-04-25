using Harmony;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml.Serialization;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable once CheckNamespace

[ModTitle("FilteredNets")]
[ModDescription("Configure your item nets to only catch specific items")]
[ModAuthor("janniksam")]
[ModIconUrl("https://raw.githubusercontent.com/janniksam/Raft.FilteredNets/master/banner.png")]
[ModWallpaperUrl("https://raw.githubusercontent.com/janniksam/Raft.FilteredNets/master/banner.png")]
[ModVersionCheckUrl("https://www.raftmodding.com/api/v1/mods/filterednets/version.txt")]
[ModVersion("1.21")]
[RaftVersion("Update 11 (4677160)")]
[ModIsPermanent(true)]
public class FilteredNets : Mod
{
    private const string HarmonyId = "com.janniksam.raftmods.filterednets";
    private const string ModNamePrefix = "<color=#42a7f5>Filtered</color><color=#FF0000>Nets</color>";
    private const string ConfigurationSubPath = @"mods\ModData\FilteredNets\";
    private const string ConfigurationFile = "netfilterMapping.xml";

    private readonly string m_configurationPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigurationSubPath);
    private static readonly Dictionary<uint, string> m_netSetup = new Dictionary<uint, string>();
    private HarmonyInstance m_harmony;

    [UsedImplicitly]
    public void Start()
    {
        RConsole.Log(string.Format("{0} has been loaded!", ModNamePrefix));
        m_harmony = HarmonyInstance.Create(HarmonyId);
        m_harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    [UsedImplicitly]
    public void OnModUnload()
    {
        RConsole.Log(string.Format("{0} has been unloaded!", ModNamePrefix));

        m_harmony.UnpatchAll(HarmonyId);
        Destroy(gameObject);
    }

    [UsedImplicitly]
    public void Update()
    {
        MessageHandler.ReadP2P_Channel();
    }

    private static void ToggleFilterModeForNet(ItemNet net)
    {
        uint netId = net.itemCollector.ObjectIndex;
        if (!m_netSetup.ContainsKey(netId))
        {
            m_netSetup.Add(netId, NetFilters.All);
        }

        string currentFilterMode = m_netSetup[netId];
        string nextFilterMode = NetFilters.Next(currentFilterMode);
        SetNetFilter(netId, nextFilterMode);
        MessageHandler.SendMessage(
            new MessageItemNetFilterChanged(
                (Messages)MessageHandler.FilteredNetsMessages.FilterChanged, netId, nextFilterMode));
    }

    private static void SetNetFilter(uint netId, string nextFilterMode)
    {
        m_netSetup[netId] = nextFilterMode;
        RConsole.Log(string.Format("{0}: Filtermode of net {1} was set to {2}", ModNamePrefix, netId, nextFilterMode));
    }

    private static string GetCurrentFilter(ItemNet net)
    {
        var netId = net.itemCollector.ObjectIndex;
        if (!m_netSetup.ContainsKey(netId))
        {
            return NetFilters.All;
        }

        return m_netSetup[netId];
    }

    public override void WorldEvent_WorldLoaded()
    {
        base.WorldEvent_WorldLoaded();
        m_netSetup.Clear();

        if (!Semih_Network.IsHost)
        {
            // Requesting current filters from host
            MessageHandler.SendMessage(
                new MessageSyncNetFiltersRequest(
                    (Messages)MessageHandler.FilteredNetsMessages.SyncNetFiltersRequested));
            return;
        }

        LoadNetFilterMapping();
    }

    public override void WorldEvent_WorldSaved()
    {
        base.WorldEvent_WorldSaved();

        if (!Semih_Network.IsHost)
        {
            return;
        }

        SaveNetFilterMapping();
    }

    private void LoadNetFilterMapping()
    {
        var mappings = ReadConfigurationFile();
        ApplyFilters(mappings);
    }

    private static void ApplyFilters(NetFilterMappings mappings)
    {
        if (mappings == null ||
            mappings.Mappings == null)
        {
            return;
        }

        uint[] itemNets = FindObjectsOfType<ItemNet>().Select(p => p.itemCollector.ObjectIndex).ToArray();
        foreach (var mapping in mappings.Mappings)
        {
            if (itemNets.Contains(mapping.NetId))
            {
                if (m_netSetup.ContainsKey(mapping.NetId))
                {
                    m_netSetup[mapping.NetId] = mapping.ActiveNetFilter;
                }
                else
                {
                    m_netSetup.Add(mapping.NetId, mapping.ActiveNetFilter);
                }
            }
        }
    }

    private static void SyncFiltersWithPlayers()
    {
        RConsole.Log(string.Format("{0}: Sync was requested. Syncing filters with players...", ModNamePrefix));

        var mappings = GetCurrentFilterMapping();
        MessageHandler.SendMessage(
             new MessageSyncNetFilters(
                (Messages)MessageHandler.FilteredNetsMessages.SyncNetFilters, mappings));
    }

    private NetFilterMappings ReadConfigurationFile()
    {
        string currentConfigurationFilePath = GetConfigurationFilePath();
        var reader = new XmlSerializer(typeof(NetFilterMappings));
        if (!File.Exists(currentConfigurationFilePath))
        {
            return null;
        }

        try
        {
            using (var file = File.OpenRead(currentConfigurationFilePath))
            {
                var mappings = reader.Deserialize(file) as NetFilterMappings;
                if (mappings == null)
                {
                    return null;
                }
                file.Flush();
                file.Close();

                return mappings;
            }
        }
        catch (IOException ex)
        {
            RConsole.LogWarning(string.Format("{0}: Cannot read the current configuration. Exception: {1}", ModNamePrefix, ex));
            return null;
        }
    }

    private void SaveNetFilterMapping()
    {
        var currentConfigurationFilePath = GetConfigurationFilePath();
        var currentConfigurationFileDirectory = Path.GetDirectoryName(currentConfigurationFilePath);
        if (currentConfigurationFileDirectory == null)
        {
            RConsole.LogError(string.Format("{0}: Cannot determine save-path.", ModNamePrefix));
            return;
        }

        if (!Directory.Exists(currentConfigurationFileDirectory))
        {
            Directory.CreateDirectory(currentConfigurationFileDirectory);
        }

        var netfilterMapping = GetCurrentFilterMapping();
        var writer = new XmlSerializer(typeof(NetFilterMappings));
        using (var file = File.OpenWrite(currentConfigurationFilePath))
        {
            writer.Serialize(file, netfilterMapping);
            file.Flush();
            file.Close();
        }
    }

    private static NetFilterMappings GetCurrentFilterMapping()
    {
        return new NetFilterMappings(
                    m_netSetup.Select(p => new NetFilterMapping
                    {
                        NetId = p.Key,
                        ActiveNetFilter = p.Value
                    }).ToArray());
    }

    private string GetConfigurationFilePath()
    {
        return Path.Combine(m_configurationPath, SaveAndLoad.CurrentGameFileName, ConfigurationFile);
    }

    private static bool ShouldBeFilteredOut(PickupItem_Networked item, uint netId)
    {
        if (!m_netSetup.ContainsKey(netId))
        {
            return false;
        }

        var filter = m_netSetup[netId];
        if (filter == NetFilters.All)
        {
            return false;
        }

        string name = GetItemName(item);
        if (name == NetFilters.Barrels)
        {
            return false;
        }

        return name != filter;
    }

    private static string GetItemName(PickupItem_Networked item)
    {
        if (item.PickupItem.dropper != null)
        {
            return NetFilters.Barrels;
        }

        string name = "";
        if (item.PickupItem.itemInstance.Valid)
        {
            name = item.PickupItem.itemInstance.UniqueName;
        }
        else
        {
            foreach (var cost in item.PickupItem.yieldHandler.yieldAsset.yieldAssets)
            {
                return cost.item.UniqueName;
            }
        }

        return name;
    }

    #region Persisting Net Configuration
    [Serializable]
    public class NetFilterMappings
    {
        public NetFilterMappings()
        {
        }

        public NetFilterMappings(NetFilterMapping[] mappings)
        {
            Mappings = mappings;
        }

        public NetFilterMapping[] Mappings { get; set; }
    }

    [Serializable]
    public class NetFilterMapping
    {
        public uint NetId { get; set; }

        public string ActiveNetFilter { get; set; }
    }
    #endregion

    #region Harmony Patches

    [HarmonyPatch(typeof(ItemCollector)), HarmonyPatch("OnTriggerEnter")]
    [UsedImplicitly]
    public class ItemCollectorEditPatch
    {
        [UsedImplicitly]
        private static bool Prefix(
            // ReSharper disable InconsistentNaming
            // ReSharper disable SuggestBaseTypeForParameter
            ItemCollector __instance,
            Collider other,
            int ___maxNumberOfItems,
            Collider ___collectorCollider)
            // ReSharper restore SuggestBaseTypeForParameter
            // ReSharper restore InconsistentNaming
        {
            if (!___collectorCollider.enabled ||
                ___maxNumberOfItems != 0 &&
                __instance.collectedItems.Count >= ___maxNumberOfItems ||
                !Helper.ObjectIsOnLayer(other.transform.gameObject, __instance.collectMask))
            {
                //Skip collection
                return false;
            }

            var itemNet = __instance.GetComponentInParent<ItemNet>();
            if (itemNet == null || 
                itemNet.itemCollector == null)
            {
                // this is not an item net, continue with collection algorithm
                return true;
            }
            var item = other.transform.GetComponentInParent<PickupItem_Networked>();
            if (item == null)
            {
                // invalid item, continue with collection algorithm
                return true;
            }
            
            if (ShouldBeFilteredOut(item, itemNet.itemCollector.ObjectIndex))
            {
                //Skip collection
                return false;
            }

            // Continue with collection algorithm
            return true;
        }
    }

    [HarmonyPatch(typeof(ItemNet)), HarmonyPatch("OnIsRayed")]
    [UsedImplicitly]
    public class ItemNetEditPatch
    {
        [UsedImplicitly]
        private static bool Prefix
        (
            // ReSharper disable InconsistentNaming
            ItemNet __instance,
            CanvasHelper ___canvas,
            ref bool ___displayText)
            // ReSharper restore InconsistentNaming
        {
            // This overrides the original logic. 
            // If the internal logic is changed in the future, this has to change aswell.
            if (!Helper.LocalPlayerIsWithinDistance(
                __instance.transform.position, Player.UseDistance))
            {
                // Not within use distance, run old logic
                return true;
            }

            if (MyInput.GetButtonDown("Rotate"))
            {
                ToggleFilterModeForNet(__instance);
            }

            ___displayText = true;
            var toggleFilterText = string.Format("Toggle net filter ({0})", GetCurrentFilter(__instance));
            if (__instance.itemCollector.collectedItems.Count > 0)
            {
                ___canvas.displayTextManager.HideDisplayTexts();

                LocalizationParameters.itemCount = __instance.itemCollector.collectedItems.Count;

                ___canvas.displayTextManager.ShowText(
                    toggleFilterText, MyInput.Keybinds["Rotate"].MainKey, 1,
                    Helper.GetTerm("Game/CollectItemCount", true), MyInput.Keybinds["Interact"].MainKey, 2);
            }
            else
            {
                ___canvas.displayTextManager.ShowText(
                    toggleFilterText, MyInput.Keybinds["Rotate"].MainKey, 1);
            }

            // skip original logic
            return false;
        }
    }

    #endregion

    private static class NetFilters
    {
        public const string All = "Default";
        private const string Planks = "Plank";
        private const string Plastic = "Plastic";
        private const string Thatches = "Thatch";
        public const string Barrels = "Barrels";

        private static readonly string[] m_allFilters =
        {
            All,
            Planks,
            Plastic,
            Thatches,
            Barrels,
        };

        internal static string Next(string currentFilterMode)
        {
            int nextIndex = Array.IndexOf(m_allFilters, currentFilterMode) + 1;
            if (nextIndex >= m_allFilters.Length)
            {
                nextIndex = 0;
            }

            return m_allFilters[nextIndex];
        }
    }

    #region Networking

    private static class MessageHandler
    {
        private const int FilteredNetsNetworkChannel = 72;

        public enum FilteredNetsMessages
        {
            FilterChanged = 10910,
            SyncNetFilters = 10911,
            SyncNetFiltersRequested = 10912
        }

        public static void SendMessage(Message message)
        {
            if (Semih_Network.IsHost)
            {
                RAPI.GetLocalPlayer().Network.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, (NetworkChannel)FilteredNetsNetworkChannel);
            }
            else
            {
                RAPI.GetLocalPlayer().SendP2P(message, EP2PSend.k_EP2PSendReliable, (NetworkChannel)FilteredNetsNetworkChannel);
            }
        }

        public static void ReadP2P_Channel()
        {
            if (Semih_Network.InLobbyScene)
            {
                return;
            }

            uint num;
            while (SteamNetworking.IsP2PPacketAvailable(out num, FilteredNetsNetworkChannel))
            {
                byte[] array = new byte[num];
                uint num2;
                CSteamID cSteamId;
                if (!SteamNetworking.ReadP2PPacket(array, num, out num2, out cSteamId, FilteredNetsNetworkChannel))
                {
                    continue;
                }

                var messages = DeserializeMessages(array);
                foreach (var message in messages)
                {
                    if (message == null)
                    {
                        continue;
                    }

                    Messages type = message.Type;
                    switch (type)
                    {
                        case (Messages)FilteredNetsMessages.FilterChanged:
                            {
                                var filterMessage = message as MessageItemNetFilterChanged;
                                if (filterMessage == null)
                                {
                                    break;
                                }

                                SetNetFilter(filterMessage.ObjectIndex, filterMessage.NewFilter);
                                break;
                            }
                        case (Messages)FilteredNetsMessages.SyncNetFilters:
                            {
                                var syncMessage = message as MessageSyncNetFilters;
                                if (syncMessage == null)
                                {
                                    break;
                                }

                                ApplyFilters(syncMessage.Mappings);

                                RConsole.Log(string.Format("{0}: Synced the item net filters with host...", ModNamePrefix));
                                break;
                            }
                        case (Messages)FilteredNetsMessages.SyncNetFiltersRequested:
                            {
                                if (Semih_Network.IsHost)
                                {
                                    SyncFiltersWithPlayers();
                                }
                                break;
                            }
                    }
                }
            }
        }

        private static Message[] DeserializeMessages(byte[] array)
        {
            Packet packet;
            using (var ms = new MemoryStream(array))
            {
                var bf = new BinaryFormatter
                {
                    Binder = new PreMergeToMergedDeserializationBinder()
                };

                packet = bf.Deserialize(ms) as Packet;
            }

            if (packet == null)
            {
                return new Message[0];
            }

            if (packet.PacketType == PacketType.Single)
            {
                var packetSingle = packet as Packet_Single;
                if (packetSingle == null)
                {
                    return new Message[0];
                }

                return new[]
                {
                    packetSingle.message
                };
            }

            var multiplepackages = packet as Packet_Multiple;
            if (multiplepackages == null)
            {
                return new Message[0];
            }

            return multiplepackages.messages;
        }
    }

    [Serializable]
    public class MessageItemNetFilterChanged : Message
    {
        public uint ObjectIndex;
        public string NewFilter;

        public MessageItemNetFilterChanged()
        {
        }

        public MessageItemNetFilterChanged(Messages type, uint objectIndex, string newFilter)
            : base(type)
        {
            ObjectIndex = objectIndex;
            NewFilter = newFilter;
        }
    }

    [Serializable]
    public class MessageSyncNetFilters : Message
    {
        public NetFilterMappings Mappings;

        public MessageSyncNetFilters()
        {
        }

        public MessageSyncNetFilters(Messages type, NetFilterMappings mappings)
            : base(type)
        {
            Mappings = mappings;
        }
    }

    [Serializable]
    public class MessageSyncNetFiltersRequest : Message
    {
        public MessageSyncNetFiltersRequest()
        {
        }

        public MessageSyncNetFiltersRequest(Messages type)
            : base(type)
        {
        }
    }

    private sealed class PreMergeToMergedDeserializationBinder : SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            string exeAssembly = Assembly.GetExecutingAssembly().FullName;
            var typeToDeserialize = Type.GetType(string.Format("{0}, {1}", typeName, exeAssembly));
            return typeToDeserialize;
        }
    }
    #endregion
}