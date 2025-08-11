//
//   SelfManagementCommands.cs
//
//   Copyright (c) Christofel authors. All rights reserved.
//   Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Christofel.BaseLib.Extensions;
using Christofel.CommandsLib.Permissions;
using Christofel.CommandsLib.Validator;
using Christofel.Common.Database;
using Christofel.Helpers.Date;
using Christofel.Helpers.Errors;
using Christofel.Helpers.Localization;
using Christofel.Management.Errors;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Extensions;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Extensions.Formatting;
using Remora.Rest.Core;
using Remora.Results;

namespace Christofel.Management.Commands;

/// <summary>
/// A class for commands applied only to self, ie. /selftimeout.
/// This means these commands can be used even by non-moderators.
/// </summary>
[RequirePermission("management.selfmanagement")]
public class SelfManagementCommands : CommandGroup
{
    private readonly IOperationContext _context;
    private readonly IFeedbackService _feedback;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly LocalizedStringLocalizer<ManagementPlugin> _localizer;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ChristofelBaseContext _dbContext;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelfManagementCommands"/> class.
    /// </summary>
    /// <param name="context">Context the command is executed in.</param>
    /// <param name="feedback">The feedback service.</param>
    /// <param name="guildApi">The discord guild api.</param>
    /// <param name="dateTimeProvider">The date time provider.</param>
    /// <param name="localizer">The localizer for localizing textual user messages.</param>
    /// <param name="dbContext">The christofel base database context.</param>
    /// <param name="logger">The logger.</param>
    public SelfManagementCommands(
        IOperationContext context,
        IFeedbackService feedback,
        IDiscordRestGuildAPI guildApi,
        IDateTimeProvider dateTimeProvider,
        LocalizedStringLocalizer<ManagementPlugin> localizer,
        ChristofelBaseContext dbContext,
        ILogger<SelfManagementCommands> logger
    )
    {
        _context = context;
        _feedback = feedback;
        _guildApi = guildApi;
        _localizer = localizer;
        _dateTimeProvider = dateTimeProvider;
        _dbContext = dbContext;
        _logger = logger;
    }

    // TODO: extract the specifications and conversion into a separate class.

    /// <summary>
    /// Specification used in selftimeoutuntil command.
    /// </summary>
    public enum TimeoutUntilSpecification
    {
        /// <summary>
        /// Till the end of today. (midnight).
        /// </summary>
        [Description("End Of Day (next midnight).")]
        EndOfDay,

        /// <summary>
        /// Till the next morning - 6 AM today if it is before 6 AM, otherwise 6 AM tomorrow.
        /// </summary>
        [Description("Following Morning (6:00 today if before 6:00, otherwise tomorrow at 6:00)")]
        FollowingMorning,

        /// <summary>
        /// Till the end of week (start of week + 7 days).
        /// </summary>
        [Description("End Of Week (until next Monday 00:00)")]
        EndOfWeek,

        /// <summary>
        /// Till the end of work week (start of week + 5 days).
        /// </summary>
        [Description("End Of Work Week (next Saturday 00:00 if past Saturday, otherwise this one)")]
        EndOfWorkWeek,

        /// <summary>
        /// Till the end of current month.
        /// </summary>
        [Description("End Of Month (1st of next month, maximum of 28 days)")]
        EndOfMonth,
    }

    /// <summary>
    /// Convert <see cref="TimeoutUntilSpecification" /> into <see cref="DateTimeOffset" />,
    /// based on current time. The method respects the preferred time of users.
    /// </summary>
    /// <param name="specification">The specification to convert into DateTimeOffset.</param>
    /// <returns>The date time in future that the specification corresponds to.</returns>
    public DateTimeOffset SpecificationToDateTimeOffset(TimeoutUntilSpecification specification)
    {
        var now = _dateTimeProvider.PreferredNow;
        var today = new DateTimeOffset(now.Date, now.Offset);

        int dayOfWeek = ((int)today.DayOfWeek + 6) % 7; // start with monday
        var startOfWeek = today.AddDays(-dayOfWeek);
        var startOfMonth = today.AddDays(-(int)today.Day + 1);
        switch (specification)
        {
            case TimeoutUntilSpecification.EndOfDay:
                return today.AddDays(1);
            case TimeoutUntilSpecification.FollowingMorning:
                var morning = today.AddHours(6);
                if (morning < now)
                {
                    morning = morning.AddDays(1);
                }

                return morning;
            case TimeoutUntilSpecification.EndOfWeek:
                return startOfWeek.AddDays(7);
            case TimeoutUntilSpecification.EndOfWorkWeek:
                var endOfWorkWeek = startOfWeek.AddDays(5);

                // already past Friday, next week.
                if (endOfWorkWeek < now)
                {
                    endOfWorkWeek = endOfWorkWeek.AddDays(7);
                }

                return endOfWorkWeek;
            case TimeoutUntilSpecification.EndOfMonth:
                return startOfMonth.AddDays(DateTime.DaysInMonth(startOfMonth.Year, startOfMonth.Month));
        }

        throw new UnreachableException();
    }

