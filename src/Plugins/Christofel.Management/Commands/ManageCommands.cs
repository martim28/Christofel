//
//  ManageCommands.cs
//
//  Copyright (c) Christofel authors. All rights reserved.
//  Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Christofel.CommandsLib.Permissions;
using Christofel.CommandsLib.Validator;
using Christofel.Helpers.Errors;
using FluentValidation;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Extensions;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Rest.Core;
using Remora.Results;

namespace Christofel.Management.Commands;

/// <summary>
/// Manage guild members, ie. timeout them.
/// </summary>
[Group("manage")]
[RequirePermission("management.manage")]
[DiscordDefaultMemberPermissions(DiscordPermission.ManageMessages)]
[Ephemeral]
public class ManageCommands : CommandGroup
{
    private readonly FeedbackService _feedback;
    private readonly IOperationContext _context;
    private readonly IDiscordRestGuildAPI _guildApi;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManageCommands"/> class.
    /// </summary>
    /// <param name="feedback">The feedback service for giving feedback to user.</param>
    /// <param name="context">The context of this operation.</param>
    /// <param name="guildApi">The discord guild api.</param>
    public ManageCommands
        (
            FeedbackService feedback,
            IOperationContext context,
            IDiscordRestGuildAPI guildApi
        )
    {
        _feedback = feedback;
        _context = context;
        _guildApi = guildApi;
    }

    /// <summary>
    /// Timeout given member.
    /// </summary>
    /// <param name="user">The user to timeout. Cannot time out self.</param>
    /// <param name="duration">The duration to timeout the user for.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    [Command("timeout")]
    [RequirePermission("management.manage.timeout")]
    [Description("Timeout given guild member.")]
    public async Task<IResult> HandleTimeout
        (
            [DiscordTypeHint(TypeHint.User)]
            [Description("The user to timeout.")]
            Snowflake user,
            [Description("Formatted duration of the timeout, ie. 1h30m, 1d20h30m10s")]
            TimeSpan duration
        )
    {
        // 1. validate lower than 28 days (maximum), greater than 0
        var validationResult = new CommandValidator()
            .MakeSure("interval", duration.TotalDays, o => o.GreaterThan(0).LessThanOrEqualTo(28))
            .Validate()
            .GetResult();

        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        if (!_context.TryGetUserID(out var execUserId))
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "Couldn't find your user id, this is a bug. Aborting.",
                ct: CancellationToken);
            return Result.FromError(
                new UnexpectedContextError(
                    nameof(HandleTimeout),
                    "UserID"));
        }

        if (user == execUserId)
        {
            return await _feedback
                .SendContextualErrorAsync("Sorry, cannot timeout yourself.", ct: CancellationToken);
        }

        if (!_context.TryGetGuildID(out var guildId))
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "It seems that you're not executing this command in a guild. The /manage commands work only in guilds.",
                ct: CancellationToken);
            return Result.FromError(
                new UnexpectedContextError(
                    nameof(HandleTimeout),
                    "GuildID"));
        }

        DateTimeOffset timeoutUntil = DateTime.Now + duration;
        var result = await _guildApi.ModifyGuildMemberAsync(
            guildId,
            user,
            communicationDisabledUntil: timeoutUntil,
            reason: "Moderator used /manage timeout",
            ct: CancellationToken
        );

        if (!result.IsSuccess)
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "There was an error when setting the timeout.",
                ct: CancellationToken);
            return result;
        }

        return await _feedback.SendContextualSuccessAsync
            (
                $"Successfully assigned timeout to user <@{user}> until {timeoutUntil}."
            );
    }
}
