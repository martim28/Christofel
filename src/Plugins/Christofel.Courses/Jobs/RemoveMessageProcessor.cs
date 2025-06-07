//
// RemoveMessageProcessor.cs
//
// Copyright (c) Christofel authors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Christofel.Courses.Data;
using Christofel.Helpers.JobQueue;
using Christofel.Plugins.Lifetime;
using Microsoft.Extensions.Logging;
using Remora.Discord.Interactivity.Services;
using Remora.Rest.Core;

namespace Christofel.Courses.Jobs;

/// <summary>
/// A queue for removing <see cref="CoursesAssignMessage"/> from memory data service.
/// </summary>
public class RemoveMessageProcessor : ThreadPoolJobQueue<RemoveMessageProcessor.RemoveMessage>
{
    /// <summary>
    /// The time to keep the information about courses message in memory service in seconds.
    /// </summary>
    private const int KEEP_MEMORY_COURSES_TIME = 20 * 60 * 1000;

    private readonly InMemoryDataService<Snowflake, CoursesAssignMessage> _memoryDataService;
    private readonly ICurrentPluginLifetime _lifetime;
    private readonly ILogger _logger;

    /// <inheritdoc cref="ThreadPoolJobQueue{T}"/>
    public RemoveMessageProcessor
    (
        InMemoryDataService<Snowflake, CoursesAssignMessage> memoryDataService,
        ICurrentPluginLifetime lifetime,
        ILogger<RemoveMessageProcessor> logger
    )
        : base(lifetime, logger)
    {
        _memoryDataService = memoryDataService;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ProcessAssignJob(RemoveMessage job)
    {
        var endTime = job.AddedTime.AddMilliseconds(KEEP_MEMORY_COURSES_TIME);
        if (DateTime.Now < endTime)
        {
            await Task.Delay(endTime - DateTime.Now, _lifetime.Stopping);
        }

        var leaseResult = await _memoryDataService.LeaseDataAsync(job.MessageId);
        if (!leaseResult.IsDefined(out var leaseData))
        {
            _logger.LogWarning("Couldn't find a message in the memory data service.");
            return;
        }

        if (!await _memoryDataService.TryDeleteDataAsync(leaseData))
        {
            _logger.LogWarning("Couldn't delete a message from the memory data service.");
        }
    }

    /// <summary>
    /// The job for <see cref="RemoveMessageProcessor"/>.
    /// </summary>
    /// <param name="MessageId">The id of the message to remove from the memory.</param>
    /// <param name="AddedTime">The time the message has been addedd..</param>
    public record RemoveMessage
    (
        Snowflake MessageId,
        DateTime AddedTime
    );
}
