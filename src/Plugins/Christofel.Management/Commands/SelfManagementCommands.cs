//
//   SelfManagementCommands.cs
//
//   Copyright (c) Christofel authors. All rights reserved.
//   Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Christofel.CommandsLib.Permissions;
using Christofel.CommandsLib.Validator;
using Christofel.Helpers.Errors;
using Christofel.Helpers.Localization;
using Christofel.Management;
using Christofel.Management.Errors;
using FluentValidation;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Extensions;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Results;

/// <summary>
/// A class for commands applied only to self, ie. /selftimeout.
/// This means these commands can be used even by non-moderators.
/// </summary>
[RequirePermission("management.selfmanagement")]
public class SelfManagementCommands : CommandGroup
{
    private readonly IOperationContext _context;
    private readonly FeedbackService _feedback;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly LocalizedStringLocalizer<ManagementPlugin> _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelfManagementCommands"/> class.
    /// </summary>
    /// <param name="context">Context the command is executed in.</param>
    /// <param name="feedback">The feedback service.</param>
    /// <param name="guildApi">The discord guild api.</param>
    /// <param name="localizer">The localizer for localizing textual user messages.</param>
    public SelfManagementCommands(
        IOperationContext context,
        FeedbackService feedback,
        IDiscordRestGuildAPI guildApi,
        LocalizedStringLocalizer<ManagementPlugin> localizer
    )
    {
        _context = context;
        _feedback = feedback;
        _guildApi = guildApi;
        _localizer = localizer;
    }

    /// <summary>
    /// The user assigns themselves a timeout for arbitrary duration from 1s to 28d (maximum supported by Discord).
    /// </summary>
    /// <param name="duration">The duration to timeout for.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    [Command("selftimeout")]
    [RequirePermission("management.selfmanagement.selftimeout")]
    [Description("Timeout self for given duration. Supports units: w - weeks, d - days, h - hours, m - mins, s - secs")]
    public async Task<IResult> HandleSelfTimeoutAsync(
        [Description("Formatted duration of the timeout, ie. 1h30m, 1d20h30m10s")] TimeSpan duration
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

        if (!_context.TryGetUserID(out var userId))
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "Couldn't find your user id, this is a bug. Aborting.",
                ct: CancellationToken);
            return Result.FromError(
                new UnexpectedContextError(
                    nameof(HandleSelfTimeoutAsync),
                    "UserID"));
        }

        // 2. Load the guild for channel where the command has been issued
        if (!_context.TryGetGuildID(out var guildId))
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "It seems that you're not executing this command in a guild. The /selftimeout command works only in guilds.",
                ct: CancellationToken);
            return Result.FromError(
                new UnexpectedContextError(
                    nameof(HandleSelfTimeoutAsync),
                    "GuildID"));
        }

        // 3. Check the user doesn't have timeout in this guild.
        // If they do, abort
        // This is a sanity check. This should't really be possible - the user cannot use commands when they have timeout, right?
        var memberResult = await _guildApi.GetGuildMemberAsync(guildId, userId, ct: CancellationToken);

        if (!memberResult.IsDefined(out var member))
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "Couldn't retrieve information about you. Aborting.", ct: CancellationToken);
            return memberResult;
        }

        if (member.CommunicationDisabledUntil.IsDefined(out var currentTimeoutUntil)
            && currentTimeoutUntil > DateTime.Now)
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync("You already do have a timeout, aborting.");
            return Result.FromError(
                new SelfManagementError(nameof(HandleSelfTimeoutAsync), "Already has timeout"));
        }

        // 4. Give them timeout for the given duration
        DateTimeOffset timeoutUntil = DateTime.Now + duration;

        var result = await _guildApi.ModifyGuildMemberAsync(
            guildId,
            userId,
            communicationDisabledUntil: timeoutUntil,
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

        // Print: The user has assigned themselves timeout for {duration} until {timeoutUntil}
        // TODO: figure out localization of DateTime and TimeSpan
        return await _feedback.SendContextualInfoAsync(
            _localizer.Translate(
                "SELFTIMEOUT_SUCCESSFUL",
                duration.ToString(),
                timeoutUntil.ToString()));
    }
}
