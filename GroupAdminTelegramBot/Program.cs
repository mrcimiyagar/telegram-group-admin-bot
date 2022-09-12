using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputMessageContents;
using Telegram.Bot.Types.ReplyMarkups;

namespace GroupAdminTelegramBot
{
    public class Program
    {
        private static DatabaseManager dbManager;

        private static Dictionary<long, KeyValuePair<int, int>> botCircleGroups;
        private static Dictionary<long, SilenceThread> groupsSilenceThread;
        private static Dictionary<long, HashSet<long>> linkAllowedUsers;
        private static Dictionary<long, string> groupsInviteLinks;

        private static Dictionary<string, KeyValuePair<long, string>> invitedsToInviters;
        private static Dictionary<string, int> allowPendingUsers;
        private static Dictionary<long, long> usersGroupInviteRequests;
        private static Dictionary<string, Election> startPendingElections;
        private static Dictionary<int, Election> startedElections;

        private static Dictionary<long, long> upgradeBusyAdmins;
        private static Dictionary<long, HashSet<string>> upgradePendingUsernames;

        public delegate void CallBotToDestructMessage(int id, long groupId, int messageId);

        private static User botUser;

        private static TelegramBotClient botClient;
        private const string botToken = "-";

        private const string createCardCommand = "show_group_invite_link ";
        private const string inviterGroupSetCommand = "set_user_group_invite_request ";
        private const string silenceCommand = "/silence_time ";
        private const string electionCreateCommand = "/create_election";
        private const string electionStartCommand = "/start_election";
        private const string increaseVoteCommand = "increase_vote ";
        private const string setInviteLinkCommand = "/set_invite_link ";
        private const string showGroupScoresCommand = "show_group_scores ";
        private const string upgradeUserCommand = "/upgrade";
        private const string switchToUpgradCommand = "/upgrading ";

        private static Regex linkParser = new Regex(@"\b(?:https?://|www\.)\S+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static long updateCounter = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("configuring data sources...");

            configDataSources();

            Console.WriteLine("connecting to telegram server...");

            startTelegramBot();

            Console.WriteLine("Purifying datasource...");

            purifyDataSource();

            botClient.StartReceiving();

            Console.WriteLine("telegram bot started.");

            Console.WriteLine("groups count : " + botCircleGroups.Count);

            while (true)
            {
                Console.ReadLine();
            }
        }

        private static void configDataSources()
        {
            dbManager = new DatabaseManager();

            Tuple<Dictionary<long, string>, Dictionary<long, KeyValuePair<int, int>>> groupsInfo = dbManager.getBotGroupsList();
            groupsInviteLinks = groupsInfo.Item1;
            botCircleGroups = groupsInfo.Item2;

            groupsSilenceThread = new Dictionary<long, SilenceThread>();
            
            foreach (long groupId in botCircleGroups.Keys)
            {
                KeyValuePair<int, int> silenceTime = botCircleGroups[groupId];
                groupsSilenceThread.Add(groupId, new SilenceThread(groupId, silenceTime.Key, silenceTime.Value));
            }

            linkAllowedUsers = dbManager.getAllowedUsersList(new HashSet<long>(botCircleGroups.Keys));
            allowPendingUsers = dbManager.getAllowPendingUsers(new HashSet<long>(botCircleGroups.Keys));
            invitedsToInviters = dbManager.getOpenedInvitations(new HashSet<long>(botCircleGroups.Keys));
            upgradePendingUsernames = dbManager.getUpgradePendingUsernames(new HashSet<long>(botCircleGroups.Keys));
            startedElections = dbManager.getElectionsList();

            HashSet<Tuple<int, long, int>> desMsgLeft = dbManager.getDestructableMessages();

            foreach (Tuple<int, long, int> desMsg in desMsgLeft)
            {
                NotifyBotDestructingMessage(desMsg.Item1, desMsg.Item2, desMsg.Item3);
            }

            usersGroupInviteRequests = new Dictionary<long, long>();
            startPendingElections = new Dictionary<string, Election>();
            upgradeBusyAdmins = new Dictionary<long, long>();
            
        }

        private async static void startTelegramBot()
        {
            botClient = new TelegramBotClient(botToken);
            botUser = await botClient.GetMeAsync();
            Console.Title = botUser.FirstName;

            botClient.OnUpdate += OnUpdateReceived;
        }

        private static void purifyDataSource()
        {
            HashSet<long> deletedGroups = new HashSet<long>();

            foreach (long groupId in botCircleGroups.Keys)
            {
                try {
                    Chat group = botClient.GetChatAsync(groupId).Result;

                    if (group != null)
                    {
                        ChatMember[] admins = botClient.GetChatAdministratorsAsync(groupId).Result;

                        if (admins.Length == 1 && admins[0].User.Id == botUser.Id)
                        {
                            dbManager.removeGroup(groupId);
                            botClient.LeaveChatAsync(groupId);
                            deletedGroups.Add(groupId);
                        }
                    }
                    else
                    {
                        dbManager.removeGroup(groupId);
                        botClient.LeaveChatAsync(groupId);
                        deletedGroups.Add(groupId);
                    }
                }
                catch (Exception)
                {
                    try
                    {
                        botClient.LeaveChatAsync(groupId);
                    }
                    catch(Exception)
                    {

                    }

                    dbManager.removeGroup(groupId);
                    deletedGroups.Add(groupId);
                }
            }

            foreach (long deletedGroupId in deletedGroups)
            {
                dbManager.removeGroup(deletedGroupId);
                botCircleGroups.Remove(deletedGroupId);
                groupsSilenceThread.Remove(deletedGroupId);
                groupsInviteLinks.Remove(deletedGroupId);
                linkAllowedUsers.Remove(deletedGroupId);
            }
        }
        
