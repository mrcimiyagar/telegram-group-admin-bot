using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GroupAdminTelegramBot
{
    class SilenceThread
    {
        private long groupId;
        private int silenceStartTime;
        public int StartTime { get { return this.silenceStartTime; } }

        private int silenceEndTime;
        public int FinishTime { get { return this.silenceEndTime; } }
        
        public bool IsSet { get { return this.silenceStartTime != -1; } }

        private bool isSilent;
        public bool IsSilent { get { return this.isSilent; } set { this.isSilent = value; } }

        private bool recycle;
        public bool Recycle { get { return this.recycle; } set { this.recycle = value; } }

        private Thread workingThread;

        public SilenceThread(long groupId, int startTime, int endTime)
        {
            this.groupId = groupId;
            this.silenceStartTime = startTime;
            this.silenceEndTime = endTime;
            this.recycle = false;

            if (startTime == -1 || endTime == -1)
            {
                workingThread = new Thread(() =>
                {

                });

                workingThread.Start();

                return;
            }
 
            this.workingThread = new Thread(() =>
            {
                while (!recycle)
                {
                    int currentMillis = (int)DateTime.Now.TimeOfDay.TotalMilliseconds;

                    silenceEndTime += (silenceEndTime - silenceStartTime) < 0 ? 86400000 : 0;

                    if (currentMillis >= silenceStartTime && currentMillis <= silenceEndTime)
                    {
                        isSilent = true;
                        Thread.Sleep(silenceEndTime - currentMillis);
                    }
                    else if (currentMillis + 86400000 >= silenceStartTime && currentMillis + 86400000 <= silenceEndTime)
                    {
                        isSilent = true;
                        Thread.Sleep(silenceEndTime - currentMillis);
                    }
                    else
                    {
                        isSilent = false;
                        int waitTime = silenceStartTime - currentMillis;
                        Thread.Sleep(waitTime < 0 ? waitTime + 86400000 : waitTime);
                    }
                }
            });

            this.workingThread.Start();
        }
    }
}