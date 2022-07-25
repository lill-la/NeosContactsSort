//#define DEBUG // if true do a lot of debug spam

using BaseX;
using CloudX.Shared;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using NeosModLoader;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosContactsSort
{
    public class NeosContactsSort : NeosMod
    {
        public override string Name => "NeosContactsSort";
        public override string Author => "hantabaru1014";
        public override string Version => "2.0.0";
        public override string Link => "https://github.com/hantabaru1014/NeosContactsSort";

        public static ModConfiguration? config;

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<List<string>> FavoriteUsers = new ModConfigurationKey<List<string>>("FavoriteUsers", " Favorite Users", () => new List<string>());

        public override void OnEngineInit()
        {
#if DEBUG
            Warn($"Extremely verbose debug logging is enabled in this build. This probably means runtime messed up and gave you a debug build.");
#endif
            config = GetConfiguration();

            Harmony harmony = new Harmony("net.hantabaru1014.NeosContactsSort");
            harmony.PatchAll();
        }

        [HarmonyPatch]
        private static class HarmonyPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(FriendsDialog), "OnCommonUpdate", new Type[] { })]
            public static void FriendsDialogOnCommonUpdatePrefix(ref bool ___sortList, out bool __state)
            {
                // steal the sortList bool's value, and force it to false from Neos's perspective
                __state = ___sortList;
                ___sortList = false;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(FriendsDialog), "OnCommonUpdate", new Type[] { })]
            public static void FriendsDialogOnCommonUpdatePostfix(bool __state, SyncRef<Slot> ____listRoot)
            {
                // if Neos would have sorted (but we prevented it)
                if (__state)
                {
                    // we need to sort
                    ____listRoot.Target.SortChildren((slot1, slot2) =>
                    {
                        FriendItem? component1 = slot1.GetComponent<FriendItem>();
                        FriendItem? component2 = slot2.GetComponent<FriendItem>();
                        Friend? friend1 = component1?.Friend;
                        Friend? friend2 = component2?.Friend;

                        // nulls go last
                        if (friend1 != null && friend2 == null) return -1;
                        if (friend1 == null && friend2 != null) return 1;
                        if (friend1 == null && friend2 == null) return 0;

                        // friends with unread messages come first
                        int messageComparison = -component1!.HasMessages.CompareTo(component2!.HasMessages);
                        if (messageComparison != 0) return messageComparison;

                        // favorite users comes second
                        var favUsers = config!.GetValue(FavoriteUsers);
                        if (favUsers!.Contains(friend1!.FriendUserId) && !favUsers.Contains(friend2!.FriendUserId)) return -1;
                        if (!favUsers.Contains(friend1.FriendUserId) && favUsers.Contains(friend2!.FriendUserId)) return 1;

                        // sort by online status
                        int onlineStatusOrder = GetOrderNumber(friend1!).CompareTo(GetOrderNumber(friend2!));
                        if (onlineStatusOrder != 0) return onlineStatusOrder;

                        // neos bot comes first
                        if (friend1!.FriendUserId == "U-Neos" && friend2!.FriendUserId != "U-Neos") return -1;
                        if (friend2!.FriendUserId == "U-Neos" && friend1!.FriendUserId != "U-Neos") return 1;

                        // sort by name
                        return string.Compare(friend1!.FriendUsername, friend2!.FriendUsername, StringComparison.CurrentCultureIgnoreCase);
                    });

#if DEBUG
                    Debug("BIG FRIEND DEBUG:");
                    foreach (Slot slot in ____listRoot.Target.Children)
                    {
                        FriendItem? component = slot.GetComponent<FriendItem>();
                        Friend? friend = component?.Friend;
                        if (friend != null)
                        {
                            Debug($"  {GetOrderNumber(friend)}: \"{friend.FriendUsername}\" status={friend.FriendStatus} online={friend.UserStatus?.OnlineStatus} incoming={friend.IsAccepted}");
                        }
                    }
#endif
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(NeosUIStyle), nameof(NeosUIStyle.GetStatusColor), new Type[] { typeof(Friend), typeof(Engine) })]
            public static void NeosUIStyleGetStatusColorPostfix(Friend friend, Engine engine, ref color __result)
            {
                OnlineStatus onlineStatus = friend.UserStatus?.OnlineStatus ?? OnlineStatus.Offline;
                if (onlineStatus == OnlineStatus.Offline && friend.FriendStatus == FriendStatus.Accepted && !friend.IsAccepted)
                {
                    __result = color.Yellow;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(FriendsDialog), "UpdateSelectedFriend")]
            public static void FriendsDialogUpdateSelectedFriendPostfix(FriendsDialog __instance, UIBuilder ___actionsUi)
            {
                if (__instance.SelectedFriend == null || __instance.SelectedFriend.FriendUserId == "U-Neos") return;
                UIBuilder actionsUIBuilder = ___actionsUi;
                var favUsers = config!.GetValue(FavoriteUsers)!;
                if (favUsers.Contains(__instance.SelectedFriendId))
                {
                    actionsUIBuilder.Button("★").LocalPressed += (IButton button, ButtonEventData eventData) =>
                    {
                        favUsers.Remove(__instance.SelectedFriendId);
                        config.Set(FavoriteUsers, favUsers);
                        UpdateFriendsDialog(__instance);
                    };
                }
                else
                {
                    actionsUIBuilder.Button("☆").LocalPressed += (IButton button, ButtonEventData eventData) =>
                    {
                        favUsers.Add(__instance.SelectedFriendId);
                        config.Set(FavoriteUsers, favUsers);
                        UpdateFriendsDialog(__instance);
                    };
                }
                var favBtnSlot = actionsUIBuilder.Root.Children.Last();
                favBtnSlot.OrderOffset = -1;
                var el = favBtnSlot.GetComponent<LayoutElement>();
                el.PreferredWidth.Value = 30f;
            }
        }

        // lower numbers appear earlier in the list
        private static int GetOrderNumber(Friend friend)
        {
            if (friend.FriendStatus == FriendStatus.Requested) // received requests
                return 0;
            OnlineStatus status = friend.UserStatus?.OnlineStatus ?? OnlineStatus.Offline;
            switch (status)
            {
                case OnlineStatus.Online:
                    return 1;
                case OnlineStatus.Away:
                    return 2;
                case OnlineStatus.Busy:
                    return 3;
                default: // Offline or Invisible
                    if (friend.FriendStatus == FriendStatus.Accepted && !friend.IsAccepted)
                    { // sent requests
                        return 4;
                    }
                    else if (friend.FriendStatus != FriendStatus.SearchResult)
                    { // offline or invisible
                        return 5;
                        // unsure how people with no relation, ignored, or blocked will appear... but they'll end up here too
                    }
                    else
                    { // search results always come last
                        return 6;
                    }
            }
        }

        private static void UpdateFriendsDialog(FriendsDialog instance)
        {
            AccessTools.Method(typeof(FriendsDialog), "UpdateSelectedFriend").Invoke(instance, null);
            AccessTools.Method(typeof(FriendsDialog), "OnCommonUpdate").Invoke(instance, null);
        }
    }
}
