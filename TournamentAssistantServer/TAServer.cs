﻿using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TournamentAssistantServer.Database;
using TournamentAssistantServer.Discord;
using TournamentAssistantServer.Sockets;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using TournamentAssistantShared.Sockets;

namespace TournamentAssistantServer
{
    public class TAServer
    {
        Server server;
        OAuthServer oauthServer;

        public event Func<Acknowledgement, Guid, Task> AckReceived;

        // The master server will maintain live connections to other servers, for the purpose of maintaining the master server
        // list and an updated list of tournaments
        private List<TAClient> ServerConnections { get; set; }

        private User Self { get; set; }

        private StateManager StateManager { get; set; }
        private PacketService.PacketService PacketService { get; set; }

        private QualifierBot QualifierBot { get; set; }
        private DatabaseService DatabaseService { get; set; }
        private AuthorizationService AuthorizationService { get; set; }

        private ServerConfig Config { get; set; }

        public TAServer(string botTokenArg = null)
        {
            Directory.CreateDirectory("files");
            Config = new ServerConfig(botTokenArg);
        }

        public void RegisterHandlerService(PacketService.PacketService packetService)
        {
            server.PacketReceived += packetService.ParseMessage;
        }

        //Blocks until socket server begins to start (note that this is not "until server is started")
        public async void Start()
        {
            //Check for updates
            Updater.StartUpdateChecker(this);

            //Set up the databases
            DatabaseService = new DatabaseService();

            //Set up state manager
            StateManager = new StateManager(this, DatabaseService);

            //Load saved tournaments from database
            await StateManager.LoadSavedTournaments();

            //Set up Authorization Manager
            AuthorizationService = new AuthorizationService(Config.ServerCert, Config.PluginCert);

            //Create the default server list
            ServerConnections = new List<TAClient>();

            //If we have a token, start a qualifier bot
            if (!string.IsNullOrEmpty(Config.BotToken) && Config.BotToken != "[botToken]")
            {
                //We need to await this so the DI framework has time to load the database service
                QualifierBot = new QualifierBot(botToken: Config.BotToken, server: this);
                await QualifierBot.Start(databaseService: DatabaseService);
            }

            //Give our new server a sense of self :P
            Self = new User()
            {
                Guid = Guid.Empty.ToString(),
                Name = Config.ServerName ?? "HOST"
            };

            Logger.Info("Starting the server...");

            //Set up OAuth Server if applicable settings have been set
            if (Config.OAuthPort > 0)
            {
                oauthServer = new OAuthServer(AuthorizationService, Config.Address, Config.OAuthPort, Config.OAuthClientId, Config.OAuthClientSecret);
                oauthServer.AuthorizeReceived += OAuthServer_AuthorizeReceived;
                oauthServer.Start();
            }

            //Set up event listeners
            server = new Server(Config.Port, Config.ServerCert, Config.WebsocketPort);
            server.ClientConnected += Server_ClientConnected;
            server.ClientDisconnected += Server_ClientDisconnected;
            server.PacketReceived += Server_PacketReceived_AckHandler;

            PacketService = new PacketService.PacketService(this, AuthorizationService, oauthServer);
            PacketService.Initialize(Assembly.GetExecutingAssembly(), new ServiceCollection()
                .AddSingleton(this)
                .AddSingleton(StateManager)
                .AddSingleton(DatabaseService)
                .AddSingleton(QualifierBot)
                .BuildServiceProvider());

            server.Start();

            //Add self to known servers
            await StateManager.AddServer(new CoreServer
            {
                Address = Config.Address == "[serverAddress]" ? "127.0.0.1" : Config.Address,
                Port = Config.Port,
                WebsocketPort = Config.WebsocketPort,
                Name = Config.ServerName
            });

            //(Optional) Verify that this server can be reached from the outside
            await Verifier.VerifyServer(Config.Address, Config.Port);
        }

        public void Shutdown()
        {
            server.Shutdown();
        }

        public void AddServerConnection(TAClient serverConnection)
        {
            ServerConnections.Add(serverConnection);
        }

        private async Task OAuthServer_AuthorizeReceived(User.DiscordInfo discordInfo, string userId)
        {
            /*using var httpClient = new HttpClient();
            using var memoryStream = new MemoryStream();
            var avatarUrl = $"https://cdn.discordapp.com/avatars/{discordInfo.UserId}/{discordInfo.AvatarUrl}.png";
            var avatarStream = await httpClient.GetStreamAsync(avatarUrl);
            await avatarStream.CopyToAsync(memoryStream);*/

            var user = new User
            {
                Guid = userId,
                discord_info = discordInfo,
            };

            //Give the newly connected player their Self and State
            await Send(Guid.Parse(userId), new Packet
            {
                Push = new Push
                {
                    discord_authorized = new Push.DiscordAuthorized
                    {
                        Success = true,
                        Token = AuthorizationService.GenerateWebsocketToken(user)
                    }
                }
            });
        }

