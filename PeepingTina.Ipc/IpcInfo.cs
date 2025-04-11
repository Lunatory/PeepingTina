using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using PeepingTina.Ipc.From;
using PeepingTina.Ipc.To;

namespace PeepingTina.Ipc {
    public static class IpcInfo {
        public const string FromRegistrationName = "PeepingTina.From";
        public const string ToRegistrationName = "PeepingTina.To";

        public static ICallGateProvider<IToMessage, object> GetProvider(IDalamudPluginInterface @interface) {
            return @interface.GetIpcProvider<IToMessage, object>(ToRegistrationName);
        }

        public static ICallGateSubscriber<IFromMessage, object> GetSubscriber(IDalamudPluginInterface @interface) {
            return @interface.GetIpcSubscriber<IFromMessage, object>(FromRegistrationName);
        }
    }
}
