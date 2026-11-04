using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AirTimes.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AirTimes.ScheduledTasks;

/// <summary>
/// Re-clamps recently added episodes' premiere dates against their date added.
/// </summary>
public class FixRecentPremiereDatesTask(ILibraryManager libraryManager, ILoggerFactory loggerFactory) : IScheduledTask
{
    private const int RecentItemScanDays = 30;
    private readonly ILogger<FixRecentPremiereDatesTask> logger = loggerFactory.CreateLogger<FixRecentPremiereDatesTask>();

    /// <inheritdoc/>
    public string Name => "Fix Recent Episode Premiere Dates";

    /// <inheritdoc/>
    public string Key => "AirTimesFixRecentPremiereDates";

    /// <inheritdoc/>
    public string Description => "Hourly check that ensures recently added episodes' premiere dates are not later than the date their files were added.";

    /// <inheritdoc/>
    public string Category => "Air Times";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var minDateCreated = DateTime.UtcNow.AddDays(-RecentItemScanDays);

        var queryResult = libraryManager.GetItemsResult(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
            MinDateCreated = minDateCreated
        });

        var episodes = queryResult.Items.OfType<Episode>().ToList();

        var total = episodes.Count;
        var current = 0;
        var updated = 0;

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            progress.Report(total == 0 ? 100 : (double)current / total * 100);

            if (!episode.PremiereDate.HasValue)
            {
                continue;
            }

            var dateAdded = GetDateAdded(episode);
            var clamped = PremiereDateHelper.ClampFutureDate(episode.PremiereDate.Value, dateAdded);

            if (clamped == episode.PremiereDate.Value)
            {
                continue;
            }

            logger.LogInformation(
                "[AirTimes] Clamping recent PremiereDate for \"{SeriesName}\" - \"{Name}\": {Old} -> {New}",
                episode.SeriesName,
                episode.Name,
                episode.PremiereDate.Value,
                clamped);

            episode.PremiereDate = clamped;
            await episode.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
            updated++;
        }

        logger.LogInformation(
            "[AirTimes] Checked {EpisodeCount} episodes added in the last {RecentItemScanDays} days; updated {UpdatedCount}",
            total,
            RecentItemScanDays,
            updated);
    }

    private static DateTime? GetDateAdded(Episode episode)
    {
        if (episode.DateCreated != default)
        {
            return episode.DateCreated;
        }

        return !string.IsNullOrWhiteSpace(episode.Path) && File.Exists(episode.Path)
            ? File.GetCreationTimeUtc(episode.Path)
            : null;
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(1).Ticks
            }
        ];
    }
}