    /// <summary>
    /// Like <see cref="HandleSelfTimeoutAsync"/>, but calculates commonly requested
    /// times for timeout durations.
    /// </summary>
    /// <param name="until">The specification that says when the timeout expires, out of common enum values.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    [Command("selftimeoutuntil")]
    [Description("Timeout self until given time like rest of day.")]
    [RequirePermission("management.selfmanagement.selftimeout")]
    public async Task<IResult> HandleSelfTimeoutUntil
        (
            [Description("When the timeout should end.")]
            TimeoutUntilSpecification until
        )
    {
        DateTimeOffset timeoutUntil = SpecificationToDateTimeOffset(until);

        if (timeoutUntil < _dateTimeProvider.UtcNow)
        {
            return await _feedback.SendContextualErrorAsync("The specified time has already passed.");
        }

        // Maximum reached, round.
        if ((timeoutUntil - _dateTimeProvider.UtcNow).TotalDays > 28)
        {
            timeoutUntil = _dateTimeProvider.UtcNow.AddDays(28);
        }

        return await SelfTimeout(timeoutUntil);
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

        DateTimeOffset timeoutUntil = DateTime.Now + duration;

        return await SelfTimeout(timeoutUntil);
    }

    /// <summary>
    /// The user assigns themselves a timeout for arbitrary duration from 1s to 28d (maximal lenght of timeout).
    /// </summary>
    /// <param name="duration">The duration to timeout for.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    [Command("selfban")]
    [RequirePermission("management.selfmanagement.selftban")]
    [Description("\"Ban\" self for given duration. Supports units: w - weeks, d - days, h - hours, m - mins, s - secs")]
    public async Task<IResult> HandleSelfBanAsync(
        [Description("Formatted duration of the ban, ie. 1h30m, 1d20h30m10s")] TimeSpan duration
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

        DateTimeOffset timeoutUntil = DateTime.Now + duration;

        return await SelfBan(timeoutUntil, duration);
    }

    private string FormatTimeSpan(TimeSpan span)
    {
        var formatted = new StringBuilder();

        if (span.Days > 0)
        {
            formatted.Append($"{span.Days}d ");
        }
        if (span.Hours > 0)
        {
            formatted.Append($"{span.Hours}h ");
        }
        if (span.Minutes > 0)
        {
            formatted.Append($"{span.Minutes}m ");
        }
        if (span.Seconds > 0)
        {
            formatted.Append($"{span.Seconds}s");
        }

        return formatted.ToString();
    }

    private async Task<IResult> SelfTimeout(DateTimeOffset timeoutUntil)
    {
        var result = await SelfTimeoutBanCommon(timeoutUntil);

        if (!result.IsSuccess)
        {
            return result;
        }
        _context.TryGetUserID(out var userId);

        // Print: The user has assigned themselves timeout for {duration} until {timeoutUntil}
        return await _feedback.SendContextualSuccessAsync(
            _localizer.Translate(
                "SELFTIMEOUT_SUCCESSFUL",
                $"<@{userId}>",
                Markdown.Timestamp(timeoutUntil, TimestampStyle.RelativeTime),
                Markdown.Timestamp(timeoutUntil, TimestampStyle.ShortDateTime)));
    }

