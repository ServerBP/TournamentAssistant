﻿using Open.Nat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TournamentAssistantServer.Database;
using TournamentAssistantServer.Discord;
using TournamentAssistantServer.Helpers;
using TournamentAssistantServer.Sockets;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using TournamentAssistantShared.Sockets;
using TournamentAssistantShared.Utilities;
using static TournamentAssistantShared.Constants;

namespace TournamentAssistantServer
{
    public class TAServer
    {
        Server server;
        OAuthServer oauthServer;

        public event Func<Acknowledgement, Guid, Task> AckReceived;

        //The master server will maintain live connections to other servers, for the purpose of maintaining the master server
        //list and an updated list of tournaments
        private List<TAClient> ServerConnections { get; set; }

        private User Self { get; set; }

        private StateManager StateManager { get; set; }

        private QualifierBot QualifierBot { get; set; }
        private DatabaseService DatabaseService { get; set; }
        private AuthorizationService AuthorizationService { get; set; }

        private ServerConfig Config { get; set; }

        public TAServer(string botTokenArg = null)
        {
            Config = new ServerConfig(botTokenArg);
        }

        //Blocks until socket server begins to start (note that this is not "until server is started")
        public async void Start()
        {
            //Check for updates
            Updater.StartUpdateChecker(this);

            //If we have a token, start a qualifier bot
            if (!string.IsNullOrEmpty(Config.BotToken) && Config.BotToken != "[botToken]")
            {
                //We need to await this so the DI framework has time to load the database service
                QualifierBot = new QualifierBot(botToken: Config.BotToken, server: this);
                await QualifierBot.Start();
            }

            //Set up the databases
            DatabaseService = new DatabaseService();

            //Set up state manager
            StateManager = new StateManager(this, DatabaseService);

            //Load saved tournaments from database
            await StateManager.LoadSavedTournaments();

            //Set up Authorization Manager
            AuthorizationService = new AuthorizationService(DatabaseService.UserDatabase, Config.ServerCert, Config.PluginCert);

            //Create the default server list
            ServerConnections = new List<TAClient>();

            //Give our new server a sense of self :P
            Self = new User()
            {
                Guid = Guid.Empty.ToString(),
                Name = Config.ServerName ?? "HOST"
            };

            Logger.Info("Starting the server...");

            //Open ports with UPnP if available
            await OpenPort(Config.Port);
            await OpenPort(Config.WebsocketPort);

            //Set up event listeners
            server = new Server(Config.Port, Config.ServerCert, Config.WebsocketPort);
            server.PacketReceived += Server_PacketReceived_UnaurhorizedHandler;
            server.ClientConnected += Server_ClientConnected;
            server.ClientDisconnected += Server_ClientDisconnected;
            server.Start();

            //Set up OAuth Server if applicable settings have been set
            if (Config.OAuthPort > 0)
            {
                await OpenPort(Config.OAuthPort);
                oauthServer = new OAuthServer(AuthorizationService, Config.Address, Config.OAuthPort, Config.OAuthClientId, Config.OAuthClientSecret);
                oauthServer.AuthorizeRecieved += OAuthServer_AuthorizeRecieved;
                oauthServer.Start();
            }

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

        //Courtesy of andruzzzhka's Multiplayer
        async Task OpenPort(int port)
        {
            Logger.Info($"Trying to open port {port} using UPnP...");
            try
            {
                NatDiscoverer discoverer = new NatDiscoverer();
                CancellationTokenSource cts = new CancellationTokenSource(2500);
                NatDevice device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);

                await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, ""));