        private async Task Server_ClientDisconnected(ConnectedUser client)
        {
            Logger.Error($"Client Disconnected! {client.id}");

            foreach (var tournament in StateManager.GetTournaments())
            {
                var users = StateManager.GetUsers(tournament.Guid);
                var user = users.FirstOrDefault(x => x.Guid == client.id.ToString());
                if (user != null)
                {
                    await StateManager.RemoveUser(tournament.Guid, user);
                }
            }
        }

        private Task Server_ClientConnected(ConnectedUser client)
        {
            return Task.CompletedTask;
        }

        static string LogPacket(Packet packet)
        {
            string secondaryInfo = string.Empty;
            if (packet.packetCase == Packet.packetOneofCase.Command)
            {
                var command = packet.Command;
                if (command.TypeCase == Command.TypeOneofCase.play_song)
                {
                    var playSong = command.play_song;
                    secondaryInfo = playSong.GameplayParameters.Beatmap.LevelId + " : " +
                                    playSong.GameplayParameters.Beatmap.Difficulty;
                }
                else
                {
                    secondaryInfo = command.TypeCase.ToString();
                }
            }
            else if (packet.packetCase == Packet.packetOneofCase.Request)
            {
                var request = packet.Request;
                if (request.TypeCase == Request.TypeOneofCase.load_song)
                {
                    var loadSong = request.load_song;
                    secondaryInfo = loadSong.LevelId;
                }
            }
            else if (packet.packetCase == Packet.packetOneofCase.Event)
            {
                var @event = packet.Event;

                secondaryInfo = @event.ChangedObjectCase.ToString();
                if (@event.ChangedObjectCase == Event.ChangedObjectOneofCase.user_updated)
                {
                    var user = @event.user_updated.User;
                    secondaryInfo =
                        $"{secondaryInfo} from ({user.Name} : {user.DownloadState}) : ({user.PlayState} : {user.StreamDelayMs})";
                }
                else if (@event.ChangedObjectCase == Event.ChangedObjectOneofCase.match_updated)
                {
                    var match = @event.match_updated.Match;
                    secondaryInfo = $"{secondaryInfo} ({match.SelectedMap?.GameplayParameters.Beatmap.Difficulty})";
                }
            }
            if (packet.packetCase == Packet.packetOneofCase.ForwardingPacket)
            {
                var forwardedpacketCase = packet.ForwardingPacket.Packet.packetCase;
                secondaryInfo = $"{forwardedpacketCase}";
            }

            return $"({packet.packetCase}) ({secondaryInfo})";
        }

        public Task InvokeAckReceived(Packet packet)
        {
            return AckReceived?.Invoke(packet.Acknowledgement, Guid.Parse(packet.From));
        }

        public async Task Send(Guid id, Packet packet)
        {
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            packet.From = Self?.Guid ?? Guid.Empty.ToString();
            await server.Send(id, new PacketWrapper(packet));
        }

        public async Task Send(Guid[] ids, Packet packet)
        {
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            packet.From = Self?.Guid ?? Guid.Empty.ToString();
            await server.Send(ids, new PacketWrapper(packet));
        }

        public async Task ForwardTo(Guid[] ids, Guid from, Packet packet)
        {
            packet.From = from.ToString();
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            await server.Send(ids, new PacketWrapper(packet));
        }

        public async Task BroadcastToAllInTournament(Guid tournamentId, Packet packet)
        {
            packet.From = Self.Guid;
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            await server.Send(StateManager.GetUsers(tournamentId.ToString()).Select(x => Guid.Parse(x.Guid)).ToArray(), new PacketWrapper(packet));
        }

        public async Task BroadcastToAllClients(Packet packet)
        {
            packet.From = Self.Guid;
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            await server.Broadcast(new PacketWrapper(packet));
        }

        private Task Server_PacketReceived_AckHandler(ConnectedUser user, Packet packet)
        {
            Logger.Debug($"Received data: {LogPacket(packet)}");

            //Ready to go, only disabled since it is currently unusued
            /*if (packet.packetCase != Packet.packetOneofCase.Acknowledgement)
            {
                await Send(Guid.Parse(packet.From), new Packet
                {
                    Acknowledgement = new Acknowledgement
                    {
                        PacketId = packet.Id
                    }
                });
            }*/

            return Task.CompletedTask;
        }
    }
}