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
/// Re-clamps every episode's premiere date against its date added.
/// Scans all episodes and is intended to be run manually.
/// </summary>
public class FixPremiereDatesTask(ILibraryManager libraryManager, ILoggerFactory loggerFactory) : IScheduledTask
{
    private readonly ILogger<FixPremiereDatesTask> logger = loggerFactory.CreateLogger<FixPremiereDatesTask>();

    /// <inheritdoc/>
    public string Name => "Fix All Episode Premiere Dates";

    /// <inheritdoc/>
    public string Key => "AirTimesFixAllPremiereDates";

    /// <inheritdoc/>
    public string Description => "Manual cleanup that checks every episode and ensures its premiere date is not later than the date its file was added.";

    /// <inheritdoc/>
    public string Category => "Air Times";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var queryResult = libraryManager.GetItemsResult(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false
        });

        var episodes = queryResult.Items.OfType<Episode>().ToList();

        var total = episodes.Count;
        var current = 0;

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
                "[AirTimes] Clamping PremiereDate for \"{SeriesName}\" - \"{Name}\": {Old} -> {New}",
                episode.SeriesName,
                episode.Name,
                episode.PremiereDate.Value,
                clamped);

            episode.PremiereDate = clamped;
            await episode.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }
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
        return [];
    }
}
