using Harmony;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using UnityEngine;

[ModTitle("FilteredNets")]
[ModDescription("Configure your item nets to only catch specific items")]
[ModAuthor("janniksam")] 
[ModIconUrl("https://i.imgur.com/ZK3HMSJ.jpg")]
[ModWallpaperUrl("https://i.imgur.com/gJP6ymx.jpg")]
[ModVersionCheckUrl("https://www.raftmodding.com/api/v1/mods/filterednets/version.txt")] 
[ModVersion("1.0")] 
[RaftVersion("Update 11 (4677160)")] 
[ModIsPermanent(true)] 
public class FilteredNets : Mod
{
    private static readonly string m_modNamePrefix = "<color=#42a7f5>Filtered</color><color=#FF0000>Nets</color>";
    private static readonly string m_configurationPath = Path.Combine(Directory.GetCurrentDirectory(), @"mods\ModData\FilteredNets\");
    private static readonly string m_configurationFile = "netfilterMapping.xml";
    private static Dictionary<uint, string> m_netSetup = new Dictionary<uint, string>();

    public HarmonyInstance harmony;
    public readonly string harmonyID = "com.janniksam.raftmods.filterednets";

    public void Start()
    {
        RConsole.Log(string.Format("{0} has been loaded!", m_modNamePrefix));
        harmony = HarmonyInstance.Create(harmonyID);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    public void OnModUnload()
    {
        RConsole.Log(string.Format("{0} has been unloaded!", m_modNamePrefix));
        
        harmony.UnpatchAll(harmonyID);
        Destroy(gameObject);
    }

    //public void Update()
    //{
    //}

    private static void ToggleFilterModeForNet(ItemNet net)
    {
        uint netId = net.itemCollector.ObjectIndex;
        if (!m_netSetup.ContainsKey(netId))
        {
            m_netSetup.Add(netId, NetFilters.All);
        }

        string currentFilterMode = m_netSetup[netId];
        string nextFilterMode = NetFilters.Next(currentFilterMode);
        m_netSetup[netId] = nextFilterMode;
        RConsole.Log(string.Format("{0}: Filtermode of net {1} was set to {2}", m_modNamePrefix, netId, nextFilterMode));
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
        
        if(!Semih_Network.IsHost)
        {
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
        m_netSetup.Clear();
        var mappings = ReadConfigurationFile();
        if (mappings == null || 
            mappings.Mappings == null)
        {
            return;
        }

        uint[] itemNets = FindObjectsOfType<ItemNet>().Select(p => p.itemCollector.ObjectIndex).ToArray();
        foreach(var mapping in mappings.Mappings)
        {
            if(itemNets.Contains(mapping.NetId))
            {
                m_netSetup.Add(mapping.NetId, mapping.ActiveNetFilter);
            }
        }
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
            using (FileStream file = File.OpenRead(currentConfigurationFilePath))
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
            RConsole.LogWarning(string.Format("{0}: Cannot read the current configuration. Exception: {1}", m_modNamePrefix, ex));
            return null;
        }
    }

    private void SaveNetFilterMapping()
    {
        var currentConfigurationFilePath = GetConfigurationFilePath();
        var currentConfigurationFileDirectory = Path.GetDirectoryName(currentConfigurationFilePath);
        if (!Directory.Exists(currentConfigurationFileDirectory))
        {
            Directory.CreateDirectory(currentConfigurationFileDirectory);
        }

        var netfilterMapping = new NetFilterMappings(
            m_netSetup.Select(p => new NetFilterMapping
            {
                NetId = p.Key,
                ActiveNetFilter = p.Value
            }).ToArray());

        var writer = new XmlSerializer(typeof(NetFilterMappings));
        using (FileStream file = File.OpenWrite(currentConfigurationFilePath))
        {
            writer.Serialize(file, netfilterMapping);
            file.Flush();
            file.Close();
        }
    }

    private string GetConfigurationFilePath()
    {
        return Path.Combine(m_configurationPath, SaveAndLoad.CurrentGameFileName, m_configurationFile);
    }

    private static bool ShouldBeFilteredOut(PickupItem_Networked item, uint netId)
    {
        if (!m_netSetup.ContainsKey(netId))
        {
            return false;
        }

        var filter = m_netSetup[netId];
        if(filter == NetFilters.All)
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
        if(item.PickupItem.dropper != null)
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
            foreach (Cost cost in item.PickupItem.yieldHandler.yieldAsset.yieldAssets)
            {
                return cost.item.UniqueName;
            }
        }

        return name;
    }

    #region Persisting Net Configuration
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

    public class NetFilterMapping
    {
        public uint NetId { get; set; }

        public string ActiveNetFilter { get; set; }
    }
    #endregion

    #region Harmony Patches
    [HarmonyPatch(typeof(ItemCollector)), HarmonyPatch("OnTriggerEnter")]
    public class ItemCollectorEditPatch
    {
        private static bool Prefix(
            ItemCollector __instance,
            Collider other,
            int ___maxNumberOfItems,
            Collider ___collectorCollider)
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
            PickupItem_Networked item = other.transform.GetComponentInParent<PickupItem_Networked>();
            if (ShouldBeFilteredOut(item, itemNet.itemCollector.ObjectIndex))
            {
                //Skip collection
                return false;
            }

            // Continue with collection algorhythm
            return true;
        }
    }

    [HarmonyPatch(typeof(ItemNet)), HarmonyPatch("OnIsRayed")]
    public class ItemNetEditPatch
    {
        private static bool Prefix(
            ItemNet __instance,
            CanvasHelper ___canvas,
            ref bool ___displayText)
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

    private class NetFilters
    {
        public static string All = "Default";
        private static string Planks = "Plank";
        private static string Plastic = "Plastic";
        private static string Thatches = "Thatch";
        public static string Barrels = "Barrels";

        private static string[] AllFilters = new string[]
        {
            All,
            Planks,
            Plastic,
            Thatches,
            Barrels,
        };

        internal static string Next(string currentFilterMode)
        {
            int nextIndex = Array.IndexOf(AllFilters, currentFilterMode) + 1;
            if (nextIndex >= AllFilters.Length)
            {
                nextIndex = 0;
            }

            return AllFilters[nextIndex];
        }
    }
}