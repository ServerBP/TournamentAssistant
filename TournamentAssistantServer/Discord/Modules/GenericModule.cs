﻿using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TournamentAssistantServer.Database;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using static Microsoft.EntityFrameworkCore.Internal.AsyncLock;

/**
 * Created by Moon on 5/18/2019
 * A Discord.NET module for basic bot functionality, not necessarily relating to Beat Saber
 */

namespace TournamentAssistantServer.Discord.Modules
{
    public class GenericModule : InteractionModuleBase<SocketInteractionContext>
    {
        public DatabaseService DatabaseService { get; set; }

        public class OngoingInteractionInfo
        {
            public string TournamentId;
            public List<IMentionable> Roles { get; set; } = new List<IMentionable>();
            public string AccessType { get; set; } = string.Empty;
        }

        // Ongoing interactions, keyed by userId
        public static Dictionary<string, OngoingInteractionInfo> OngoingInteractions = new Dictionary<string, OngoingInteractionInfo>();

        [SlashCommand("authorize", "Authorize users in your tournament")]
        public async Task Authorize()
        {
            var tournamentDatabase = DatabaseService.NewTournamentDatabaseContext();
            var tournaments = tournamentDatabase.GetTournamentsWhereUserIsAdmin(Context.User.Id.ToString());

            if (tournaments.Count == 0)
            {
                await RespondAsync("You are not Admin of any tournament", ephemeral: true);
            }
            else if (tournaments.Count == 1)
            {
                OngoingInteractions.Add(Context.User.Id.ToString(), new OngoingInteractionInfo
                {
                    TournamentId = tournaments.First().Guid
                });
            }
            else
            {
                OngoingInteractions.Add(Context.User.Id.ToString(), new OngoingInteractionInfo());
            }

            await RespondAsync(components: BuildComponents(OngoingInteractions[Context.User.Id.ToString()]), ephemeral: true);
        }

        [ComponentInteraction("add_button")]
        public async Task AddButtonInteracted()
        {
            if (!OngoingInteractions.TryGetValue(Context.User.Id.ToString(), out var ongoingInteractionInfo))
            {
                await RespondAsync("Interaction has expired! Try running `/authorize` again", ephemeral: true);
            }
            else if (ongoingInteractionInfo.TournamentId == null || ongoingInteractionInfo.Roles.Count < 1 || string.IsNullOrWhiteSpace(ongoingInteractionInfo.AccessType))
            {
                await RespondAsync("Please select a Tournament, a user/role to add, and an access level", ephemeral: true);
            }
            else
            {
                var tournamentDatabase = DatabaseService.NewTournamentDatabaseContext();

                void addUserPerm(string userId)
                {
                    // TODO: It seems we can't have much selectivity here,
                    // ie: we can't pick admin without view. It's not really a
                    // problem for now, but it's different than how TAUI handles this.
                    // Revisit this if discord ever adds more component options
                    var permissions = Permissions.None;
                    switch (ongoingInteractionInfo.AccessType)
                    {
                        case "view":
                            permissions = Permissions.View;
                            break;
                        case "admin":
                            permissions = Permissions.View | Permissions.Admin;
                            break;
                        default:
                            permissions = Permissions.None;
                            break;
                    }
                    tournamentDatabase.AddAuthorizedUser(ongoingInteractionInfo.TournamentId, userId, permissions);
                }

                foreach (var userOrRole in ongoingInteractionInfo.Roles)
                {
                    Logger.Debug(userOrRole.GetType().ToString());

                    if (userOrRole is SocketGuildUser user)
                    {
                        Logger.Debug($"Adding: {user.DisplayName}");
                        addUserPerm(user.Id.ToString());
                    }
                    else if (userOrRole is SocketRole role)
                    {
                        // TODO: I really hope no one runs this in the BSMG
                        var allUsers = await Context.Guild.GetUsersAsync().FlattenAsync();
                        foreach (var roleUser in allUsers.Where(x => x.RoleIds.Contains(role.Id)))
                        {
                            Logger.Debug($"Adding: {roleUser.DisplayName}");
                            addUserPerm(roleUser.Id.ToString());
                        }
                    }
                }

                OngoingInteractions.Remove(Context.User.Id.ToString());

                await DeferAsync();
                await Context.Interaction.ModifyOriginalResponseAsync((original) =>
                {
                    original.Content = $"Success! {Emoji.Parse(":white_check_mark:")}";
                    original.Components = null;
                });
            }
        }

        [ComponentInteraction("tournament_id_select")]
        public async Task TournamentIdSelectInteracted(string[] tournamentId)
        {
            if (!OngoingInteractions.TryGetValue(Context.User.Id.ToString(), out var ongoingInteractionInfo))
            {
                await RespondAsync("Interaction has expired! Try running `/authorize` again", ephemeral: true);
            }
            else
            {
                ongoingInteractionInfo.TournamentId = tournamentId.FirstOrDefault();

                await DeferAsync();
                await Context.Interaction.ModifyOriginalResponseAsync((messageProperties) => UpdateComponentVisibility(ongoingInteractionInfo, messageProperties));
            }
        }

