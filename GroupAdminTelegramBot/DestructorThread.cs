using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static GroupAdminTelegramBot.Program;

namespace GroupAdminTelegramBot
{
    public class DestructorThread
    {
        public int Id { get; private set; }
        public long GroupId { get; private set; }
        public int MessageId { get; private set; }

        private Thread thread;
        private CallBotToDestructMessage callback;

        public DestructorThread(int id, long groupId, int messageId, CallBotToDestructMessage callback)
        {
            this.Id = id;
            this.GroupId = groupId;
            this.MessageId = messageId;
            this.callback = callback;

            this.thread = new Thread(() =>
            {
                Thread.Sleep(10000);
                this.callback(this.Id, this.GroupId, this.MessageId);
            });

            this.thread.Start();
        }
    }
}