    private async Task<IResult> SelfTimeoutBanCommon(DateTimeOffset timeoutUntil)
    {
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
        var result = await _guildApi.ModifyGuildMemberAsync
            (
                guildId,
                userId,
                communicationDisabledUntil: timeoutUntil,
                reason: "Self-timeout",
                ct: CancellationToken
            );

        if (!result.IsSuccess)
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "There was an error when setting the timeout.",
                ct: CancellationToken);
        }
        return result;
    }

    private async Task<IResult> SelfBan(DateTimeOffset timeoutUntil, TimeSpan duration)
    {
        var result = await SelfTimeoutBanCommon(timeoutUntil);

        if (!result.IsSuccess)
        {
            return result;
        }
        _context.TryGetUserID(out var userId);
        _context.TryGetGuildID(out var guildId);

        var mutedRole = await _dbContext.SpecificRoleAssignments
                .AsNoTracking()
                .Where(x => x.Name == "Muted")
                .Include(x => x.Assignment)
                .Select
                (
                    x => x.Assignment.RoleId
                )
                .FirstOrDefaultAsync(CancellationToken);

        // TODO log into DB, when roles should be reversed
        result = await AssignRole(guildId, userId, mutedRole, ct: CancellationToken.None);

        if (!result.IsSuccess)
        {
            // Error intentionally ignored.
            await _feedback.SendContextualErrorAsync(
                "There was an error when assigning muted role.",
                ct: CancellationToken.None);
            return result;
        }

        var userVerified =
            await _dbContext.Users
                .AsQueryable()
                .Authenticated()
                .Where(x => x.DiscordId == userId)
                .AnyAsync(CancellationToken.None);

        var memberResult = await _guildApi.GetGuildMemberAsync(guildId, userId, ct: CancellationToken.None);
        if (!memberResult.IsDefined(out var member))
        {
            return Result.FromError(memberResult);
        }

        var verifiedRole = await _dbContext.SpecificRoleAssignments
            .AsNoTracking()
            .Where(x => x.Name == "Verified")
            .Include(x => x.Assignment)
            .Select
            (
                x => x.Assignment.RoleId
            )
            .FirstOrDefaultAsync(CancellationToken.None);

        var memberRoles = member.Roles;

        if (userVerified && memberRoles.Contains(verifiedRole))
        {
            result = await DeassignRole(guildId, userId, verifiedRole, ct: CancellationToken.None);

            if (!result.IsSuccess)
            {
                // Error intentionally ignored.
                await _feedback.SendContextualErrorAsync(
                    "There was an error when deasigning verified role.",
                    ct: CancellationToken.None);
                return result;
            }
        }

        Task.Run
        (
            async () =>
            {
                var canceled = false;
                try
                {
                    await Task.Delay(duration);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    _logger.LogDebug("Selfban remove was canceled");
                }

                if (!canceled)
                {
                    for (var i = 0; i < 10; i++)
                    {
                        result = await DeassignRole(guildId, userId, mutedRole, ct: CancellationToken.None);

                        if (result.IsSuccess)
                        {
                            i = 10;
                        }
                        await Task.Delay(60000); // wait one minute before retry
                    }

                    if (!result.IsSuccess)
                    {
                        // Error intentionally ignored.
                        await _feedback.SendContextualErrorAsync(
                            "There was an error when deasigning verified role.",
                            ct: CancellationToken.None);
                        return result;
                    }

                    var userVerifiedLoc =
                        await _dbContext.Users
                            .AsQueryable()
                            .Authenticated()
                            .Where(x => x.DiscordId == userId)
                            .AnyAsync(CancellationToken.None);

                    if (userVerifiedLoc)
                    {
                        for (var i = 0; i < 10; i++)
                        {
                            result = await AssignRole(guildId, userId, verifiedRole, ct: CancellationToken.None);

                            if (result.IsSuccess)
                            {
                                i = 10;
                            }

                            await Task.Delay(60000); // wait one minute before retry
                        }

                        if (!result.IsSuccess)
                        {
                            // Error intentionally ignored.
                            await _feedback.SendContextualErrorAsync
                            (
                                "There was an error when deasigning verified role.",
                                ct: CancellationToken.None
                            );
                            return result;
                        }
                    }

                    // TODO remove log from DB
                }
                return result;
            }
        );

        /* TODO somewhere else
         * on bot start check DB and execute role reverse, which shiould have already been done and log it
         */

        // Print: The user has assigned themselves timeout for {duration} until {timeoutUntil}
        return await _feedback.SendContextualSuccessAsync(
            _localizer.Translate(
                "SELFBAN_SUCCESSFUL",
                $"<@{userId}>",
                Markdown.Timestamp(timeoutUntil, TimestampStyle.RelativeTime),
                Markdown.Timestamp(timeoutUntil, TimestampStyle.ShortDateTime)));
    }

    private Task<Result> AssignRole
        (
            Snowflake guildId,
            Snowflake userId,
            Snowflake roleId,
            CancellationToken ct
        )
            => _guildApi.AddGuildMemberRoleAsync
            (
                guildId,
                userId,
                roleId,
                "Self ban",
                ct
            );

    private Task<Result> DeassignRole
        (
            Snowflake guildId,
            Snowflake userId,
            Snowflake roleId,
            CancellationToken ct
        )
            => _guildApi.RemoveGuildMemberRoleAsync
            (
                guildId,
                userId,
                roleId,
                "Self ban",
                ct
            );
}