        private static void OnUpdateReceived(object sender, UpdateEventArgs uea)
        {
            Console.WriteLine("Update Received " + updateCounter++);

            new Thread(() =>
            {
                try
                {
                    if (uea.Update.Type == Telegram.Bot.Types.Enums.UpdateType.MessageUpdate)
                    {
                        if (uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.ServiceMessage)
                        {
                            if (uea.Update.Message.NewChatMembers != null)
                            {
                                if (uea.Update.Message.NewChatMembers.Length > 0)
                                {
                                    foreach (User cm in uea.Update.Message.NewChatMembers)
                                    {
                                        if (cm.Id.Identifier == botUser.Id.Identifier)
                                        {
                                            dbManager.addGroup(uea.Update.Message.Chat.Id.Identifier);
                                            botCircleGroups.Add(uea.Update.Message.Chat.Id.Identifier, new KeyValuePair<int, int>(-1, -1));
                                            groupsSilenceThread.Add(uea.Update.Message.Chat.Id.Identifier, new SilenceThread(uea.Update.Message.Chat.Id.Identifier, -1, -1));
                                            groupsInviteLinks.Add(uea.Update.Message.Chat.Id.Identifier, "not_set");
                                            linkAllowedUsers.Add(uea.Update.Message.Chat.Id.Identifier, new HashSet<long>());
                                        }
                                        else
                                        {
                                            notifyNewUserJointGroup(uea, cm);
                                        }
                                    }
                                }
                            }
                            else if (uea.Update.Message.LeftChatMember != null)
                            {
                                if (uea.Update.Message.LeftChatMember.Id.Identifier == botUser.Id.Identifier)
                                {
                                    dbManager.removeGroup(uea.Update.Message.Chat.Id.Identifier);
                                    botCircleGroups.Remove(uea.Update.Message.Chat.Id.Identifier);
                                    groupsSilenceThread.Remove(uea.Update.Message.Chat.Id.Identifier);
                                    groupsInviteLinks.Remove(uea.Update.Message.Chat.Id.Identifier);
                                    linkAllowedUsers.Remove(uea.Update.Message.Chat.Id.Identifier);
                                }
                                else
                                {
                                    ChatMember[] admins = botClient.GetChatAdministratorsAsync(uea.Update.Message.Chat.Id.Identifier).Result;

                                    if (admins.Length == 1 && admins[0].User.Id == botUser.Id)
                                    {
                                        dbManager.removeGroup(uea.Update.Message.Chat.Id.Identifier);
                                        botClient.LeaveChatAsync(uea.Update.Message.Chat.Id.Identifier);
                                        botCircleGroups.Remove(uea.Update.Message.Chat.Id.Identifier);
                                        groupsSilenceThread.Remove(uea.Update.Message.Chat.Id.Identifier);
                                        groupsInviteLinks.Remove(uea.Update.Message.Chat.Id.Identifier);
                                        linkAllowedUsers.Remove(uea.Update.Message.Chat.Id.Identifier);
                                    }
                                }
                            }
                        }
                        else if (uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.TextMessage
                            || uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.AudioMessage
                            || uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.DocumentMessage
                            || uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.PhotoMessage
                            || uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.VideoMessage
                            || uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.VoiceMessage)
                        {
                            SilenceThread silenceThread;

                            // message sent in a bot circle group
                            if (groupsSilenceThread.TryGetValue(uea.Update.Message.Chat.Id.Identifier, out silenceThread))
                            {
                                bool isSenderAdmin = false;

                                ChatMember[] groupAdmins = botClient.GetChatAdministratorsAsync(uea.Update.Message.Chat.Id.Identifier).Result;

                                foreach (ChatMember cm in groupAdmins)
                                {
                                    if (cm.User.Id.Identifier == uea.Update.Message.From.Id.Identifier)
                                    {
                                        isSenderAdmin = true;
                                        break;
                                    }
                                }

                                if (!isSenderAdmin)
                                {
                                    if (silenceThread.IsSet && silenceThread.IsSilent)
                                    {
                                        notifyMemberActedInSilenceTime(uea, silenceThread.StartTime, silenceThread.FinishTime);
                                        return;
                                    }
                                    
                                    if (uea.Update.Message.Entities != null && uea.Update.Message.Entities.Count > 0)
                                    {
                                        notifyEntitiesSentInGroup(uea);
                                        return;
                                    }

                                    if (uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.TextMessage && uea.Update.Message.Text != null)
                                    {
                                        if (linkParser.Matches(uea.Update.Message.Text).Count > 0)
                                        {
                                            notifyNonAdminSentLinkToGroup(uea);
                                            return;
                                        }
                                        else
                                        {
                                            int signIndex = uea.Update.Message.Text.IndexOf('@');

                                            string text = uea.Update.Message.Text;

                                            while (signIndex >= 0)
                                            {
                                                int counter = signIndex + 1;

                                                while (counter < text.Length && (Regex.IsMatch(text[counter].ToString(), "[a-zA-Z0-9]") || text[counter] == '_'))
                                                {
                                                    counter++;
                                                }

                                                if (counter > signIndex)
                                                {
                                                    string username = text.Substring(signIndex, counter - signIndex);

                                                    if (checkGroupExistance(text.Substring(signIndex, counter - signIndex)))
                                                    {
                                                        notifyNonAdminSentLinkToGroup(uea);
                                                        break;
                                                    }

                                                    text = text.Replace(username, " ");
                                                }
                                                else
                                                {
                                                    if (signIndex + 1 < text.Length)
                                                    {
                                                        text = text.Substring(0, signIndex) + " " + text.Substring(signIndex + 1);
                                                    }
                                                    else
                                                    {
                                                        text = text.Substring(0, signIndex) + " ";
                                                    }
                                                }

                                                signIndex = text.IndexOf('@');
                                            }

                                            return;
                                        }
                                        
                                    }
                                    else if (uea.Update.Message.Caption != null)
                                    {
                                        if (linkParser.Matches(uea.Update.Message.Caption).Count > 0)
                                        {
                                            notifyNonAdminSentLinkToGroup(uea);
                                            return;
                                        }
                                        else
                                        {
                                            int signIndex = uea.Update.Message.Caption.IndexOf('@');

                                            string caption = uea.Update.Message.Caption;

                                            while (signIndex >= 0)
                                            {
                                                int counter = signIndex + 1;

                                                while (counter < caption.Length && (Regex.IsMatch(caption[counter].ToString(), "[a-zA-Z0-9]") || caption[counter] == '_'))
                                                {
                                                    counter++;
                                                }

                                                if (counter > signIndex)
                                                {
                                                    string username = caption.Substring(signIndex, counter - signIndex);

                                                    if (checkGroupExistance(caption.Substring(signIndex, counter - signIndex)))
                                                    {
                                                        notifyNonAdminSentLinkToGroup(uea);
                                                        break;
                                                    }

                                                    caption = caption.Replace(username, " ");
                                                }
                                                else
                                                {
                                                    if (signIndex + 1 < caption.Length)
                                                    {
                                                        caption = caption.Substring(0, signIndex) + " " + caption.Substring(signIndex + 1);
                                                    }
                                                    else
                                                    {
                                                        caption = caption.Substring(0, signIndex) + " ";
                                                    }
                                                }

                                                signIndex = caption.IndexOf('@');
                                            }

                                            return;
                                        }
                                    }

                                    if (uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.TextMessage &&
                                        uea.Update.Message.Text != null)
                                    {
                                        if (uea.Update.Message.Text == "/bot_help")
                                        {
                                            notifyUserRequestedBotHelpText(uea);
                                        }
                                        else if (uea.Update.Message.Text == "/bot_invitation_tutorials")
                                        {
                                            notifyUserRequestedBotInviteSystemHelpText(uea);
                                        }

                                        return;
                                    }
                                }
                                else
                                {
                                    if (uea.Update.Message.Text != null)
                                    {
                                        Election election;

                                        if (startPendingElections.TryGetValue(uea.Update.Message.Chat.Id.Identifier + " " + uea.Update.Message.From.Id.Identifier, out election))
                                        {
                                            if (uea.Update.Message.Text == electionStartCommand)
                                            {
                                                notifyStartingElection(uea, election);
                                                return;
                                            }
                                            else
                                            {
                                                notifyDevelopingElection(uea, election);
                                                return;
                                            }
                                        }

                                        if (uea.Update.Message.Text.StartsWith(silenceCommand))
                                        {
                                            notifyAdminSettingSilenceTime(uea);
                                            return;
                                        }

                                        if (uea.Update.Message.Text == electionCreateCommand)
                                        {
                                            notifyAdminCreatingElection(uea);
                                            return;
                                        }

                                        if (uea.Update.Message.Text.StartsWith(setInviteLinkCommand))
                                        {
                                            notifyAdminSettingGroupInviteLink(uea);
                                            return;
                                        }

                                        if (uea.Update.Message.Text == "/bot_help")
                                        {
                                            notifyUserRequestedBotHelpText(uea);
                                        }
                                        else if (uea.Update.Message.Text == "/bot_invitation_tutorials")
                                        {
                                            notifyUserRequestedBotInviteSystemHelpText(uea);
                                        }

                                        return;
                                    }
                                }
                            }
                            // message sent to private chat
                            else
                            {
                                if (uea.Update.Message.Text != null)
                                {
                                    long tempGroupId;

                                    if (upgradeBusyAdmins.TryGetValue(uea.Update.Message.From.Id.Identifier, out tempGroupId))
                                    {
                                        notifyAdminEnteredUsernameToBeUpgraded(uea, tempGroupId);
                                        return;
                                    }

                                    if (uea.Update.Message.Text == "/start")
                                    {
                                        notifyUserRequestedBotStart(uea);
                                    }
                                    else if (uea.Update.Message.Text == "/invite")
                                    {
                                        notifyUserPreparingInvite(uea);
                                    }
                                    else if (uea.Update.Message.Text == "/stats")
                                    {
                                        notifySendUserStatsInGroups(uea);
                                    }
                                    else if (uea.Update.Message.Text == "/bot_help")
                                    {
                                        notifyUserRequestedBotHelpText(uea);
                                    }
                                    else if (uea.Update.Message.Text == "/bot_invitation_tutorials")
                                    {
                                        notifyUserRequestedBotInviteSystemHelpText(uea);
                                    }
                                    else if (uea.Update.Message.Text == "/panel")
                                    {
                                        notifyAdminRequestingPanel(uea);
                                    }
                                    else if (uea.Update.Message.Text == upgradeUserCommand)
                                    {
                                        if (uea.Update.Message.From.Username == "mohammadi_keyhan" || uea.Update.Message.From.Username == "Roshangaranapp")
                                        {
                                            notifyMainAdminPrepareMemberUpgrade(uea);
                                        }
                                        else
                                        {
                                            notifyAdminPrepareMemberUpgrade(uea);
                                        }
                                    }
                                    else if (uea.Update.Message.Text == "/check_upgrade")
                                    {
                                        notifyMemberCheckingUpgradeAvalability(uea);
                                    }
                                }
                            }
                        }
                    }
                    else if (uea.Update.Type == Telegram.Bot.Types.Enums.UpdateType.InlineQueryUpdate)
                    {
                        if (uea.Update.InlineQuery.Query != null)
                        {
                            if (uea.Update.InlineQuery.Query == "invite")
                            {
                                notifyInlineInviteRequested(uea);
                            }
                        }
                    }
                    else if (uea.Update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQueryUpdate)
                    {
                        if (uea.Update.CallbackQuery != null)
                        {
                            if (uea.Update.CallbackQuery.Data.StartsWith(inviterGroupSetCommand))
                            {
                                notifyInviterGroupSet(uea);
                            }
                            else if (uea.Update.CallbackQuery.Data.StartsWith(createCardCommand))
                            {
                                notifyInvitationOpened(uea);
                            }
                            else if (uea.Update.CallbackQuery.Data.StartsWith(increaseVoteCommand))
                            {
                                notifyUserVotingElection(uea);
                            }
                            else if (uea.Update.CallbackQuery.Data.StartsWith(showGroupScoresCommand))
                            {
                                notifyAdminRequestingUsersScores(uea);
                            }
                            else if (uea.Update.CallbackQuery.Data.StartsWith(switchToUpgradCommand))
                            {
                                notifyAdminPickedGroupToUpgradeMember(uea);
                            }
                        }
                    }
                }
                catch (Exception ignored)
                {
                    Console.WriteLine(ignored.ToString());
                }
            }).Start();
        }

