using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Ipc;
using PeepingTom.Ipc;
using PeepingTom.Ipc.From;
using PeepingTom.Ipc.To;

namespace PeepingTom {
    internal class IpcManager : IDisposable {
        private Plugin Plugin { get; }

        private ICallGateProvider<IFromMessage, object> Provider { get; }
        private ICallGateSubscriber<IToMessage, object> Subscriber { get; }

        internal IpcManager(Plugin plugin) {
            Plugin = plugin;

            Provider = Service.Interface.GetIpcProvider<IFromMessage, object>(IpcInfo.FromRegistrationName);
            Subscriber = Service.Interface.GetIpcSubscriber<IToMessage, object>(IpcInfo.ToRegistrationName);

            Subscriber.Subscribe(ReceiveMessage);
        }

        public void Dispose() {
            Subscriber.Unsubscribe(ReceiveMessage);
        }

        internal void SendAllTargeters() {
            var targeters = new List<(Targeter, bool)>();
            targeters.AddRange(Plugin.Watcher.CurrentTargeters.Select(t => (t, true)));
            targeters.AddRange(Plugin.Watcher.PreviousTargeters.Select(t => (t, false)));

            Provider.SendMessage(new AllTargetersMessage(targeters));
        }

        internal void SendNewTargeter(Targeter targeter) {
            Provider.SendMessage(new NewTargeterMessage(targeter));
        }

        internal void SendStoppedTargeting(Targeter targeter) {
            Provider.SendMessage(new StoppedTargetingMessage(targeter));
        }

        private void ReceiveMessage(IToMessage message) {
            switch (message) {
                case RequestTargetersMessage: {
                    SendAllTargeters();
                    break;
                }
            }
        }
    }
}