                Logger.Info($"Port {port} is open!");
            }
            catch (Exception)
            {
                Logger.Warning($"Can't open port {port} using UPnP! (This is only relevant for people behind NAT who don't port forward. If you're being hosted by an actual server, or you've set up port forwarding manually, you can safely ignore this message. As well as any other yellow messages... Yellow means \"warning\" folks.");
            }
        }

        private async Task OAuthServer_AuthorizeRecieved(User.DiscordInfo discordInfo, string userId)
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
                    secondaryInfo = $"{secondaryInfo} ({match.SelectedDifficulty})";
                }
            }
            if (packet.packetCase == Packet.packetOneofCase.ForwardingPacket)
            {
                var forwardedpacketCase = packet.ForwardingPacket.Packet.packetCase;
                secondaryInfo = $"{forwardedpacketCase}";
            }

            return $"({packet.packetCase}) ({secondaryInfo})";
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

        private async Task Server_PacketReceived_UnaurhorizedHandler(ConnectedUser user, Packet packet)
        {
            Logger.Debug($"Received data: {LogPacket(packet)}");

            //Ready to go, only disabled since it is currently unusued
            /*if (packet.Type != PacketType.Acknowledgement)
            {
                Send(packet.From, new Packet(new Acknowledgement()
                {
                    PacketId = packet.Id
                }));
            }*/

            if (packet.packetCase == Packet.packetOneofCase.Acknowledgement)
            {
                Acknowledgement acknowledgement = packet.Acknowledgement;
                AckReceived?.Invoke(acknowledgement, Guid.Parse(packet.From));
                return;
            }
            else if (packet.packetCase == Packet.packetOneofCase.Command)
            {
                var command = packet.Command;
                if (command.TypeCase == Command.TypeOneofCase.Heartbeat)
                {
                    //No need to do anything, just chill
                    return;
                }
            }

            await Server_PacketReceived_AuthorizedHandler(user, packet);
        }

        private async Task Server_PacketReceived_AuthorizedHandler(ConnectedUser user, Packet packet)
        {
            //Authorization
            //TODO: We can probably split the packet handler down even further into websocket/player
            //Would be better for security, since we can limit the actions websockets/players can take
            if (!AuthorizationService.VerifyUser(packet.Token, user, out var userFromToken))
            {
                //If the user is not an automated connection, trigger authorization from them
                await Send(user.id, new Packet
                {
                    Command = new Command
                    {
                        DiscordAuthorize = oauthServer.GetOAuthUrl(user.id.ToString())
                    }
                });
                return;
            }

            if (packet.packetCase == Packet.packetOneofCase.Command)
            {
                var command = packet.Command;
                if (command.TypeCase == Command.TypeOneofCase.send_bot_message)
                {
                    var sendBotMessage = command.send_bot_message;
                    QualifierBot.SendMessage(sendBotMessage.Channel, sendBotMessage.Message);
                }
            }
            else if (packet.packetCase == Packet.packetOneofCase.Push)
            {
                var push = packet.Push;
                if (push.DataCase == Push.DataOneofCase.song_finished)
                {
                    var finalScore = push.song_finished;

                    await BroadcastToAllClients(packet); //TODO: Should be targeted
                }
            }
            else if (packet.packetCase == Packet.packetOneofCase.Request)
            {
                var request = packet.Request;
                if (request.TypeCase == Request.TypeOneofCase.connect)
                {
                    var connect = request.connect;
                    if (connect.ClientVersion != VERSION_CODE)
                    {
                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                Type = Response.ResponseType.Fail,
                                connect = new Response.Connect
                                {
                                    ServerVersion = VERSION_CODE,
                                    Message = $"Version mismatch, this server is on version {VERSION}",
                                    Reason = Response.Connect.ConnectFailReason.IncorrectVersion
                                },
                                RespondingToPacketId = packet.Id
                            }
                        });
                    }
                    else
                    {
                        //Give the newly connected player the sanitized state

                        //Don't expose tourney info unless the tourney is joined
                        var sanitizedState = new State();
                        sanitizedState.Tournaments.AddRange(StateManager.GetTournaments().Select(x => new Tournament
                        {
                            Guid = x.Guid,
                            Settings = new Tournament.TournamentSettings
                            {
                                TournamentName = x.Settings.TournamentName,
                                TournamentImage = x.Settings.TournamentImage,
                            },
                            Server = x.Server,
                        }));
                        sanitizedState.KnownServers.AddRange(StateManager.GetServers());

                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                Type = Response.ResponseType.Success,
                                connect = new Response.Connect
                                {
                                    State = sanitizedState,
                                    ServerVersion = VERSION_CODE
                                },
                                RespondingToPacketId = packet.Id
                            }
                        });
                    }
                }
                else if (request.TypeCase == Request.TypeOneofCase.join)
                {
                    var join = request.join;
                    var tournament = StateManager.GetTournamentByGuid(join.TournamentId);

                    if (await DatabaseService.TournamentDatabase.VerifyHashedPassword(tournament.Guid, join.Password))
                    {
                        await StateManager.AddUser(tournament.Guid, userFromToken);

                        //Don't expose other tourney info, unless they're part of that tourney too
                        var sanitizedState = new State();
                        sanitizedState.Tournaments.AddRange(
                            StateManager.GetTournaments()
                                .Where(x => !x.Users.ContainsUser(userFromToken))
                                .Select(x => new Tournament
                                {
                                    Guid = x.Guid,
                                    Settings = x.Settings
                                }));

                        //Re-add new tournament, tournaments the user is part of
                        sanitizedState.Tournaments.Add(tournament);
                        sanitizedState.Tournaments.AddRange(StateManager.GetTournaments().Where(x => x.Users.ContainsUser(userFromToken)));
                        sanitizedState.KnownServers.AddRange(StateManager.GetServers());

                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                Type = Response.ResponseType.Success,
                                join = new Response.Join
                                {
                                    SelfGuid = user.id.ToString(),
                                    State = sanitizedState,
                                    TournamentId = tournament.Guid,
                                    Message = $"Connected to {tournament.Settings.TournamentName}!"
                                },
                                RespondingToPacketId = packet.Id
                            }
                        });
                    }
                    else
                    {
                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                Type = Response.ResponseType.Fail,
                                join = new Response.Join
                                {
                                    Message = $"Incorrect password for {tournament.Settings.TournamentName}!",
                                    Reason = Response.Join.JoinFailReason.IncorrectPassword
                                },
                                RespondingToPacketId = packet.Id
                            }
                        });
                    }
                }
                else if (request.TypeCase == Request.TypeOneofCase.qualifier_scores)
                {
                    var scoreRequest = request.qualifier_scores;
                    var scores = DatabaseService.QualifierDatabase.Scores
                        .Where(x => x.MapId == scoreRequest.MapId && !x.Old).OrderByDescending(x => x._Score)
                        .Select(x => new LeaderboardScore
                        {
                            EventId = scoreRequest.EventId,
                            MapId = scoreRequest.MapId,
                            Username = x.Username,
                            PlatformId = x.PlatformId,
                            Score = x._Score,
                            FullCombo = x.FullCombo,
                            Color = x.PlatformId == userFromToken.PlatformId ? "#00ff00" : "#ffffff"
                        });

                    //If scores are disabled for this event, don't return them
                    var @event = DatabaseService.QualifierDatabase.Qualifiers.FirstOrDefault(x => x.Guid == scoreRequest.EventId);
                    if (((QualifierEvent.EventSettings)@event.Flags).HasFlag(QualifierEvent.EventSettings.HideScoresFromPlayers))
                    {
                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                leaderboard_scores = new Response.LeaderboardScores(),
                                RespondingToPacketId = packet.Id
                            }
                        });
                    }
                    else
                    {
                        var scoreRequestResponse = new Response.LeaderboardScores();
                        scoreRequestResponse.Scores.AddRange(scores);

                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                leaderboard_scores = scoreRequestResponse,
                                RespondingToPacketId = packet.Id
                            }
                        });
                    }
                }
                else if (request.TypeCase == Request.TypeOneofCase.submit_qualifier_score)
                {
                    var submitScoreRequest = request.submit_qualifier_score;

                    //Check to see if the song exists in the database
                    var song = DatabaseService.QualifierDatabase.Songs.FirstOrDefault(x => x.Guid == submitScoreRequest.QualifierScore.MapId && !x.Old);
                    if (song != null)
                    {
                        var scores = DatabaseService.QualifierDatabase.Scores.Where(x => x.MapId == submitScoreRequest.QualifierScore.MapId && !x.Old && x.PlatformId == submitScoreRequest.QualifierScore.PlatformId);

                        var oldHighScore = scores.OrderBy(x => x._Score).FirstOrDefault()?._Score ?? -1;
                        if (oldHighScore < submitScoreRequest.QualifierScore.Score)
                        {
                            //Mark all older scores as old
                            foreach (var score in scores)
                            {
                                score.Old = true;
                            }

                            //If the old high score was lower, we'll add a new one
                            DatabaseService.QualifierDatabase.Scores.Add(new Database.Models.Score
                            {
                                MapId = submitScoreRequest.QualifierScore.MapId,
                                EventId = submitScoreRequest.QualifierScore.EventId,
                                PlatformId = submitScoreRequest.QualifierScore.PlatformId,
                                Username = submitScoreRequest.QualifierScore.Username,
                                LevelId = submitScoreRequest.Map.Beatmap.LevelId,
                                Characteristic = submitScoreRequest.Map.Beatmap.Characteristic.SerializedName,
                                BeatmapDifficulty = submitScoreRequest.Map.Beatmap.Difficulty,
                                GameOptions = (int)submitScoreRequest.Map.GameplayModifiers.Options,
                                PlayerOptions = (int)submitScoreRequest.Map.PlayerSettings.Options,
                                _Score = submitScoreRequest.QualifierScore.Score,
                                FullCombo = submitScoreRequest.QualifierScore.FullCombo,
                            });

                            await DatabaseService.QualifierDatabase.SaveChangesAsync();
                        }

                        var newScores = DatabaseService.QualifierDatabase.Scores
                            .Where(x => x.MapId == submitScoreRequest.QualifierScore.MapId && !x.Old).OrderByDescending(x => x._Score)
                            .Select(x => new LeaderboardScore
                            {
                                EventId = submitScoreRequest.QualifierScore.EventId,
                                MapId = submitScoreRequest.QualifierScore.MapId,
                                Username = x.Username,
                                PlatformId = x.PlatformId,
                                Score = x._Score,
                                FullCombo = x.FullCombo,
                                Color = x.PlatformId == userFromToken.PlatformId ? "#00ff00" : "#ffffff"
                            });

                        //Return the new scores for the song so the leaderboard will update immediately
                        //If scores are disabled for this event, don't return them
                        var @event = DatabaseService.QualifierDatabase.Qualifiers.FirstOrDefault(x => x.Guid == submitScoreRequest.QualifierScore.EventId);
                        var hideScores =
                            ((QualifierEvent.EventSettings)@event.Flags).HasFlag(QualifierEvent.EventSettings
                                .HideScoresFromPlayers);
                        var enableLeaderboardMessage =
                            ((QualifierEvent.EventSettings)@event.Flags).HasFlag(QualifierEvent.EventSettings
                                .EnableLeaderboardMessage);

                        var submitScoreResponse = new Response.LeaderboardScores();
                        submitScoreResponse.Scores.AddRange(hideScores ? new LeaderboardScore[] { } : newScores.ToArray());

                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                leaderboard_scores = submitScoreResponse,
                                RespondingToPacketId = packet.Id
                            }
                        });

                        if (oldHighScore < submitScoreRequest.QualifierScore.Score && @event.InfoChannelId != default && !hideScores && QualifierBot != null)
                        {
                            QualifierBot.SendScoreEvent(@event.InfoChannelId, submitScoreRequest.QualifierScore);

                            if (enableLeaderboardMessage)
                            {
                                var eventSongs = DatabaseService.QualifierDatabase.Songs.Where(x => x.EventId == submitScoreRequest.QualifierScore.EventId.ToString() && !x.Old);
                                var eventScores = DatabaseService.QualifierDatabase.Scores.Where(x => x.EventId == submitScoreRequest.QualifierScore.EventId.ToString() && !x.Old);
                                var newMessageId = await QualifierBot.SendLeaderboardUpdate(@event.InfoChannelId, @event.LeaderboardMessageId, eventScores.ToList(), eventSongs.ToList());
                                if (@event.LeaderboardMessageId != newMessageId)
                                {
                                    @event.LeaderboardMessageId = newMessageId;
                                    await DatabaseService.QualifierDatabase.SaveChangesAsync();
                                }
                            }
                        }
                    }
                }
                else if (request.TypeCase == Request.TypeOneofCase.update_user)
                {
                    var updateUser = request.update_user;

                    //TODO: Do permission checks

                    await StateManager.UpdateUser(updateUser.tournamentId, updateUser.User);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            update_user = new Response.UpdateUser
                            {
                                Message = "Successfully updated user"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.create_match)
                {
                    var createMatch = request.create_match;

                    //TODO: Do permission checks

                    await StateManager.CreateMatch(createMatch.tournamentId, createMatch.Match);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            create_match = new Response.CreateMatch
                            {
                                Message = "Successfully created match"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.update_match)
                {
                    var updateMatch = request.update_match;

                    //TODO: Do permission checks

                    await StateManager.UpdateMatch(updateMatch.tournamentId, updateMatch.Match);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            update_match = new Response.UpdateMatch
                            {
                                Message = "Successfully updated match"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.delete_match)
                {
                    var deleteMatch = request.delete_match;

                    //TODO: Do permission checks

                    await StateManager.DeleteMatch(deleteMatch.tournamentId, deleteMatch.Match);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            delete_match = new Response.DeleteMatch
                            {
                                Message = "Successfully deleted match"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.create_qualifier_event)
                {
                    var createQualifierEvent = request.create_qualifier_event;

                    //TODO: Do permission checks

                    await StateManager.CreateQualifier(createQualifierEvent.tournamentId, createQualifierEvent.Event);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            create_qualifier_event = new Response.CreateQualifierEvent
                            {
                                Message = "Successfully created qualifier"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.update_qualifier_event)
                {
                    var updateQualifierEvent = request.update_qualifier_event;

                    //TODO: Do permission checks

                    await StateManager.UpdateQualifier(updateQualifierEvent.tournamentId, updateQualifierEvent.Event);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            update_qualifier_event = new Response.UpdateQualifierEvent
                            {
                                Message = "Successfully updated qualifier"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.delete_qualifier_event)
                {
                    var deleteQualifierEvent = request.delete_qualifier_event;

                    //TODO: Do permission checks

                    await StateManager.DeleteQualifier(deleteQualifierEvent.tournamentId, deleteQualifierEvent.Event);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            delete_qualifier_event = new Response.DeleteQualifierEvent
                            {
                                Message = "Successfully deleted qualifier"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.create_tournament)
                {
                    var createTournament = request.create_tournament;

                    //TODO: Do permission checks

                    await StateManager.CreateTournament(createTournament.Tournament);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            create_tournament = new Response.CreateTournament
                            {
                                Message = "Successfully created tournament"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.update_tournament)
                {
                    var updateTournament = request.update_tournament;

                    //TODO: Do permission checks

                    await StateManager.UpdateTournament(updateTournament.Tournament);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            update_tournament = new Response.UpdateTournament
                            {
                                Message = "Successfully updated tournament"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.delete_tournament)
                {
                    var deleteTournament = request.delete_tournament;

                    //TODO: Do permission checks

                    await StateManager.DeleteTournament(deleteTournament.Tournament);

                    await Send(user.id, new Packet
                    {
                        Response = new Response
                        {
                            Type = Response.ResponseType.Success,
                            RespondingToPacketId = packet.Id,
                            delete_tournament = new Response.DeleteTournament
                            {
                                Message = "Successfully deleted tournament"
                            }
                        }
                    });
                }
                else if (request.TypeCase == Request.TypeOneofCase.add_server)
                {
                    var addServer = request.add_server;

                    //TODO: Do permission checks

                    //To add a server to the master list, we'll need to be sure we can connect to it first. If not, we'll tell the requester why.
                    var newConnection = new TAClient(addServer.Server.Address, addServer.Server.Port);

                    //If we've been provided with a token to use, use it
                    if (!string.IsNullOrWhiteSpace(addServer.AuthToken))
                    {
                        newConnection.SetAuthToken(addServer.AuthToken);
                    }

                    newConnection.ConnectedToServer += async (response) =>
                    {
                        ServerConnections.Add(newConnection);

                        await StateManager.AddServer(addServer.Server);

                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                Type = Response.ResponseType.Success,
                                add_server = new Response.AddServer
                                {
                                    Message = $"Server added to the master list!",
                                },
                                RespondingToPacketId = packet.Id
                            }
                        });
                    };

                    newConnection.AuthorizationRequestedFromServer += async (authRequest) =>
                    {
                        newConnection.Shutdown();

                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                Type = Response.ResponseType.Fail,
                                add_server = new Response.AddServer
                                {
                                    Message = $"Could not connect to your server due to an authorization error. Try adding an auth token in your AddServerToList request",
                                },
                                RespondingToPacketId = packet.Id
                            }
                        });
                    };

                    newConnection.FailedToConnectToServer += async (response) =>
                    {
                        newConnection.Shutdown();

                        await Send(user.id, new Packet
                        {
                            Response = new Response
                            {
                                Type = Response.ResponseType.Fail,
                                add_server = new Response.AddServer
                                {
                                    Message = $"Could not connect to your server. Try connecting directly to your server from TAUI to see if it's accessible from a regular/external setup",
                                },
                                RespondingToPacketId = packet.Id
                            }
                        });
                    };

                    await newConnection.Start();
                }
            }
            else if (packet.packetCase == Packet.packetOneofCase.Response)
            {
                var response = packet.Response;
                if (response.DetailsCase == Response.DetailsOneofCase.show_modal)
                {
                    //await BroadcastToAllClients(packet); //TODO: Should be targeted
                }
            }
            else if (packet.packetCase == Packet.packetOneofCase.ForwardingPacket)
            {
                var forwardingPacket = packet.ForwardingPacket;
                var forwardedPacket = forwardingPacket.Packet;

                await ForwardTo(forwardingPacket.ForwardToes.Select(Guid.Parse).ToArray(), Guid.Parse(packet.From), forwardedPacket);
            }
        }
    }
}