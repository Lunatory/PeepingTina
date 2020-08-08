﻿using Dalamud.Game.Chat;
using Dalamud.Game.Chat.SeStringHandling;
using Dalamud.Game.Chat.SeStringHandling.Payloads;
using Dalamud.Game.ClientState.Actors.Types;
using Dalamud.Game.Internal;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;

namespace PeepingTom {
    class TargetWatcher : IDisposable {
        private readonly PeepingTomPlugin plugin;

        private long soundLastPlayed = 0;
        private int lastTargetAmount = 0;

        private volatile bool stop = false;
        private volatile bool needsUpdate = true;
        private Thread thread;

        private readonly object dataMutex = new object();
        private TargetThreadData data;

        private readonly Mutex currentMutex = new Mutex();
        private Targeter[] current = Array.Empty<Targeter>();
        public IReadOnlyCollection<Targeter> CurrentTargeters {
            get {
                this.currentMutex.WaitOne();
                Targeter[] current = this.current.ToArray();
                this.currentMutex.ReleaseMutex();
                return current;
            }
        }

        private readonly Mutex previousMutex = new Mutex();
        private readonly List<Targeter> previousTargeters = new List<Targeter>();
        public IReadOnlyCollection<Targeter> PreviousTargeters {
            get {
                this.previousMutex.WaitOne();
                Targeter[] previous = this.previousTargeters.ToArray();
                this.previousMutex.ReleaseMutex();
                return previous;
            }
        }

        public TargetWatcher(PeepingTomPlugin plugin) {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin), "PeepingTomPlugin cannot be null");
        }

        public void ClearPrevious() {
            this.previousMutex.WaitOne();
            this.previousTargeters.Clear();
            this.previousMutex.ReleaseMutex();
        }

        public void StartThread() {
            this.thread = new Thread(new ThreadStart(() => {
                while (!this.stop) {
                    this.Update();
                    this.needsUpdate = true;
                    Thread.Sleep(this.plugin.Config.PollFrequency);
                }
            }));
            this.thread.Start();
        }

        public void WaitStopThread() {
            this.stop = true;
            this.thread?.Join();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "delegate")]
        public void OnFrameworkUpdate(Framework framework) {
            if (!this.needsUpdate) {
                return;
            }

            lock (this.dataMutex) {
                this.data = new TargetThreadData(this.plugin.Interface);
            }
            this.needsUpdate = false;
        }

        private void Update() {
            lock (this.dataMutex) {
                if (this.data == null) {
                    return;
                }

                PlayerCharacter player = this.data.localPlayer;
                if (player == null) {
                    return;
                }

                // block until lease
                this.currentMutex.WaitOne();

                // get targeters and set a copy so we can release the mutex faster
                Targeter[] current = this.GetTargeting(this.data.actors, player);
                this.current = (Targeter[])current.Clone();

                // release
                this.currentMutex.ReleaseMutex();
            }

            this.HandleHistory(current);

            // play sound if necessary
            if (this.CanPlaySound()) {
                this.soundLastPlayed = Stopwatch.GetTimestamp();
                this.PlaySound();
            }
            this.lastTargetAmount = this.current.Length;
        }

        private void HandleHistory(Targeter[] targeting) {
            if (!this.plugin.Config.KeepHistory || (!this.plugin.Config.HistoryWhenClosed && !this.plugin.Ui.Visible)) {
                return;
            }

            this.previousMutex.WaitOne();

            foreach (Targeter targeter in targeting) {
                // add the targeter to the previous list
                if (this.previousTargeters.Any(old => old.ActorId == targeter.ActorId)) {
                    this.previousTargeters.RemoveAll(old => old.ActorId == targeter.ActorId);
                }
                this.previousTargeters.Insert(0, targeter);
            }

            // only keep the configured number of previous targeters (ignoring ones that are currently targeting)
            while (this.previousTargeters.Where(old => targeting.All(actor => actor.ActorId != old.ActorId)).Count() > this.plugin.Config.NumHistory) {
                this.previousTargeters.RemoveAt(this.previousTargeters.Count - 1);
            }

            this.previousMutex.ReleaseMutex();
        }

        private Targeter[] GetTargeting(Actor[] actors, Actor player) {
            return actors
                .Where(actor => actor.TargetActorID == player.ActorId && actor is PlayerCharacter)
                .Select(actor => actor as PlayerCharacter)
                .Where(actor => this.plugin.Config.LogParty || this.plugin.Interface.ClientState.PartyList.All(member => member.Actor?.ActorId != actor.ActorId))
                .Where(actor => this.plugin.Config.LogAlliance || !this.InAlliance(actor))
                .Where(actor => this.plugin.Config.LogInCombat || !this.InCombat(actor))
                .Where(actor => this.plugin.Config.LogSelf || actor.ActorId != player.ActorId)
                .Select(actor => new Targeter(actor))
                .ToArray();
        }

        private byte GetStatus(Actor actor) {
            IntPtr statusPtr = this.plugin.Interface.TargetModuleScanner.ResolveRelativeAddress(actor.Address, 0x1901);
            return Marshal.ReadByte(statusPtr);
        }

        private bool InCombat(Actor actor) {
            return (GetStatus(actor) & 2) > 0;
        }

        private bool InAlliance(Actor actor) {
            return (GetStatus(actor) & 32) > 0;
        }

        private bool CanPlaySound() {
            if (!this.plugin.Config.PlaySoundOnTarget) {
                return false;
            }

            if (this.current.Length <= this.lastTargetAmount) {
                return false;
            }

            if (!this.plugin.Config.PlaySoundWhenClosed && !this.plugin.Ui.Visible) {
                return false;
            }

            if (this.soundLastPlayed == 0) {
                return true;
            }

            long current = Stopwatch.GetTimestamp();
            long diff = current - this.soundLastPlayed;
            // only play every 10 seconds?
            float secs = (float)diff / Stopwatch.Frequency;
            return secs >= this.plugin.Config.SoundCooldown;
        }

        private void PlaySound() {
            SoundPlayer player;
            if (this.plugin.Config.SoundPath == null) {
                player = new SoundPlayer(Properties.Resources.Target);
            } else {
                player = new SoundPlayer(this.plugin.Config.SoundPath);
            }
            using (player) {
                try {
                    player.Play();
                } catch (FileNotFoundException e) {
                    this.SendError($"Could not play sound: {e.Message}");
                } catch (InvalidOperationException e) {
                    this.SendError($"Could not play sound: {e.Message}");
                }
            }
        }

        private void SendError(string message) {
            Payload[] payloads = { new TextPayload($"[Who's Looking] {message}") };
            this.plugin.Interface.Framework.Gui.Chat.PrintChat(new XivChatEntry {
                MessageBytes = new SeString(payloads).Encode(),
                Type = XivChatType.ErrorMessage
            });
        }

        public void Dispose() {
            this.currentMutex.Dispose();
            this.previousMutex.Dispose();
        }
    }

    class TargetThreadData {
        public PlayerCharacter localPlayer;
        public Actor[] actors;

        public TargetThreadData(DalamudPluginInterface pi) {
            this.localPlayer = pi.ClientState.LocalPlayer;
            this.actors = pi.ClientState.Actors.ToArray();
        }
    }
}
