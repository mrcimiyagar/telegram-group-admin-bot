using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GroupAdminTelegramBot
{
    class DatabaseManager
    {
        private string groupAdminDBPath = @"GroupAdminDB";

        private SQLiteConnection groupAdminDB;

        public DatabaseManager()
        {
            // create necessary database files

            if (!File.Exists(groupAdminDBPath))
            {
                SQLiteConnection.CreateFile(groupAdminDBPath);
            }

            if (!Directory.Exists("ElectionsDatabase"))
            {
                Directory.CreateDirectory("ElectionsDatabase");
            }

            // ***

            // config databases and create necessary files

            groupAdminDB = new SQLiteConnection(@"Data Source=" + groupAdminDBPath + ";Version=3;");
            groupAdminDB.Open();

            SQLiteCommand command = new SQLiteCommand("create table if not exists Groups (group_id bigint primary key, group_silence_start_time integer default -1, group_silence_end_time integer default -1, group_link varchar(256));", groupAdminDB);
            command.ExecuteNonQuery();

            SQLiteCommand command2 = new SQLiteCommand("create table if not exists Elections (election_id integer primary key autoincrement, group_id bigint, user_id bigint);", groupAdminDB);
            command2.ExecuteNonQuery();

            SQLiteCommand command3 = new SQLiteCommand("select * from Groups", groupAdminDB);
            SQLiteDataReader reader = command3.ExecuteReader();

            while (reader.Read())
            {
                long groupId = Convert.ToInt64(reader["group_id"]);

                SQLiteCommand command5 = new SQLiteCommand("create table if not exists 'OpenedInvitations" + groupId + "' (invited_id bigint primary key, inviter_id bigint, message_id varchar(64));", groupAdminDB);
                command5.ExecuteNonQuery();

                SQLiteCommand command6 = new SQLiteCommand("create table if not exists 'AllowPendingUsers" + groupId + "' (user_id bigint primary key, score integer);", groupAdminDB);
                command6.ExecuteNonQuery();

                SQLiteCommand command7 = new SQLiteCommand("create table if not exists 'AllowedUsers" + groupId + "' (user_id bigint primary key);", groupAdminDB);
                command7.ExecuteNonQuery();

                /*SQLiteCommand command100 = new SQLiteCommand("drop table if exists 'UpgradePendingUsernames" + groupId + "'", groupAdminDB);
                command100.ExecuteNonQuery();*/

                SQLiteCommand command8 = new SQLiteCommand("create table if not exists 'UpgradePendingUsernames" + groupId + "' (id integer primary key autoincrement, username var);", groupAdminDB);
                command8.ExecuteNonQuery();
            }

        }

        public void addGroup(long groupId)
        {
            SQLiteCommand command = new SQLiteCommand("select * from Groups where group_id = " + groupId, groupAdminDB);
            SQLiteDataReader reader = command.ExecuteReader();

            if (reader.StepCount == 0)
            {
                SQLiteCommand command1 = new SQLiteCommand("insert into Groups (group_id, group_silence_start_time, group_silence_end_time, group_link) values (" + groupId + ", -1, -1, 'not_set');", groupAdminDB);
                command1.ExecuteNonQuery();

                SQLiteCommand command2 = new SQLiteCommand("create table if not exists 'OpenedInvitations" + groupId + "' (invited_id bigint primary key, inviter_id bigint, message_id varchar(64));", groupAdminDB);
                command2.ExecuteNonQuery();

                SQLiteCommand command3 = new SQLiteCommand("create table if not exists 'AllowPendingUsers" + groupId + "' (user_id bigint primary key, score integer);", groupAdminDB);
                command3.ExecuteNonQuery();

                SQLiteCommand command4 = new SQLiteCommand("create table if not exists 'AllowedUsers" + groupId + "' (user_id bigint primary key);", groupAdminDB);
                command4.ExecuteNonQuery();

                SQLiteCommand command5 = new SQLiteCommand("create table if not exists 'UpgradePendingUsernames" + groupId + "' (id integer primary key, username var);", groupAdminDB);
                command5.ExecuteNonQuery();
            }
        }

        public void removeGroup(long groupId)
        {
            SQLiteCommand command = new SQLiteCommand("delete from Groups where group_id = " + groupId, groupAdminDB);
            command.ExecuteNonQuery();

            SQLiteCommand command2 = new SQLiteCommand("drop table if exists 'OpenedInvitations" + groupId + "'", groupAdminDB);
            command2.ExecuteNonQuery();

            SQLiteCommand command3 = new SQLiteCommand("drop table if exists 'AllowPendingUsers" + groupId + "'", groupAdminDB);
            command3.ExecuteNonQuery();

            SQLiteCommand command4 = new SQLiteCommand("drop table if exists 'AllowedUsers" + groupId + "'", groupAdminDB);
            command4.ExecuteNonQuery();

            SQLiteCommand command5 = new SQLiteCommand("drop table if exists 'UpgradePendingUsernames" + groupId + "'", groupAdminDB);
            command5.ExecuteNonQuery();
        }

        public void updateGroupSilenceTime(long groupId, long startTime, long endTime)
        {
            SQLiteCommand command = new SQLiteCommand("update Groups set group_silence_start_time = " + startTime + ", group_silence_end_time = " + endTime + " where group_id = " + groupId, groupAdminDB);
            command.ExecuteNonQuery();
        }

        public void updateGroupInviteLink(long groupId, string inviteLink)
        {
            SQLiteCommand command = new SQLiteCommand("update Groups set group_link = '" + inviteLink + "' where group_id = " + groupId, groupAdminDB);
            command.ExecuteNonQuery();
        }

        public Tuple<Dictionary<long, string>, Dictionary<long, KeyValuePair<int, int>>> getBotGroupsList()
        {
            Dictionary<long, KeyValuePair<int, int>> groupsIdsList = new Dictionary<long, KeyValuePair<int, int>>();
            Dictionary<long, string> groupsInviteLinks = new Dictionary<long, string>();

            SQLiteCommand command = new SQLiteCommand("select * from Groups", groupAdminDB);
            SQLiteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                groupsIdsList.Add(Convert.ToInt64(reader["group_id"]), new KeyValuePair<int, int>(Convert.ToInt32(reader["group_silence_start_time"]), Convert.ToInt32(reader["group_silence_end_time"])));
                groupsInviteLinks.Add(Convert.ToInt64(reader["group_id"]), reader["group_link"].ToString());
            }

            return new Tuple<Dictionary<long, string>, Dictionary<long, KeyValuePair<int, int>>>(groupsInviteLinks, groupsIdsList);
        }

        public Dictionary<string, KeyValuePair<long, string>> getOpenedInvitations(HashSet<long> botGroupsSet)
        {
            Dictionary<string, KeyValuePair<long, string>> result = new Dictionary<string, KeyValuePair<long, string>>();

            foreach(long groupId in botGroupsSet)
            {
                SQLiteCommand command = new SQLiteCommand("select * from 'OpenedInvitations" + groupId + "'", groupAdminDB);
                SQLiteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    result.Add(groupId + " " + Convert.ToInt32(reader["invited_id"]), new KeyValuePair<long, string>(Convert.ToInt64(reader["inviter_id"]), reader["message_id"].ToString()));
                }
            }

            return result;
        }

        public Dictionary<string, int> getAllowPendingUsers(HashSet<long> botGroupsSet)
        {
            Dictionary<string, int> result = new Dictionary<string, int>();

            foreach (long groupId in botGroupsSet)
            {
                SQLiteCommand command = new SQLiteCommand("select * from 'AllowPendingUsers" + groupId + "'", groupAdminDB);
                SQLiteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    result.Add(groupId + " " + Convert.ToInt64(reader["user_id"]), Convert.ToInt32(reader["score"]));
                }
            }
            
            return result;
        }

        public Dictionary<long, HashSet<long>> getAllowedUsersList(HashSet<long> botGroupsSet)
        {
            Dictionary<long, HashSet<long>> allowedUsersList = new Dictionary<long, HashSet<long>>();

            foreach (long groupId in botGroupsSet)
            {
                HashSet<long> allowedUsers = new HashSet<long>();

                SQLiteCommand command = new SQLiteCommand("select * from 'AllowedUsers" + groupId + "'", groupAdminDB);
                SQLiteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    allowedUsers.Add(Convert.ToInt64(reader["user_id"]));
                }

                allowedUsersList.Add(groupId, allowedUsers);
            }

            return allowedUsersList;
        }

        public Dictionary<long, HashSet<string>> getUpgradePendingUsernames(HashSet<long> botGroupsSet)
        {
            Dictionary<long, HashSet<string>> result = new Dictionary<long, HashSet<string>>();

            foreach(long groupId in botGroupsSet)
            {
                HashSet<string> unSet = new HashSet<string>();

                SQLiteCommand command = new SQLiteCommand("select * from 'UpgradePendingUsernames" + groupId + "'", groupAdminDB);
                SQLiteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    unSet.Add(reader["username"].ToString());
                }

                result.Add(groupId, unSet);
            }

            return result;
        }

        public Dictionary<int, Election> getElectionsList()
        {
            Dictionary<int, Election> result = new Dictionary<int, Election>();

            SQLiteCommand command = new SQLiteCommand("select * from Elections", groupAdminDB);
            SQLiteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                int electionId = Convert.ToInt32(reader["election_id"]);
                Election election = JsonConvert.DeserializeObject<Election>(File.ReadAllText(@"ElectionsDatabase\" + electionId));

                result.Add(electionId, election);
            }

            return result;
        }

        public void updateOpenedInviteRequest(long groupId, long inviterId, long invitedId, string messageId)
        {
            lock (groupAdminDB)
            {
                SQLiteCommand command = new SQLiteCommand("select count(*) from 'OpenedInvitations" + groupId + "' where invited_id = " + invitedId, groupAdminDB);
                int count = Convert.ToInt32(command.ExecuteScalar());

                if (count > 0)
                {
                    SQLiteCommand command2 = new SQLiteCommand("update 'OpenedInvitations" + groupId + "' set inviter_id = " + inviterId + ", message_id = '" + messageId + "' where invited_id = " + invitedId, groupAdminDB);
                    command2.ExecuteNonQuery();
                }
                else
                {
                    SQLiteCommand command2 = new SQLiteCommand("insert into 'OpenedInvitations" + groupId + "' (invited_id, inviter_id, message_id) values (" + invitedId + ", " + inviterId + ", '" + messageId + "');", groupAdminDB);
                    command2.ExecuteNonQuery();
                }
            }
        }

        public void removeOpenedInviteRequest(long groupId, long invitedId)
        {
            SQLiteCommand command = new SQLiteCommand("delete from 'OpenedInvitations" + groupId + "' where invited_id = " + invitedId, groupAdminDB);
            command.ExecuteNonQuery();
        }

        public void updateAllowPendingUserScore(long groupId, long userId, int score)
        {
            lock (groupAdminDB)
            {
                SQLiteCommand command = new SQLiteCommand("select count(*) from 'AllowPendingUsers" + groupId + "' where user_id = " + userId, groupAdminDB);
                int count = Convert.ToInt32(command.ExecuteScalar());

                if (count > 0)
                {
                    SQLiteCommand command2 = new SQLiteCommand("update 'AllowPendingUsers" + groupId + "' set score = " + score + " where user_id = " + userId, groupAdminDB);
                    command2.ExecuteNonQuery();
                }
                else
                {
                    SQLiteCommand command2 = new SQLiteCommand("insert into 'AllowPendingUsers" + groupId + "' (user_id, score) values (" + userId + ", " + score + ");", groupAdminDB);
                    command2.ExecuteNonQuery();
                }
            }
        }

        public void removeAllowPendingUserScore(long groupId, long userId)
        {
            SQLiteCommand command = new SQLiteCommand("delete from 'AllowPendingUsers" + groupId + "' where user_id = " + userId, groupAdminDB);
            command.ExecuteNonQuery();
        }

        public List<KeyValuePair<long, int>> getGroupScoresList(long groupId)
        {
            List<KeyValuePair<long, int>> result = new List<KeyValuePair<long, int>>();
            
            SQLiteCommand command = new SQLiteCommand("select * from 'AllowPendingUsers" + groupId + "'", groupAdminDB);
            SQLiteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                result.Add(new KeyValuePair<long, int>(Convert.ToInt64(reader["user_id"]), Convert.ToInt32(reader["score"])));
            }

            return result;
        }

        public void addAllowedUser(long groupId, long userId)
        {
            lock (groupAdminDB)
            {
                SQLiteCommand command = new SQLiteCommand("select count(*) from 'AllowedUsers" + groupId + "' where user_id = " + userId, groupAdminDB);
                int count = Convert.ToInt32(command.ExecuteScalar());

                if (count == 0)
                {
                    SQLiteCommand command1 = new SQLiteCommand("insert into 'AllowedUsers" + groupId + "' (user_id) values (" + userId + ");", groupAdminDB);
                    command1.ExecuteNonQuery();
                }
            }
        }

        public void removeAllowedUser(long groupId, long userId)
        {
            SQLiteCommand command = new SQLiteCommand("delete from 'AllowedUsers" + groupId + "' where user_id = " + userId, groupAdminDB);
            command.ExecuteNonQuery();
        }

        public void addUpgradePendingUsername(long groupId, string username)
        {
            lock (groupAdminDB)
            {
                SQLiteCommand command = new SQLiteCommand("select count(*) from 'UpgradePendingUsernames" + groupId + "' where username = '" + username + "'", groupAdminDB);
                int count = Convert.ToInt32(command.ExecuteScalar());

                if (count == 0)
                {
                    SQLiteCommand command1 = new SQLiteCommand("insert into 'UpgradePendingUsernames" + groupId + "' (username) values ('" + username + "');", groupAdminDB);
                    command1.ExecuteNonQuery();
                }
            }
        }

        public void removeUpgradePendingUsername(long groupId, string username)
        {
            SQLiteCommand command = new SQLiteCommand("delete from 'UpgradePendingUsernames" + groupId + "' where username = " + username, groupAdminDB);
            command.ExecuteNonQuery();
        }

        public int createElection(long groupId, Election election)
        {
            int id = -1;
            lock (groupAdminDB)
            {
                SQLiteCommand command = new SQLiteCommand("insert into Elections (group_id) values (" + groupId + ");", groupAdminDB);
                command.ExecuteNonQuery();
                SQLiteCommand command2 = new SQLiteCommand("select last_insert_rowid()", groupAdminDB);
                id = Convert.ToInt32(command2.ExecuteScalar());
            }
            SQLiteCommand command3 = new SQLiteCommand("create table if not exists Election" + id + " (user_id, vote);", groupAdminDB);
            command3.ExecuteNonQuery();

            File.WriteAllText(@"ElectionsDatabase\" + id, JsonConvert.SerializeObject(election));

            return id;
        }

        public void updateElector(int electionId, long userId, int vote, Election election)
        {
            lock (groupAdminDB)
            {
                SQLiteCommand command = new SQLiteCommand("select * from Election" + electionId + " where user_id = " + userId, groupAdminDB);
                SQLiteDataReader reader = command.ExecuteReader();

                if (reader.StepCount > 0)
                {
                    reader.Read();

                    int oldVote = Convert.ToInt32(reader["vote"]);

                    SQLiteCommand command1 = new SQLiteCommand("update Election" + electionId + " set vote = " + vote + " where user_id = " + userId, groupAdminDB);
                    command1.ExecuteNonQuery();
                    election.Options[oldVote] = new KeyValuePair<string, int>(election.Options[oldVote].Key, election.Options[oldVote].Value - 1);
                    election.Options[vote] = new KeyValuePair<string, int>(election.Options[vote].Key, election.Options[vote].Value + 1);
                    File.WriteAllText(@"ElectionsDatabase\" + electionId, JsonConvert.SerializeObject(election));
                }
                else
                {
                    SQLiteCommand command1 = new SQLiteCommand("insert into Election" + electionId + " (user_id, vote) values (" + userId + ", " + vote + ");", groupAdminDB);
                    command1.ExecuteNonQuery();
                    election.Options[vote] = new KeyValuePair<string, int>(election.Options[vote].Key, election.Options[vote].Value + 1);
                    File.WriteAllText(@"ElectionsDatabase\" + electionId, JsonConvert.SerializeObject(election));
                }
            }
        }

        public void removeElection(int electionId)
        {
            SQLiteCommand command = new SQLiteCommand("delete from Elections where election_id = " + electionId, groupAdminDB);
            command.ExecuteNonQuery();

            SQLiteCommand command2 = new SQLiteCommand("drop table if exists Election" + electionId, groupAdminDB);
            command2.ExecuteNonQuery();

            File.Delete(@"ElectionsDatabase\" + electionId);
        }
    }
}