        [ComponentInteraction("mentionable_select")]
        public async Task MentionableSelectInteracted(IMentionable[] selectedMentionables)
        {
            if (!OngoingInteractions.TryGetValue(Context.User.Id.ToString(), out var ongoingInteractionInfo))
            {
                await RespondAsync("Interaction has expired! Try running `/authorize` again", ephemeral: true);
            }
            else
            {
                ongoingInteractionInfo.Roles.Clear();
                ongoingInteractionInfo.Roles.AddRange(selectedMentionables);

                await DeferAsync();
                await Context.Interaction.ModifyOriginalResponseAsync((messageProperties) => UpdateComponentVisibility(ongoingInteractionInfo, messageProperties));
            }
        }

        [ComponentInteraction("access_type_select")]
        public async Task AccessTypeSelectInteracted(string[] accessType)
        {
            if (!OngoingInteractions.TryGetValue(Context.User.Id.ToString(), out var ongoingInteractionInfo))
            {
                await RespondAsync("Interaction has expired! Try running `/authorize` again", ephemeral: true);
            }
            else
            {
                ongoingInteractionInfo.AccessType = accessType.FirstOrDefault();

                await DeferAsync();
                await Context.Interaction.ModifyOriginalResponseAsync((messageProperties) => UpdateComponentVisibility(ongoingInteractionInfo, messageProperties));
            }
        }

        private MessageComponent BuildComponents(OngoingInteractionInfo ongoingInteractionInfo)
        {
            var tournamentDatabase = DatabaseService.NewTournamentDatabaseContext();
            var tournaments = tournamentDatabase.GetTournamentsWhereUserIsAdmin(Context.User.Id.ToString());

            var showAccessTypeSelect = ongoingInteractionInfo.Roles.Count > 0;
            var showAddButton = ongoingInteractionInfo.Roles.Count > 0 && !string.IsNullOrWhiteSpace(ongoingInteractionInfo.AccessType);

            var tournamentSelectBuilder = new SelectMenuBuilder()
                .WithCustomId("tournament_id_select")
                .WithMinValues(1)
                .WithMaxValues(1)
                .WithPlaceholder("Select tournament for which to authorize these users");

            if (tournaments.Count == 0)
            {
                return null;
            }

            var options = tournaments.Select(x => new SelectMenuOptionBuilder().WithLabel(x.Name).WithValue(x.Guid)).ToList();
            tournamentSelectBuilder = tournamentSelectBuilder.WithOptions(options);

            if (tournaments.Count == 1)
            {
                tournamentSelectBuilder.Options.First().IsDefault = true;
                tournamentSelectBuilder = tournamentSelectBuilder.WithDisabled(true);
            }
            else if (ongoingInteractionInfo.TournamentId != null)
            {
                tournamentSelectBuilder.Options.First(x => x.Value == ongoingInteractionInfo.TournamentId).IsDefault = true;
                tournamentSelectBuilder = tournamentSelectBuilder.WithDisabled(true);
            }

            var mentionableSelectBuilder = new SelectMenuBuilder()
                .WithCustomId("mentionable_select")
                .WithType(ComponentType.MentionableSelect)
                .WithMinValues(1)
                .WithMaxValues(25)
                .WithPlaceholder("Select a user or role")
                .WithDisabled(ongoingInteractionInfo.TournamentId == null);

            var accessTypeSelectBuilder = new SelectMenuBuilder()
                .WithCustomId("access_type_select")
                .AddOption("View / Participate", "view", isDefault: "view" == ongoingInteractionInfo.AccessType)
                .AddOption("Admin", "admin", isDefault: "admin" == ongoingInteractionInfo.AccessType)
                .WithPlaceholder("Select a level of access")
                .WithDisabled(!showAccessTypeSelect);

            var buttonBuilder = new ButtonBuilder()
                .WithCustomId("add_button")
                .WithLabel("Add")
                .WithStyle(ButtonStyle.Success)
                .WithDisabled(!showAddButton);

            return new ComponentBuilder()
                .WithSelectMenu(tournamentSelectBuilder)
                .WithSelectMenu(mentionableSelectBuilder)
                .WithSelectMenu(accessTypeSelectBuilder)
                .WithButton(buttonBuilder)
                .Build();
        }

        private void UpdateComponentVisibility(OngoingInteractionInfo ongoingInteractionInfo, MessageProperties messageProperties)
        {
            messageProperties.Components = BuildComponents(ongoingInteractionInfo);
        }
    }
}
