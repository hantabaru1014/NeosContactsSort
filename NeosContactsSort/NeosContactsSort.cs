using SkyFrost.Base;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosContactsSort
{
    public class NeosContactsSort : ResoniteMod
    {
        public override string Name => "NeosContactsSort";
        public override string Author => "hantabaru1014";
        public override string Version => "3.0.2";
        public override string Link => "https://github.com/hantabaru1014/NeosContactsSort";

        private static string BOT_USER_ID = "U-Resonite";

        public static ModConfiguration? config;

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<List<string>> FavoriteUsers = new ModConfigurationKey<List<string>>("FavoriteUsers", " Favorite Users", () => new List<string>());
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> ShowBotOnTop = new ModConfigurationKey<bool>("Show Resonite Bot on top", "ShowBotOnTop", () => true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> ShowMeOnTop = new ModConfigurationKey<bool>("Show your account on top", "ShowMeOnTop", () => true);

        public override void OnEngineInit()
        {
            config = GetConfiguration();

            Harmony harmony = new Harmony("net.hantabaru1014.NeosContactsSort");
            harmony.PatchAll();
        }

        [HarmonyPatch]
        private static class HarmonyPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(ContactsDialog), "OnCommonUpdate")]
            public static void ContactsDialogOnCommonUpdatePrefix(ref bool ___sortList, out bool __state)
            {
                // steal the sortList bool's value, and force it to false from Neos's perspective
                __state = ___sortList;
                ___sortList = false;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(ContactsDialog), "OnCommonUpdate")]
            public static void ContactsDialogOnCommonUpdatePostfix(bool __state, SyncRef<Slot> ____listRoot)
            {
                // if Neos would have sorted (but we prevented it)
                if (__state)
                {
                    // we need to sort
                    ____listRoot.Target.SortChildren((slot1, slot2) =>
                    {
                        ContactItem? component1 = slot1.GetComponent<ContactItem>();
                        ContactItem? component2 = slot2.GetComponent<ContactItem>();
                        Contact? contact1 = component1?.Contact;
                        Contact? contact2 = component2?.Contact;

                        // nulls go last
                        if (contact1 != null && contact2 == null) return -1;
                        if (contact1 == null && contact2 != null) return 1;
                        if (contact1 == null && contact2 == null) return 0;

                        // Contacts with unread messages come first
                        int messageComparison = -component1!.HasMessages.CompareTo(component2!.HasMessages);
                        if (messageComparison != 0) return messageComparison;

                        // resonite bot comes first
                        if (config!.GetValue(ShowBotOnTop))
                        {
                            if (contact1!.ContactUserId == BOT_USER_ID && contact2!.ContactUserId != BOT_USER_ID) return -1;
                            if (contact2!.ContactUserId == BOT_USER_ID && contact1!.ContactUserId != BOT_USER_ID) return 1;
                        }
                        
                        // my account comes first
                        if (config!.GetValue(ShowMeOnTop))
                        {
                            if (contact1!.ContactUserId == Engine.Current.Cloud.CurrentUserID && contact2!.ContactUserId != Engine.Current.Cloud.CurrentUserID) return -1;
                            if (contact2!.ContactUserId == Engine.Current.Cloud.CurrentUserID && contact1!.ContactUserId != Engine.Current.Cloud.CurrentUserID) return 1;
                        }

                        // favorite users comes second
                        var favUsers = config!.GetValue(FavoriteUsers);
                        if (favUsers!.Contains(contact1!.ContactUserId) && !favUsers.Contains(contact2!.ContactUserId)) return -1;
                        if (!favUsers.Contains(contact1.ContactUserId) && favUsers.Contains(contact2!.ContactUserId)) return 1;

                        // sort by online status
                        int onlineStatusOrder = GetOrderNumber(contact1!, component1.Data).CompareTo(GetOrderNumber(contact2!, component2.Data));
                        if (onlineStatusOrder != 0) return onlineStatusOrder;

                        // sort by name
                        return string.Compare(contact1!.ContactUsername, contact2!.ContactUsername, StringComparison.CurrentCultureIgnoreCase);
                    });
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(ContactsDialog), "UpdateSelectedContact")]
            public static void ContactsDialogUpdateSelectedContactPostfix(ContactsDialog __instance, UIBuilder ___actionsUi)
            {
                if (__instance.SelectedContact == null
                    || __instance.SelectedContact.ContactUserId == BOT_USER_ID
                    || __instance.SelectedContact.ContactUserId == Engine.Current.Cloud.CurrentUserID) return;
                UIBuilder actionsUIBuilder = ___actionsUi;
                var favUsers = config!.GetValue(FavoriteUsers)!;
                if (favUsers.Contains(__instance.SelectedContactId))
                {
                    actionsUIBuilder.Button("★").LocalPressed += (IButton button, ButtonEventData eventData) =>
                    {
                        favUsers.Remove(__instance.SelectedContactId);
                        config.Set(FavoriteUsers, favUsers);
                        UpdateContactsDialog(__instance);
                    };
                }
                else
                {
                    actionsUIBuilder.Button("☆").LocalPressed += (IButton button, ButtonEventData eventData) =>
                    {
                        favUsers.Add(__instance.SelectedContactId);
                        config.Set(FavoriteUsers, favUsers);
                        UpdateContactsDialog(__instance);
                    };
                }
                var favBtnSlot = actionsUIBuilder.Root.Children.Last();
                favBtnSlot.OrderOffset = -1;
                var el = favBtnSlot.GetComponent<LayoutElement>();
                el.PreferredWidth.Value = 30f;
            }
        }

        // lower numbers appear earlier in the list
        private static int GetOrderNumber(Contact contact, ContactData? data)
        {
            if (contact.ContactStatus == ContactStatus.Requested) // received requests
                return 0;
            if (contact.IsPartiallyMigrated) // Not Migrated
                return 10;

            var currentStatus = data?.CurrentStatus;
            return currentStatus?.OnlineStatus switch
            {
                OnlineStatus.Sociable => 1,
                OnlineStatus.Online => 2,
                OnlineStatus.Away => 3,
                OnlineStatus.Busy => 4,
                // Offline or Invisible
                _ => currentStatus?.SessionType switch
                {
                    UserSessionType.Headless or UserSessionType.ChatClient or UserSessionType.Bot => 5,
                    _ => 6, // really Offline or Invisible
                }
            };
        }

        private static void UpdateContactsDialog(ContactsDialog instance)
        {
            AccessTools.Method(typeof(ContactsDialog), "UpdateSelectedContact").Invoke(instance, null);
            AccessTools.Method(typeof(ContactsDialog), "OnCommonUpdate").Invoke(instance, null);
        }
    }
}