        private static void notifyMainAdminPrepareMemberUpgrade(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotInviteResponse1);

            List<Chat> userGroups = new List<Chat>();

            foreach (long groupId in botCircleGroups.Keys)
            {
                try
                {
                    userGroups.Add(botClient.GetChatAsync(groupId).Result);
                }
                catch (Exception) { }
            }

            if (userGroups.Count > 0)
            {
                InlineKeyboardButton[][] keyboardButtons = new InlineKeyboardButton[userGroups.Count][];

                int counter = 0;

                foreach (Chat group in userGroups)
                {
                    keyboardButtons[counter] = new InlineKeyboardButton[1]
                    {
                        new InlineKeyboardButton(group.Title, switchToUpgradCommand + group.Id.Identifier)
                    };

                    counter++;
                }

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "All Groups :"
                    , Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0, new InlineKeyboardMarkup(keyboardButtons));
            }
        }

        private static void notifyAdminEnteredUsernameToBeUpgraded(UpdateEventArgs uea, long tempGroupId)
        {
            string username = uea.Update.Message.Text;

            if (username.StartsWith("@"))
            {
                lock (upgradePendingUsernames[tempGroupId])
                {
                    if (!upgradePendingUsernames[tempGroupId].Contains(username))
                    {
                        upgradePendingUsernames[tempGroupId].Add(username);
                    }
                }

                dbManager.addUpgradePendingUsername(tempGroupId, username);

                upgradeBusyAdmins.Remove(uea.Update.Message.From.Id.Identifier);

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "username added to upgrade pending list.");
            }
            else
            {
                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Member username must start with '@'");
            }
        }

        private static void notifyMemberCheckingUpgradeAvalability(UpdateEventArgs uea)
        {
            bool any = false;

            foreach (KeyValuePair<long, HashSet<string>> pair in upgradePendingUsernames)
            {
                if (pair.Value.Contains("@" + uea.Update.Message.From.Username))
                {
                    any = true;

                    dbManager.removeUpgradePendingUsername(pair.Key, "@" + uea.Update.Message.From.Username);
                    dbManager.removeAllowPendingUserScore(pair.Key, uea.Update.Message.From.Id.Identifier);
                    dbManager.addAllowedUser(pair.Key, uea.Update.Message.From.Id.Identifier);

                    upgradePendingUsernames[pair.Key].Remove(uea.Update.Message.From.Username);
                    allowPendingUsers.Remove(pair.Key + " " + uea.Update.Message.From.Id.Identifier);
                    linkAllowedUsers[pair.Key].Add(uea.Update.Message.From.Id.Identifier);

                    botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "you are upgraded in group " + botClient.GetChatAsync(pair.Key).Result.Title);
                }
            }

            if (!any)
            {
                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "No upgrade found .");
            }
        }

        private static void notifyAdminPickedGroupToUpgradeMember(UpdateEventArgs uea)
        {
            long groupId = Convert.ToInt64(uea.Update.CallbackQuery.Data.Substring(switchToUpgradCommand.Length));

            upgradeBusyAdmins.Add(uea.Update.CallbackQuery.From.Id.Identifier, groupId);

            botClient.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id.Identifier, uea.Update.CallbackQuery.Message
                .MessageId, "Now enter the member username.", Telegram.Bot.Types.Enums.ParseMode.Default, false, null);
        }

        private static void notifyAdminPrepareMemberUpgrade(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotInviteResponse1);

            List<Chat> userGroups = new List<Chat>();

            foreach (long groupId in botCircleGroups.Keys)
            {
                try
                {
                    ChatMember cm = botClient.GetChatMemberAsync(groupId, (int)uea.Update.Message.From.Id.Identifier).Result;

                    if (cm != null && (cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Creator || cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Administrator))
                    {
                        userGroups.Add(botClient.GetChatAsync(groupId).Result);
                    }
                }
                catch (Exception) { }
            }

            if (userGroups.Count > 0)
            {
                InlineKeyboardButton[][] keyboardButtons = new InlineKeyboardButton[userGroups.Count][];

                int counter = 0;

                foreach (Chat group in userGroups)
                {
                    keyboardButtons[counter] = new InlineKeyboardButton[1]
                    {
                                                    new InlineKeyboardButton(group.Title, switchToUpgradCommand + group.Id.Identifier)
                    };

                    counter++;
                }

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Your Groups :"
                    , Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0, new InlineKeyboardMarkup(keyboardButtons));
            }
        }

        private static void notifyAdminRequestingPanel(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotInviteResponse1);

            List<Chat> userGroups = new List<Chat>();

            foreach (long groupId in botCircleGroups.Keys)
            {
                try
                {
                    ChatMember cm = botClient.GetChatMemberAsync(groupId, (int)uea.Update.Message.From.Id.Identifier).Result;

                    if (cm != null && (cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Creator || cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Administrator))
                    {
                        userGroups.Add(botClient.GetChatAsync(groupId).Result);
                    }
                }
                catch (Exception) { }
            }

            if (userGroups.Count > 0)
            {
                InlineKeyboardButton[][] keyboardButtons = new InlineKeyboardButton[userGroups.Count][];

                int counter = 0;

                foreach (Chat group in userGroups)
                {
                    keyboardButtons[counter] = new InlineKeyboardButton[1]
                    {
                                                    new InlineKeyboardButton(group.Title, showGroupScoresCommand + group.Id.Identifier)
                    };

                    counter++;
                }

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Your Groups :"
                    , Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0, new InlineKeyboardMarkup(keyboardButtons));
            }
        }

        private static void notifyAdminRequestingUsersScores(UpdateEventArgs uea)
        {
            long groupId = Convert.ToInt64(uea.Update.CallbackQuery.Data.Substring(showGroupScoresCommand.Length));

            List<KeyValuePair<long, int>> groupScoresList = dbManager.getGroupScoresList(groupId);

            List<KeyValuePair<string, int>> updatedMemberScores = new List<KeyValuePair<string, int>>();

            int counter = 0;

            foreach (KeyValuePair<long, int> pair in groupScoresList)
            {
                try
                {
                    try
                    {
                        ChatMember cm = botClient.GetChatMemberAsync(groupId, (int)pair.Key).Result;

                        if (cm != null && (cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Member))
                        {
                            updatedMemberScores.Add(new KeyValuePair<string, int>(cm.User.FirstName + " " + cm.User.LastName, pair.Value));
                        }
                        else
                        {
                            dbManager.removeAllowPendingUserScore(groupId, pair.Key);
                        }
                    }
                    catch (Exception)
                    {
                        dbManager.removeAllowPendingUserScore(groupId, pair.Key);
                    }
                }
                catch (Exception) { }

                counter++;
            }

            InlineKeyboardButton[][] keyboardButtons = new InlineKeyboardButton[updatedMemberScores.Count][];

            counter = 0;

            foreach (KeyValuePair<string, int> pair in updatedMemberScores)
            {
                keyboardButtons[counter] = new InlineKeyboardButton[]
                {
                                        new InlineKeyboardButton(pair.Key, "not_set"),
                                        new InlineKeyboardButton(pair.Value.ToString(), "not_set")
                };

                counter++;
            }

            botClient.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id, uea.Update.CallbackQuery.Message.MessageId, "Users Scores List : ", Telegram.Bot.Types.Enums.ParseMode.Default, false, new InlineKeyboardMarkup(keyboardButtons));
        }

        private static async void notifyNonAdminSentLinkToGroup(UpdateEventArgs uea)
        {
            if (!linkAllowedUsers[uea.Update.Message.Chat.Id.Identifier].Contains(uea.Update.Message.From.Id.Identifier))
            {
                await botClient.DeleteMessageAsync(uea.Update.Message.Chat.Id.Identifier, uea.Update.Message.MessageId);
                Message message = await botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "کاربر گرامی " + uea.Update.Message.From.FirstName + " , " + Environment.NewLine + BotResources.BotForbidLinkResponse);
                int id = dbManager.addNewDestructableMessage(uea.Update.Message.Chat.Id.Identifier, message.MessageId);
                new DestructorThread(id, uea.Update.Message.Chat.Id.Identifier, message.MessageId, NotifyBotDestructingMessage);
            }
        }

        private static void notifyUserRequestedBotInviteSystemHelpText(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotInviteSystemHelpText);
        }

        private static void notifyUserRequestedBotStart(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotStartResponse);
        }

        private static void notifyUserRequestedBotHelpText(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotHelpText);
        }

        private static void notifyNewUserJointGroup(UpdateEventArgs uea, User cm)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id.Identifier, "سلام " + cm.FirstName + " , " + Environment.NewLine + "از این که در گروه ما عضو شدی ممنونم . به گروه ما خوش آمدی .");

            long adderUserId = uea.Update.Message.From.Id.Identifier;

            if (adderUserId != cm.Id.Identifier)
            {
                if (!linkAllowedUsers[uea.Update.Message.Chat.Id.Identifier].Contains(adderUserId))
                {
                    lock (allowPendingUsers)
                    {
                        if (!allowPendingUsers.ContainsKey(uea.Update.Message.Chat.Id.Identifier + " " + adderUserId))
                        {
                            allowPendingUsers.Add(uea.Update.Message.Chat.Id.Identifier + " " + adderUserId, 1);
                            dbManager.updateAllowPendingUserScore(uea.Update.Message.Chat.Id.Identifier, adderUserId, 1);
                            Console.WriteLine("inviter score increased " + adderUserId + " to 1");
                        }
                        else
                        {
                            lock (allowPendingUsers)
                            {
                                int oldScore = allowPendingUsers[uea.Update.Message.Chat.Id.Identifier + " " + adderUserId];
                                allowPendingUsers.Remove(uea.Update.Message.Chat.Id.Identifier + " " + adderUserId);

                                if (oldScore + 1 >= 10)
                                {
                                    linkAllowedUsers[uea.Update.Message.Chat.Id.Identifier].Add(adderUserId);
                                    dbManager.removeAllowPendingUserScore(uea.Update.Message.Chat.Id.Identifier, adderUserId);
                                    dbManager.addAllowedUser(uea.Update.Message.Chat.Id.Identifier, adderUserId);
                                    Console.WriteLine("inviter score reached 10 and upgraded to allowed level");
                                }
                                else
                                {
                                    allowPendingUsers.Add(uea.Update.Message.Chat.Id.Identifier + " " + adderUserId, oldScore + 1);
                                    dbManager.updateAllowPendingUserScore(uea.Update.Message.Chat.Id.Identifier, adderUserId, oldScore + 1);
                                    Console.WriteLine("inviter score increased " + adderUserId + " " + (oldScore + 1));
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void notifyEntitiesSentInGroup(UpdateEventArgs uea)
        {
            if (!linkAllowedUsers[uea.Update.Message.Chat.Id.Identifier].Contains(uea.Update.Message.From.Id.Identifier))
            {
                List<MessageEntity> enities = uea.Update.Message.Entities;
                List<string> entityValues = uea.Update.Message.EntityValues;

                for (int counter = 0; counter < enities.Count; counter++)
                {
                    if (enities[counter].Type == Telegram.Bot.Types.Enums.MessageEntityType.Url)
                    {
                        notifyNonAdminSentLinkToGroup(uea);
                        //notifyDeletingMemberAdvertiseMessage(uea);
                        break;
                    }
                    else if (enities[counter].Type == Telegram.Bot.Types.Enums.MessageEntityType.Mention)
                    {
                        if (checkGroupExistance(entityValues[counter]))
                        {
                            notifyNonAdminSentLinkToGroup(uea);
                            //notifyDeletingMemberAdvertiseMessage(uea);
                            break;
                        }
                    }
                }
            }
        }

        private static bool checkGroupExistance(string username)
        {
            Console.WriteLine("Checking " + username);
            try
            {
                Chat chat = botClient.GetChatAsync(username).Result;
                return true;
            }
            catch (Exception ex) { if (ex.ToString().ToLower().Contains("too many requests")) return true; else return false; }
        }

        private static void notifyUserPreparingInvite(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id.Identifier, BotResources.BotInviteResponse1);

            List<Chat> userGroups = new List<Chat>();

            foreach (long groupId in botCircleGroups.Keys)
            {
                try
                {
                    ChatMember cm = botClient.GetChatMemberAsync(groupId, (int)(uea.Update.Message.From.Id.Identifier)).Result;

                    if (cm != null && (cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Member || cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Creator || cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Administrator))
                    {
                        userGroups.Add(botClient.GetChatAsync(groupId).Result);
                    }
                }
                catch (Exception) { }
            }

            if (userGroups.Count > 0)
            {
                InlineKeyboardButton[][] keybaordButtonsArr = new InlineKeyboardButton[userGroups.Count][];

                for (int btnCounter = 0; btnCounter < userGroups.Count; btnCounter++)
                {
                    keybaordButtonsArr[btnCounter] = new InlineKeyboardButton[]
                    {
                        new InlineKeyboardButton(userGroups[btnCounter].Title, inviterGroupSetCommand + userGroups[btnCounter].Id)
                    };
                }

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotInviteResponse2, Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0,
                    new InlineKeyboardMarkup()
                    {
                        InlineKeyboard = keybaordButtonsArr
                    });
            }
        }

        private static void notifyInvitationOpened(UpdateEventArgs uea)
        {
            string numDatas = uea.Update.CallbackQuery.Data.Substring(createCardCommand.Length);
            string[] nums = numDatas.Split(' ');
            long groupId = Convert.ToInt64(nums[0]);
            long inviterId = Convert.ToInt32(nums[1]);

            if (inviterId != uea.Update.CallbackQuery.From.Id.Identifier)
            {
                Console.WriteLine(groupsInviteLinks[groupId]);

                InlineKeyboardMarkup keyboardMarkup = new InlineKeyboardMarkup()
                {
                    InlineKeyboard = new InlineKeyboardButton[][]
                    {
                        new InlineKeyboardButton[]
                        {
                            new InlineKeyboardButton("Join Group", "")
                            {
                                Url = groupsInviteLinks[groupId]
                            }
                        }
                    }
                };

                ChatMember[] groupAdmins = botClient.GetChatAdministratorsAsync(groupId).Result;

                bool inviterIsAdmin = false;

                foreach (ChatMember cm in groupAdmins)
                {
                    if (cm.User.Id.Identifier == inviterId)
                    {
                        inviterIsAdmin = true;
                        break;
                    }
                }

                if (!inviterIsAdmin)
                {
                    lock (invitedsToInviters)
                    {
                        if (invitedsToInviters.ContainsKey(groupId + " " + uea.Update.CallbackQuery.From.Id.Identifier))
                        {
                            invitedsToInviters.Remove(groupId + " " + uea.Update.CallbackQuery.From.Id.Identifier);
                        }

                        Console.WriteLine(uea.Update.CallbackQuery.InlineMessageId);

                        invitedsToInviters.Add(groupId + " " + uea.Update.CallbackQuery.From.Id.Identifier, new KeyValuePair<long, string>(inviterId, uea.Update.CallbackQuery.InlineMessageId));
                    }

                    dbManager.updateOpenedInviteRequest(groupId, inviterId, uea.Update.CallbackQuery.From.Id.Identifier, uea.Update.CallbackQuery.InlineMessageId);
                }

                botClient.EditInlineMessageTextAsync(uea.Update.CallbackQuery.InlineMessageId
                    , botClient.GetChatAsync(groupId).Result.Title, Telegram.Bot.Types.Enums.ParseMode.Default, false, keyboardMarkup);
            }
        }

        private static void notifyInviterGroupSet(UpdateEventArgs uea)
        {
            long groupId = Convert.ToInt64(uea.Update.CallbackQuery.Data.Substring(inviterGroupSetCommand.Length));

            lock (usersGroupInviteRequests)
            {
                if (usersGroupInviteRequests.ContainsKey(uea.Update.CallbackQuery.From.Id.Identifier))
                {
                    usersGroupInviteRequests.Remove(uea.Update.CallbackQuery.From.Id.Identifier);
                }

                usersGroupInviteRequests.Add(uea.Update.CallbackQuery.From.Id.Identifier, groupId);
            }

            botClient.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id, uea.Update.CallbackQuery.Message.MessageId, BotResources.BotPickUserResponse,
                Telegram.Bot.Types.Enums.ParseMode.Default, false,
                new InlineKeyboardMarkup()
                {
                    InlineKeyboard = new InlineKeyboardButton[][]
                    {
                        new InlineKeyboardButton[]
                        {
                            new InlineKeyboardButton("یه دوستتو انتخاب کن", "")
                            {
                                SwitchInlineQuery = "invite"
                            }
                        }
                    }
                });
        }

        private static void notifyInlineInviteRequested(UpdateEventArgs uea)
        {
            long groupId = 0;

            if (usersGroupInviteRequests.TryGetValue(uea.Update.InlineQuery.From.Id.Identifier, out groupId))
            {
                usersGroupInviteRequests.Remove(uea.Update.InlineQuery.From.Id.Identifier);
                
                botClient.AnswerInlineQueryAsync(uea.Update.InlineQuery.Id, new Telegram.Bot.Types.InlineQueryResults.InlineQueryResultArticle[]
                {
                    new Telegram.Bot.Types.InlineQueryResults.InlineQueryResultArticle()
                    {
                        Id = "1",
                        Title = botClient.GetChatAsync(groupId).Result.Title,
                        InputMessageContent = new InputTextMessageContent() { MessageText = "Press button below to show link" },
                        ReplyMarkup = new InlineKeyboardMarkup()
                        {
                            InlineKeyboard = new InlineKeyboardButton[][]
                            {
                                new InlineKeyboardButton[]
                                {
                                    new InlineKeyboardButton("Show Group Invite Link", createCardCommand + groupId + " " + uea.Update.InlineQuery.From.Id.Identifier)
                                }
                            }
                        }
                    }
                });
            }
        }

        private static void notifySendUserStatsInGroups(UpdateEventArgs uea)
        {
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id.Identifier, BotResources.BotInviteResponse1);

            List<Chat> userGroups = new List<Chat>();

            foreach (long groupId in botCircleGroups.Keys)
            {
                try
                {
                    ChatMember cm = botClient.GetChatMemberAsync(groupId, (int)uea.Update.Message.From.Id.Identifier).Result;

                    if (cm != null && (cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Member || cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Creator || cm.Status == Telegram.Bot.Types.Enums.ChatMemberStatus.Administrator))
                    {
                        userGroups.Add(botClient.GetChatAsync(groupId).Result);
                    }
                }
                catch (Exception) { }
            }

            if (userGroups.Count > 0)
            {
                InlineKeyboardButton[][] keybaordButtonsArr = new InlineKeyboardButton[userGroups.Count][];

                for (int btnCounter = 0; btnCounter < userGroups.Count; btnCounter++)
                {
                    int score = 0;

                    allowPendingUsers.TryGetValue(userGroups[btnCounter].Id.Identifier + " " + uea.Update.Message.From.Id.Identifier, out score);
                    
                    keybaordButtonsArr[btnCounter] = new InlineKeyboardButton[]
                    {
                        new InlineKeyboardButton(userGroups[btnCounter].Title + " : " + score.ToString() + " scores", "nullCallback")
                    };
                }

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotStatsResponse2, Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0,
                    new InlineKeyboardMarkup()
                    {
                        InlineKeyboard = keybaordButtonsArr
                    });
            }
        }

        private static void notifyMemberActedInSilenceTime(UpdateEventArgs uea, int startTime, int finishTime)
        {
            TimeSpan startDT = TimeSpan.FromMilliseconds(startTime);
            TimeSpan finishDT = TimeSpan.FromMilliseconds(finishTime);

            var startDTParts = string.Format("{0:D2}:{1:D2}", startDT.Hours, startDT.Minutes).Split(':').SkipWhile(s => Regex.Match(s, @"00\w").Success).ToArray();
            var startDTStr = string.Join(":", startDTParts);

            var finishDTParts = string.Format("{0:D2}:{1:D2}", finishDT.Hours, finishDT.Minutes).Split(':').SkipWhile(s => Regex.Match(s, @"00\w").Success).ToArray();
            var finishDTStr = string.Join(":", finishDTParts);

            botClient.DeleteMessageAsync(uea.Update.Message.Chat.Id, uea.Update.Message.MessageId);
            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "کاربر گرامی " + uea.Update.Message.From.FirstName + " , " + Environment.NewLine + "با عرض پوزش شما اجازه قرار دادن پیام از ساعت (" + startDTStr + ") تا ساعت (" + finishDTStr + ") را ندارید. این بازه زمانی توسط مدیر گروه به عنوان ساعت سکوت انتخاب شده است.");
        }

        private static void notifyAdminSettingSilenceTime(UpdateEventArgs uea)
        {
            string argsData = uea.Update.Message.Text.Substring(silenceCommand.Length);
            string[] timeDetails = argsData.Contains(" - ") ? argsData.Split(new string[] { " - " }, StringSplitOptions.None) : argsData.Split('-');

            if (timeDetails.Length == 2)
            {
                DateTime startTime = DateTime.ParseExact(timeDetails[0], "HH:mm", CultureInfo.InvariantCulture);
                DateTime finishTime = DateTime.ParseExact(timeDetails[1], "HH:mm", CultureInfo.InvariantCulture);

                int startTimeMillis = (int)startTime.TimeOfDay.TotalMilliseconds;
                int finishTimeMillis = (int)finishTime.TimeOfDay.TotalMilliseconds;
                
                groupsSilenceThread[uea.Update.Message.Chat.Id.Identifier].Recycle = true;
                groupsSilenceThread[uea.Update.Message.Chat.Id.Identifier] = new SilenceThread(uea.Update.Message.Chat.Id.Identifier, startTimeMillis, finishTimeMillis);

                dbManager.updateGroupSilenceTime(uea.Update.Message.Chat.Id.Identifier, startTimeMillis, finishTimeMillis);

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id.Identifier, "ساعت سکوت تنظیم شد. کاربران از ساعت " + timeDetails[0] + "تا ساعت " + timeDetails[1] + "اجازه ی پیام گذاشتن ندارند.");
            }
            else
            {
                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id.Identifier, "قالب زمان رعایت نشده است . توجه کنید که عدد ساعت و دقیقه باید دو رقمی باشد. مانند : 09:53 یا 23:12");
            }
        }

        private static void notifyAdminCreatingElection(UpdateEventArgs uea)
        {
            lock (startPendingElections)
            {
                if (startPendingElections.ContainsKey(uea.Update.Message.Chat.Id.Identifier + " " + uea.Update.Message.From.Id.Identifier))
                {
                    startPendingElections[uea.Update.Message.Chat.Id.Identifier + " " + uea.Update.Message.From.Id.Identifier] = new Election(-1);
                }
                else
                {
                    startPendingElections.Add(uea.Update.Message.Chat.Id.Identifier + " " + uea.Update.Message.From.Id.Identifier, new Election(-1));
                }
            }

            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "فرم نظر سنجی جدیدی برای شما ایجاد شد . لطفا سوال نظر سنجی خود را وارد کنید.");
        }

        private static void notifyStartingElection(UpdateEventArgs uea, Election election)
        {
            if (election.Description != null && election.Options.Count > 1)
            {
                startPendingElections.Remove(uea.Update.Message.Chat.Id.Identifier + " " + uea.Update.Message.From.Id.Identifier);

                int createdElectionId = dbManager.createElection(uea.Update.Message.Chat.Id.Identifier, election);

                InlineKeyboardButton[][] keyboardArr = new InlineKeyboardButton[election.Options.Count][];

                for (int btnCounter = 0; btnCounter < election.Options.Count; btnCounter++)
                {
                    keyboardArr[btnCounter] = new InlineKeyboardButton[]
                    {
                        new InlineKeyboardButton("0 - " + election.Options[btnCounter].Key, "increase_vote " + createdElectionId + " " + btnCounter)
                    };
                }

                InlineKeyboardMarkup keyboardMarkup = new InlineKeyboardMarkup()
                {
                    InlineKeyboard = keyboardArr
                };

                election.Id = createdElectionId;

                startedElections.Add(createdElectionId, election);

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "نظرسنجی آغاز شد.");

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, election.Description, Telegram.Bot.Types.Enums.ParseMode.Default,
                    false, false, 0, keyboardMarkup);
            }
            else
            {
                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "election configuration not completed.");
            }
        }

        private static void notifyDevelopingElection(UpdateEventArgs uea, Election election)
        {
            if (election.Description == null)
            {
                election.Description = uea.Update.Message.Text;

                botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "با تشکر از شما سوال نظرسنجی شما ثبت شد . حالا گزینه های نظرسنجی را وارد کنید ( هر پیام یک گزینه )");
            }
            else
            {
                if (election.Options.Count <= 10)
                {
                    election.Options.Add(new KeyValuePair<string, int>(uea.Update.Message.Text, 0));
                    botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "با تشکر از شما این گزینه به نظرسنجی اضافه شد . در صورت تمایل گزینه های دیگر را نیز وارد کنید .");
                }
                else
                {
                    botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, "نظرسنجی بیشتر از 10 گزینه نمی تواند داشته باشد.");
                }
            }
        }

        private static void notifyUserVotingElection(UpdateEventArgs uea)
        {
            string[] dataArgs = uea.Update.CallbackQuery.Data.Substring(increaseVoteCommand.Length).Split(' ');
            int electionId = Convert.ToInt32(dataArgs[0]);
            int voteId = Convert.ToInt32(dataArgs[1]);

            Election election = startedElections[electionId];

            dbManager.updateElector(electionId, uea.Update.CallbackQuery.From.Id.Identifier, voteId, election);

            InlineKeyboardButton[][] keyboardArr = new InlineKeyboardButton[election.Options.Count][];

            for (int btnCounter = 0; btnCounter < election.Options.Count; btnCounter++)
            {
                keyboardArr[btnCounter] = new InlineKeyboardButton[]
                {
                    new InlineKeyboardButton(election.Options[btnCounter].Value + " - " + election.Options[btnCounter].Key, "increase_vote " + electionId + " " + btnCounter)
                };
            }

            botClient.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id, uea.Update.CallbackQuery.Message.MessageId
                , election.Description, Telegram.Bot.Types.Enums.ParseMode.Default, false, new InlineKeyboardMarkup()
                {
                    InlineKeyboard = keyboardArr
                });
        }

        private static void notifyAdminSettingGroupInviteLink(UpdateEventArgs uea)
        {
            string groupLink = uea.Update.Message.Text.Substring(setInviteLinkCommand.Length);

            dbManager.updateGroupInviteLink(uea.Update.Message.Chat.Id.Identifier, groupLink);

            lock (groupsInviteLinks)
            {
                groupsInviteLinks[uea.Update.Message.Chat.Id.Identifier] = groupLink;
            }

            botClient.SendTextMessageAsync(uea.Update.Message.Chat.Id, BotResources.BotSetInviteLinkReponse);
        }

        private static void NotifyBotDestructingMessage(int id, long groupId, int messageId)
        {
            try
            {
                botClient.DeleteMessageAsync(groupId, messageId);
            }
            catch(Exception) { }

            dbManager.removeDestructableMessage(id);
        }
    }
}
