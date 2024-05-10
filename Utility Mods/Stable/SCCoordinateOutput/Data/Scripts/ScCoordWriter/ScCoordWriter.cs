﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace YourName.ModName.Data.Scripts.ScCoordWriter
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ScCoordWriter : MySessionComponentBase
    {
        public static ScCoordWriter Instance;
        private ushort NetworkId;
        private List<TrackedItem> TrackedItems;
        private TextWriter Writer;
        private bool Recording;

        private const int Version = 2;
        private readonly string[] _columns =
        {
            "kind", "name", "owner", "faction", "factionColor", "entityId", "health", "position", "rotation"
        };

        private const string Extension = ".scc";
        private const string CommandPrefix = "/coordwriter";
        public string Usage = $"Usage: {CommandPrefix} [stop|start]";

        private int TickCounter = 0;

        private class TrackedItem
        {
            public object item;
            public int initialBlockCount;

            public TrackedItem(object item, int initialBlockCount = 1)
            {
                this.item = item;
                this.initialBlockCount = initialBlockCount;
            }
        }

        public override void LoadData()
        {
            Instance = this;
            NetworkId = 12493;
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.MessageEnteredSender += HandleMessage;
            }
            else
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkId, ReceivedPacket);
            }

            TrackedItems = new List<TrackedItem>();
            MyAPIGateway.Entities.GetEntities(null, entity =>
            {
                if (ShouldBeTracked(entity))
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid != null)
                    {
                        var blocks = new List<IMySlimBlock>();
                        grid.GetBlocks(blocks);
                        TrackedItems.Add(new TrackedItem(grid, blocks.Count));
                    }
                }
                return false;
            });
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
            MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            if (ShouldBeTracked(entity))
            {
                var grid = entity as IMyCubeGrid;
                if (grid != null)
                {
                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks);
                    TrackedItems.Add(new TrackedItem(grid, blocks.Count));
                }
            }
        }

        private void OnEntityRemove(IMyEntity entity)
        {
            for (var i = 0; i < TrackedItems.Count; ++i)
            {
                var cur = TrackedItems[i];
                var grid = cur.item as IMyCubeGrid;
                if (grid != null && grid.EntityId == entity.EntityId)
                {
                    TrackedItems.RemoveAt(i);
                    break;
                }
            }
        }

        private bool ShouldBeTracked(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null || grid.IsStatic || grid.Physics == null)
                return false;

            bool hasPowerBlock = false;
            bool hasGyro = false;
            bool hasThruster = false;

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, block =>
            {
                // Check for functional power block (reactor or battery)
                if (!hasPowerBlock)
                {
                    var battery = block.FatBlock as IMyBatteryBlock;
                    var reactor = block.FatBlock as IMyReactor;
                    if ((battery != null && battery.IsFunctional) || (reactor != null && reactor.IsFunctional))
                        hasPowerBlock = true;
                }

                // Check for functional gyroscope
                if (!hasGyro)
                {
                    var gyro = block.FatBlock as IMyGyro;
                    if (gyro != null && gyro.IsFunctional)
                        hasGyro = true;
                }

                // Check for functional thruster
                if (!hasThruster)
                {
                    var thruster = block.FatBlock as IMyThrust;
                    if (thruster != null && thruster.IsFunctional)
                        hasThruster = true;
                }

                // Continue iterating until all conditions are checked or all are true
                return !(hasPowerBlock && hasGyro && hasThruster); // Return false to stop iterating if all conditions are met
            });

            // Only track grids that have functional power blocks, gyroscopes, and thrusters
            return hasPowerBlock && hasGyro && hasThruster;
        }

        protected override void UnloadData()
        {
            Writer.Close();
            TrackedItems.Clear();
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.MessageEnteredSender -= HandleMessage;
            }
            else
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NetworkId, ReceivedPacket);
            }
            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
            MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
        }

        public void Start()
        {
            var fileName = $"{DateTime.Now:dd-MM-yyyy HHmm}{Extension}";

            try
            {
                Writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(fileName, typeof(ScCoordWriter));
                Writer.NewLine = "\n";
                MyVisualScriptLogicProvider.SendChatMessage("Global grid tracker file created");
                Writer.WriteLine($"version {Version}");
                Writer.WriteLine(string.Join(",", _columns));
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine("Failed to create grid tracker file.");
                MyVisualScriptLogicProvider.SendChatMessage("Failed to create grid tracker file.");
                MyLog.Default.WriteLine(ex);
            }

            Recording = true;
            MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, new byte[] { 1 });
            MyAPIGateway.Utilities.ShowNotification("Recording started.");
        }

        public void Stop()
        {
            Recording = false;
            MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, new byte[] { 0 });
            MyAPIGateway.Utilities.ShowNotification("Recording ended.");
        }

        public override void UpdateAfterSimulation()
        {
            if (!Recording) return;
            if (TrackedItems == null)
            {
                MyVisualScriptLogicProvider.SendChatMessage("TrackedItems is null");
                return;
            }

            if (TickCounter++ < 60) { return; }
            TickCounter = 0;

            Writer.WriteLine($"start_block,{DateTime.Now}");
            TrackedItems.ForEach(element =>
            {
                if (element.item == null)
                {
                    MyLog.Default.WriteLine("null item in TrackedItems");
                    return;
                }

                var grid = element.item as IMyCubeGrid;
                var owner = GetGridOwner(grid);
                var factionName = GetFactionName(owner);
                var factionColor = GetFactionColor(owner);

                MatrixD worldMatrix = grid.WorldMatrix;
                var position = grid.GetPosition();
                var rotation = Quaternion.CreateFromForwardUp(worldMatrix.Forward, worldMatrix.Up);

                var blockList = new List<IMySlimBlock>();
                grid.GetBlocks(blockList);
                var currentBlockCount = blockList.Count;
                if (currentBlockCount > element.initialBlockCount)
                {
                    element.initialBlockCount = currentBlockCount;
                }
                var healthPercent = (float)currentBlockCount / element.initialBlockCount;

                Writer.WriteLine($"grid,{grid.CustomName},{owner?.DisplayName ?? "Unowned"},{factionName},{factionColor},{grid.EntityId},{SmallDouble(healthPercent)},{SmallVector3D(position)},{SmallQuaternion(rotation)}");
            });
            Writer.Flush();
        }

        public string SmallQuaternion(Quaternion q)
        {
            return
                $"{SmallDouble(q.X)} {SmallDouble(q.Y)} {SmallDouble(q.Z)} {SmallDouble(q.W)}";
        }
        public string SmallVector3D(Vector3D v)
        {
            
            return $"{SmallDouble(v.X)} {SmallDouble(v.Y)} {SmallDouble(v.Z)}";
        }
        public string SmallDouble(double value)
        {
            const int decimalPlaces = 2;
            return value.ToString($"F{decimalPlaces}");
        }

        public void HandleMessage(ulong sender, string messageText, ref bool sendToOthers)
        {
            if (!messageText.StartsWith(CommandPrefix)) return;
            sendToOthers = false;

            var args = messageText.Split(' ');

            if (args.Length != 2)
            {
                return;

            }

            switch (args[1])
            {
                case "start": Start();
                    break;
                case "stop": Stop();
                    break;
                default:
                {
                    var error = $"[{nameof(ScCoordWriter)}] Unknown command '{args[1]}'";
                    MyLog.Default.WriteLine(error);
                    MyAPIGateway.Utilities.ShowMessage($"[{nameof(ScCoordWriter)}]", error);
                    MyAPIGateway.Utilities.ShowMessage($"[{nameof(ScCoordWriter)}]", Usage);
                } break;
            }
        }

        public void ReceivedPacket(ushort channelId, byte[] data, ulong steamSenderId, bool isSenderServer)
        {
            if (data != null && data.Length == 1)
            {
                Recording = data[0] == 1;
                if (Recording)
                {
                    Start();
                }
                else
                {
                    Stop();
                }
            }
        }

        private string GetFactionName(IMyIdentity player)
        {
            if (player == null) return "Unowned";
            IMyFaction playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
            return playerFaction != null ? playerFaction.Name : "Unowned";
        }

        private string GetFactionColor(IMyIdentity owner)
        {
            if (owner == null) return SmallVector3D(Vector3D.Zero);

            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner.IdentityId);
            if (faction != null)
            {
                // Example, replace with actual way to get faction color if available

                return SmallVector3D(faction.CustomColor); 
            }
            return "None"; // Default color if no faction or no color defined
        }


        public IMyIdentity GetGridOwner(IMyCubeGrid grid)
        {
            IMyIdentity owner = null;
            if (grid.BigOwners.Count > 0)
            {
                var identities = new List<IMyIdentity>();
                MyAPIGateway.Players.GetAllIdentites(identities, id => id.IdentityId == grid.BigOwners[0]);
                if (identities.Count > 0)
                {
                    owner = identities[0];
                }
            }
            return owner;
        }
    }
}