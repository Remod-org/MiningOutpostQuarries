#region License (GPL v2)
/*
    Mining Outpost Quarries
    Copyright (c) 2023 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v2)
using Oxide.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Facepunch.Extend;

namespace Oxide.Plugins
{
    [Info("MiningOutpostQuarries", "RFC1920", "1.0.1")]
    [Description("Spawn quarries next to MiningOutposts just like the good old days")]
    internal class MiningOutpostQuarries : RustPlugin
    {
        private ConfigData configData;
        public static MiningOutpostQuarries Instance;
        public List<uint> Quarries = new List<uint>();
        private static SortedDictionary<string, Vector3> monPos = new SortedDictionary<string, Vector3>();
        private static SortedDictionary<string, Quaternion> monRot = new SortedDictionary<string, Quaternion>();
        private static SortedDictionary<string, Vector3> monSize = new SortedDictionary<string, Vector3>();

        private const string quarryprefab = "assets/prefabs/deployable/quarry/mining_quarry.prefab";
        private const string permUse = "miningoutpostquarries.use";

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You don't have permission to do that !!"
            }, this);
        }

        private void OnServerInitialized()
        {
            permission.RegisterPermission(permUse, this);
            LoadConfigVariables();

            Instance = this;

            FindMonuments();
            BuildQuarries();
        }

        private void DoLog(string message)
        {
            if (configData.debug) Interface.Oxide.LogInfo(message);
        }

        private void Unload()
        {
            foreach (uint quarry in Quarries)
            {
                BaseNetworkable quarryObj = BaseNetworkable.serverEntities.Find(quarry);
                if (quarryObj == null) continue;
                Vector3 loc = quarryObj.transform.position;
                UnityEngine.Object.DestroyImmediate(quarryObj, true);
                List<MiningQuarry> oldmq = new List<MiningQuarry>();
                Vis.Entities(loc, 50f, oldmq);
                foreach (MiningQuarry mq in oldmq)
                {
                    UnityEngine.Object.DestroyImmediate(mq, true);
                }
            }
        }

        private class ConfigData
        {
            public Options Options;
            public bool debug;
            public VersionNumber Version;
        }

        class Options
        {
            public int maxQuarries;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();
            if (configData.Options == null) configData.Options = new Options();
            if (configData.Options.maxQuarries == 0) configData.Options.maxQuarries = 3;
            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData()
            {
                debug = false,
                Options = new Options()
                {
                    maxQuarries = 3
                }
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private bool BadLocation(Vector3 location)
        {
            // Avoid placing in a rock or foundation, water, etc.
            int layerMask = LayerMask.GetMask("Construction", "World", "Water", "Road");
            RaycastHit hit;
            if (Physics.Raycast(new Ray(location, Vector3.down), out hit, 6f, layerMask))
            {
                if (!hit.GetCollider().material.name.Contains("Grass"))
                {
                    DoLog($"Found {hit.GetCollider().material.name} {hit.distance}f below this location");
                    return true;
                }
            }
            else if (Physics.Raycast(new Ray(location, Vector3.up), out hit, 6f, layerMask))
            {
                DoLog($"Found {hit.GetCollider().material.name} {hit.distance}f above this location");
                return true;
            }
            else if (Physics.Raycast(new Ray(location, Vector3.forward), out hit, 15f, layerMask))
            {
                DoLog($"Found {hit.GetCollider().material.name} at {hit.distance}f next to this location");
                return true;
            }
            return false;
        }

        private void BuildQuarries()
        {
            int spawned = 0;
            foreach (KeyValuePair<string, Vector3> warehouse in monPos)
            {
                if (spawned > configData.Options.maxQuarries) break;
                DoLog($"Attemping placement near {warehouse.Key} at {warehouse.Value}");
                string whseName = warehouse.Key;
                const float radius = 30f;// monSize[whseName].magnitude / 2;
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2f / 16;
                    Vector3 newPos = new Vector3(Mathf.Cos(angle) * radius, monSize[whseName].y, Mathf.Sin(angle) * radius);

                    newPos += warehouse.Value;
                    newPos.y = TerrainMeta.HeightMap.GetHeight(newPos);

                    DoLog($"Checking location {newPos} size {radius}");
                    if (BadLocation(newPos)) continue;

                    DoLog("Good choice, spawning quarry...");
                    BaseEntity quarryEnt = GameManager.server.CreateEntity(quarryprefab, newPos, monRot[whseName] *= Quaternion.Euler(0, 90, 0), true);
                    quarryEnt.Spawn();
                    MiningQuarry mq = quarryEnt as MiningQuarry;
                    mq.canExtractLiquid = true;
                    mq.canExtractSolid = true;

                    if (quarryEnt == null)
                    {
                        DoLog($"Unable to spawn quarry at {newPos}");
                        continue;
                    }
                    //quarry.FindSuitableParent();
                    DoLog("Adding to list");
                    Quarries.Add(quarryEnt.net.ID);
                    spawned++;
                    break;
                }
            }
            DoLog($"Spawned {spawned} quarries!");
        }

        private void FindMonuments()
        {
            Vector3 extents = Vector3.zero;
            float realWidth = 0f;
            string name = null;

            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>().Where(x => x.name.Contains("warehouse", System.Globalization.CompareOptions.OrdinalIgnoreCase)))
            {
                realWidth = 20f;
                name = Regex.Match(monument.name, @"\w{6}\/(.+\/)(.+)\.(.+)").Groups[2].Value.Replace("_", " ").Replace(" 1", "").Titleize();

                int i = 0;
                string newname = name + i.ToString();
                while (monPos.ContainsKey(newname))
                {
                    i++;
                    newname = name + i.ToString();
                }
                name = newname;
                DoLog($"Found {name}");

                extents = monument.Bounds.extents;
                DoLog($"Size: {extents.z}");
                if (realWidth > 0f)
                {
                    extents.z = realWidth;
                }

                if (extents.z < 1)
                {
                    extents.z = 50f;
                }
                monPos.Add(name, monument.transform.position);
                monSize.Add(name, extents);
                monRot.Add(name, monument.transform.rotation);
            }
        }
    }
}
