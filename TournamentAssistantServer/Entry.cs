﻿using System.Threading;

namespace TournamentAssistantServer
{
    class Entry
    {
        public static TAServer Server;
        public static AutoResetEvent MainThreadStop = new(false);


        static void Main(string[] args)
        {
            Server = new TAServer(args.Length > 0 ? args[0] : null);
            Server.Start();

            MainThreadStop.WaitOne();
        }
    }
}