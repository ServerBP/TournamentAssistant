﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TournamentAssistantServer.Database;
using TournamentAssistantServer.Database.Models;
using TournamentAssistantServer.Discord;
using TournamentAssistantServer.PacketService;
using TournamentAssistantServer.PacketService.Attributes;
using TournamentAssistantServer.Utilities;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using TournamentAssistantShared.Utilities;
using static TournamentAssistantShared.Constants;
using Tournament = TournamentAssistantShared.Models.Tournament;

namespace TournamentAssistantServer.PacketHandlers
{
    [Module(Packet.packetOneofCase.Request, "packet.Request.TypeCase")]
    class Requests
    {
        public ExecutionContext ExecutionContext { get; set; }
        public TAServer TAServer { get; set; }
        public StateManager StateManager { get; set; }
        public DatabaseService DatabaseService { get; set; }
        public QualifierBot QualifierBot { get; set; }

        [AllowFromPlayer]
        [AllowFromWebsocket]
        [AllowFromReadonly]
        [PacketHandler((int)Request.TypeOneofCase.connect)]
        public async Task Connect()
        {
            var connect = ExecutionContext.Packet.Request.connect;

            if (connect.ClientVersion != VERSION_CODE)
            {
                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
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
                        RespondingToPacketId = ExecutionContext.Packet.Id
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

                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
                {
                    Response = new Response
                    {
                        Type = Response.ResponseType.Success,
                        connect = new Response.Connect
                        {
                            State = sanitizedState,
                            ServerVersion = VERSION_CODE
                        },
                        RespondingToPacketId = ExecutionContext.Packet.Id
                    }
                });
            }
        }

        [AllowFromPlayer]
        [AllowFromWebsocket]
        [AllowFromReadonly]
        [PacketHandler((int)Request.TypeOneofCase.join)]
        public async Task Join()
        {
            var join = ExecutionContext.Packet.Request.join;

            var tournament = StateManager.GetTournamentByGuid(join.TournamentId);

            using var tournamentDatabase = DatabaseService.NewTournamentDatabaseContext();

            if (tournament == null)
            {
                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
                {
                    Response = new Response
                    {
                        Type = Response.ResponseType.Fail,
                        join = new Response.Join
                        {
                            Message = $"Tournament does not exist!",
                            Reason = Response.Join.JoinFailReason.IncorrectPassword
                        },
                        RespondingToPacketId = ExecutionContext.Packet.Id
                    }
                });
            }
            else if (await tournamentDatabase.VerifyHashedPassword(tournament.Guid, join.Password))
            {
                await StateManager.AddUser(tournament.Guid, ExecutionContext.User);

                //Don't expose other tourney info, unless they're part of that tourney too
                var sanitizedState = new State();
                sanitizedState.Tournaments.AddRange(
                    StateManager.GetTournaments()
                        .Where(x => !x.Users.ContainsUser(ExecutionContext.User))
                        .Select(x => new Tournament
                        {
                            Guid = x.Guid,
                            Settings = x.Settings
                        }));

                //Re-add new tournament, tournaments the user is part of
                sanitizedState.Tournaments.Add(tournament);
                sanitizedState.Tournaments.AddRange(StateManager.GetTournaments().Where(x => StateManager.GetUsers(x.Guid).ContainsUser(ExecutionContext.User)));
                sanitizedState.KnownServers.AddRange(StateManager.GetServers());

                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
                {
                    Response = new Response
                    {
                        Type = Response.ResponseType.Success,
                        join = new Response.Join
                        {
                            SelfGuid = ExecutionContext.User.Guid,
                            State = sanitizedState,
                            TournamentId = tournament.Guid,
                            Message = $"Connected to {tournament.Settings.TournamentName}!"
                        },
                        RespondingToPacketId = ExecutionContext.Packet.Id
                    }
                });
            }
            else
            {
                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
                {
                    Response = new Response
                    {
                        Type = Response.ResponseType.Fail,
                        join = new Response.Join
                        {
                            Message = $"Incorrect password for {tournament.Settings.TournamentName}!",
                            Reason = Response.Join.JoinFailReason.IncorrectPassword
                        },
                        RespondingToPacketId = ExecutionContext.Packet.Id
                    }
                });
            }
        }

        [AllowFromPlayer]
        [AllowFromWebsocket]
        [AllowFromReadonly]
        [PacketHandler((int)Request.TypeOneofCase.qualifier_scores)]
        public async Task GetQualifierScores()
        {
            using var qualifierDatabase = DatabaseService.NewQualifierDatabaseContext();

            var scoreRequest = ExecutionContext.Packet.Request.qualifier_scores;
            var @event = qualifierDatabase.Qualifiers.FirstOrDefault(x => !x.Old && x.Guid == scoreRequest.EventId);

            IQueryable<LeaderboardEntry> scores;

            // If a map was specified, return only scores for that map. Otherwise, return all for the event
            if (!string.IsNullOrEmpty(scoreRequest.MapId))
            {
                scores = qualifierDatabase.Scores
                    .Where(x => x.MapId == scoreRequest.MapId && !x.IsPlaceholder && !x.Old)
                    .OrderByQualifierSettings((QualifierEvent.LeaderboardSort)@event.Sort)
                    .Select(x => new LeaderboardEntry
                    {
                        EventId = x.EventId,
                        MapId = x.MapId,
                        PlatformId = x.PlatformId,
                        Username = x.Username,
                        MultipliedScore = x.MultipliedScore,
                        ModifiedScore = x.ModifiedScore,
                        MaxPossibleScore = x.MaxPossibleScore,
                        Accuracy = x.Accuracy,
                        NotesMissed = x.NotesMissed,
                        BadCuts = x.BadCuts,
                        GoodCuts = x.GoodCuts,
                        MaxCombo = x.MaxCombo,
                        FullCombo = x.FullCombo,
                        Color = x.PlatformId == ExecutionContext.User.PlatformId ? "#00ff00" : "#ffffff"
                    });
            }
            else
            {
                scores = qualifierDatabase.Scores
                    .Where(x => x.EventId == scoreRequest.EventId && !x.IsPlaceholder && !x.Old)
                    .OrderByQualifierSettings((QualifierEvent.LeaderboardSort)@event.Sort)
                    .Select(x => new LeaderboardEntry
                    {
                        EventId = x.EventId,
                        MapId = x.MapId,
                        PlatformId = x.PlatformId,
                        Username = x.Username,
                        MultipliedScore = x.MultipliedScore,
                        ModifiedScore = x.ModifiedScore,
                        MaxPossibleScore = x.MaxPossibleScore,
                        Accuracy = x.Accuracy,
                        NotesMissed = x.NotesMissed,
                        BadCuts = x.BadCuts,
                        GoodCuts = x.GoodCuts,
                        MaxCombo = x.MaxCombo,
                        FullCombo = x.FullCombo,
                        Color = x.PlatformId == ExecutionContext.User.PlatformId ? "#00ff00" : "#ffffff"
                    });
            }

            //If scores are disabled for this event, don't return them
            if (((QualifierEvent.EventSettings)@event.Flags).HasFlag(QualifierEvent.EventSettings.HideScoresFromPlayers))
            {
                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
                {
                    Response = new Response
                    {
                        Type = Response.ResponseType.Success,
                        leaderboard_entries = new Response.LeaderboardEntries(),
                        RespondingToPacketId = ExecutionContext.Packet.Id
                    }
                });
            }
            else
            {
                var scoreRequestResponse = new Response.LeaderboardEntries();
                scoreRequestResponse.Scores.AddRange(scores);

                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
                {
                    Response = new Response
                    {
                        Type = Response.ResponseType.Success,
                        leaderboard_entries = scoreRequestResponse,
                        RespondingToPacketId = ExecutionContext.Packet.Id
                    }
                });
            }
        }

        [AllowFromPlayer]
        [PacketHandler((int)Request.TypeOneofCase.submit_qualifier_score)]
        public async Task SubmitQualifierScore()
        {
            using var qualifierDatabase = DatabaseService.NewQualifierDatabaseContext();

            var submitScoreRequest = ExecutionContext.Packet.Request.submit_qualifier_score;
            var @event = qualifierDatabase.Qualifiers.FirstOrDefault(x => !x.Old && x.Guid == submitScoreRequest.QualifierScore.EventId);

            //Check to see if the song exists in the database
            var song = qualifierDatabase.Songs.FirstOrDefault(x => x.Guid == submitScoreRequest.QualifierScore.MapId && !x.Old);
            if (song != null)
            {
                // Returns list of NOT "OLD" scores (usually just the most recent score)
                var scores = qualifierDatabase.Scores.Where(x => x.MapId == submitScoreRequest.QualifierScore.MapId && x.PlatformId == submitScoreRequest.QualifierScore.PlatformId && !x.Old);
                var oldLowScore = scores.OrderByQualifierSettings((QualifierEvent.LeaderboardSort)@event.Sort, true).FirstOrDefault();

                // If limited attempts is enabled
                if (song.Attempts > 0)
                {
                    // If the score is a placeholder score, that indicates an attempt is being made. Written no matter what.
                    if (submitScoreRequest.QualifierScore.IsPlaceholder)
                    {
                        qualifierDatabase.Scores.Add(new Score
                        {
                            MapId = submitScoreRequest.QualifierScore.MapId,
                            EventId = submitScoreRequest.QualifierScore.EventId,
                            PlatformId = submitScoreRequest.QualifierScore.PlatformId,
                            Username = submitScoreRequest.QualifierScore.Username,
                            LevelId = submitScoreRequest.Map.Beatmap.LevelId,
                            MultipliedScore = submitScoreRequest.QualifierScore.MultipliedScore,
                            ModifiedScore = submitScoreRequest.QualifierScore.ModifiedScore,
                            MaxPossibleScore = submitScoreRequest.QualifierScore.MaxPossibleScore,
                            Accuracy = submitScoreRequest.QualifierScore.Accuracy,
                            NotesMissed = submitScoreRequest.QualifierScore.NotesMissed,
                            BadCuts = submitScoreRequest.QualifierScore.BadCuts,
                            GoodCuts = submitScoreRequest.QualifierScore.GoodCuts,
                            MaxCombo = submitScoreRequest.QualifierScore.MaxCombo,
                            FullCombo = submitScoreRequest.QualifierScore.FullCombo,
                            Characteristic = submitScoreRequest.Map.Beatmap.Characteristic.SerializedName,
                            BeatmapDifficulty = submitScoreRequest.Map.Beatmap.Difficulty,
                            GameOptions = (int)submitScoreRequest.Map.GameplayModifiers.Options,
                            PlayerOptions = (int)submitScoreRequest.Map.PlayerSettings.Options,
                            IsPlaceholder = submitScoreRequest.QualifierScore.IsPlaceholder,
                        });

                        qualifierDatabase.SaveChanges();
                    }

                    // If the score isn't a placeholder, but the lowest other score is, then we can replace it with our new attempt's result
                    else if (oldLowScore != null && oldLowScore.IsPlaceholder)
                    {
                        var newScore = new Database.Models.Score
                        {
                            ID = oldLowScore.ID,
                            MapId = submitScoreRequest.QualifierScore.MapId,
                            EventId = submitScoreRequest.QualifierScore.EventId,
                            PlatformId = submitScoreRequest.QualifierScore.PlatformId,
                            Username = submitScoreRequest.QualifierScore.Username,
                            LevelId = submitScoreRequest.Map.Beatmap.LevelId,
                            MultipliedScore = submitScoreRequest.QualifierScore.MultipliedScore,
                            ModifiedScore = submitScoreRequest.QualifierScore.ModifiedScore,
                            MaxPossibleScore = submitScoreRequest.QualifierScore.MaxPossibleScore,
                            Accuracy = submitScoreRequest.QualifierScore.Accuracy,
                            NotesMissed = submitScoreRequest.QualifierScore.NotesMissed,
                            BadCuts = submitScoreRequest.QualifierScore.BadCuts,
                            GoodCuts = submitScoreRequest.QualifierScore.GoodCuts,
                            MaxCombo = submitScoreRequest.QualifierScore.MaxCombo,
                            FullCombo = submitScoreRequest.QualifierScore.FullCombo,
                            Characteristic = submitScoreRequest.Map.Beatmap.Characteristic.SerializedName,
                            BeatmapDifficulty = submitScoreRequest.Map.Beatmap.Difficulty,
                            GameOptions = (int)submitScoreRequest.Map.GameplayModifiers.Options,
                            PlayerOptions = (int)submitScoreRequest.Map.PlayerSettings.Options,
                            IsPlaceholder = submitScoreRequest.QualifierScore.IsPlaceholder,
                            Old = false
                        };

                        qualifierDatabase.Entry(oldLowScore).CurrentValues.SetValues(newScore);

                        // Have to save scores again because if we don't, OrderByDescending will still use the old value for _Score
                        qualifierDatabase.SaveChanges();

                        // At this point, the new score might be lower than the old high score, so let's mark the highest one as newest
                        var highScore = scores.OrderByQualifierSettings((QualifierEvent.LeaderboardSort)@event.Sort).FirstOrDefault();

                        // Mark all older scores as old
                        foreach (var score in scores)
                        {
                            score.Old = true;
                        }

                        // Mark the newer score as new
                        highScore.Old = false;

                        qualifierDatabase.SaveChanges();
                    }

                    // If neither of the above conditions is met, somehow the player is submitting a score without initiating an attempt...
                    // Which is... weird.
                }

                // Write new score to database if it's better than the last one, and limited attempts is not enabled
                else if (oldLowScore == null || oldLowScore.IsNewScoreBetter(submitScoreRequest.QualifierScore, (QualifierEvent.LeaderboardSort)@event.Sort))
                {
                    //Mark all older scores as old
                    foreach (var score in scores)
                    {
                        score.Old = true;
                    }

                    //If the old high score was lower, we'll add a new one
                    qualifierDatabase.Scores.Add(new Score
                    {
                        MapId = submitScoreRequest.QualifierScore.MapId,
                        EventId = submitScoreRequest.QualifierScore.EventId,
                        PlatformId = submitScoreRequest.QualifierScore.PlatformId,
                        Username = submitScoreRequest.QualifierScore.Username,
                        LevelId = submitScoreRequest.Map.Beatmap.LevelId,
                        MultipliedScore = submitScoreRequest.QualifierScore.MultipliedScore,
                        ModifiedScore = submitScoreRequest.QualifierScore.ModifiedScore,
                        MaxPossibleScore = submitScoreRequest.QualifierScore.MaxPossibleScore,
                        Accuracy = submitScoreRequest.QualifierScore.Accuracy,
                        NotesMissed = submitScoreRequest.QualifierScore.NotesMissed,
                        BadCuts = submitScoreRequest.QualifierScore.BadCuts,
                        GoodCuts = submitScoreRequest.QualifierScore.GoodCuts,
                        MaxCombo = submitScoreRequest.QualifierScore.MaxCombo,
                        FullCombo = submitScoreRequest.QualifierScore.FullCombo,
                        Characteristic = submitScoreRequest.Map.Beatmap.Characteristic.SerializedName,
                        BeatmapDifficulty = submitScoreRequest.Map.Beatmap.Difficulty,
                        GameOptions = (int)submitScoreRequest.Map.GameplayModifiers.Options,
                        PlayerOptions = (int)submitScoreRequest.Map.PlayerSettings.Options,
                        IsPlaceholder = submitScoreRequest.QualifierScore.IsPlaceholder,
                    });

                    qualifierDatabase.SaveChanges();
                }

                var newScores = qualifierDatabase.Scores
                    .Where(x => x.MapId == submitScoreRequest.QualifierScore.MapId && !x.IsPlaceholder && !x.Old)
                    .OrderByQualifierSettings((QualifierEvent.LeaderboardSort)@event.Sort)
                    .Select(x => new LeaderboardEntry
                    {
                        EventId = x.EventId,
                        MapId = x.MapId,
                        PlatformId = x.PlatformId,
                        Username = x.Username,
                        MultipliedScore = x.MultipliedScore,
                        ModifiedScore = x.ModifiedScore,
                        MaxPossibleScore = x.MaxPossibleScore,
                        Accuracy = x.Accuracy,
                        NotesMissed = x.NotesMissed,
                        BadCuts = x.BadCuts,
                        GoodCuts = x.GoodCuts,
                        MaxCombo = x.MaxCombo,
                        FullCombo = x.FullCombo,
                        Color = x.PlatformId == ExecutionContext.User.PlatformId ? "#00ff00" : "#ffffff"
                    });

                //Return the new scores for the song so the leaderboard will update immediately
                //If scores are disabled for this event, don't return them
                var hideScores = ((QualifierEvent.EventSettings)@event.Flags).HasFlag(QualifierEvent.EventSettings.HideScoresFromPlayers);
                var enableScoreFeed = ((QualifierEvent.EventSettings)@event.Flags).HasFlag(QualifierEvent.EventSettings.EnableDiscordScoreFeed);
                var enableLeaderboardMessage = ((QualifierEvent.EventSettings)@event.Flags).HasFlag(QualifierEvent.EventSettings.EnableDiscordLeaderboard);

                var submitScoreResponse = new Response.LeaderboardEntries();
                submitScoreResponse.Scores.AddRange(hideScores ? new LeaderboardEntry[] { } : newScores.ToArray());

                await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
                {
                    Response = new Response
                    {
                        Type = Response.ResponseType.Success,
                        RespondingToPacketId = ExecutionContext.Packet.Id,
                        leaderboard_entries = submitScoreResponse
                    }
                });

                //if (@event.InfoChannelId != default && !hideScores && QualifierBot != null)
                if ((oldLowScore == null || oldLowScore.IsNewScoreBetter(submitScoreRequest.QualifierScore, (QualifierEvent.LeaderboardSort)@event.Sort)) && @event.InfoChannelId != default && QualifierBot != null)
                {
                    if (enableScoreFeed)
                    {
                        QualifierBot.SendScoreEvent(@event.InfoChannelId, submitScoreRequest.QualifierScore);
                    }

                    if (enableLeaderboardMessage)
                    {
                        var newMessageId = await QualifierBot.SendLeaderboardUpdate(@event.InfoChannelId, song.LeaderboardMessageId, song.Guid);

                        // In console apps, await might continue on a different thread, so to be sure `song` isn't detached, let's grab a new reference
                        song = qualifierDatabase.Songs.FirstOrDefault(x => x.Guid == submitScoreRequest.QualifierScore.MapId && !x.Old);
                        if (song.LeaderboardMessageId != newMessageId)
                        {
                            File.AppendAllText("leaderboardDebug.txt", $"Saving new messageId: old-{song.LeaderboardMessageId} new-{newMessageId} songName-{song.Name}\n");

                            song.LeaderboardMessageId = newMessageId;
                            qualifierDatabase.SaveChanges();
                        }
                    }
                }
            }
        }

        [AllowFromPlayer]
        [PacketHandler((int)Request.TypeOneofCase.remaining_attempts)]
        public async Task GetReminingAttempts()
        {
            using var qualifierDatabase = DatabaseService.NewQualifierDatabaseContext();

            var remainingAttempts = ExecutionContext.Packet.Request.remaining_attempts;

            var currentAttempts = qualifierDatabase.Scores.Where(x => x.MapId == remainingAttempts.MapId && x.PlatformId == ExecutionContext.User.PlatformId).Count();
            var totalAttempts = qualifierDatabase.Songs.First(x => x.Guid == remainingAttempts.MapId).Attempts;

            await TAServer.Send(Guid.Parse(ExecutionContext.User.Guid), new Packet
            {
                Response = new Response
                {
                    Type = Response.ResponseType.Success,
                    RespondingToPacketId = ExecutionContext.Packet.Id,
                    remaining_attempts = new Response.RemainingAttempts
                    {
                        remaining_attempts = totalAttempts - currentAttempts
                    }
                }
            });
        }
    }
}