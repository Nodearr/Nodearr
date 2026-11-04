using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(268)]
    public class add_statistics : NzbDroneMigrationBase
    {
        // TransferState.Completed and TransferState.Failed, as they were when this was written.
        private const int Completed = 2;
        private const int Failed = 3;

        // TransferDirection.Fetched.
        private const int Fetched = 0;

        // The TransferFailure names, by number, as they were when this was written.
        private static readonly string[] FailureNames =
        {
            "None", "NoDirectRoute", "DirectPathLost", "ConnectionLost", "PeerUnreachable", "NotAuthorized",
            "Revoked", "Blocked", "NotFound", "VerificationFailed", "DiskError", "Failed"
        };

        protected override void MainDbUpgrade()
        {
            Alter.Table("Transfers")
                .AddColumn("BytesMoved").AsInt64().WithDefaultValue(0)
                .AddColumn("MovingMilliseconds").AsInt64().WithDefaultValue(0);

            Create.TableForModel("TransferStatistics")
                .WithColumn("Hour").AsDateTimeOffset()
                .WithColumn("PeerNodeId").AsString()
                .WithColumn("ApplicationId").AsInt32()
                .WithColumn("Direction").AsInt32()
                .WithColumn("Failure").AsString()
                .WithColumn("Bytes").AsInt64()
                .WithColumn("Transfers").AsInt32()
                .WithColumn("Seconds").AsDouble()
                .WithColumn("PeakBytesPerSecond").AsDouble()
                .WithColumn("Requests").AsInt32()
                .WithColumn("Waited").AsInt32()
                .WithColumn("WaitSeconds").AsDouble()
                .WithColumn("LongestWaitSeconds").AsDouble();

            Create.Index("IX_TransferStatistics_Key").OnTable("TransferStatistics")
                .OnColumn("Hour").Ascending()
                .OnColumn("PeerNodeId").Ascending()
                .OnColumn("ApplicationId").Ascending()
                .OnColumn("Direction").Ascending()
                .OnColumn("Failure").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("ConnectionStatistics")
                .WithColumn("Hour").AsDateTimeOffset()
                .WithColumn("PeerNodeId").AsString()
                .WithColumn("Direct").AsInt32()
                .WithColumn("RelayOnly").AsInt32()
                .WithColumn("None").AsInt32()
                .WithColumn("RouteChanges").AsInt32();

            Create.Index("IX_ConnectionStatistics_Key").OnTable("ConnectionStatistics")
                .OnColumn("Hour").Ascending()
                .OnColumn("PeerNodeId").Ascending()
                .WithOptions().Unique();

            Create.TableForModel("PeerRoutes")
                .WithColumn("PeerNodeId").AsString().Unique()
                .WithColumn("Path").AsInt32()
                .WithColumn("Changed").AsDateTimeOffset().Nullable();

            Create.TableForModel("FetchedTitles")
                .WithColumn("Hour").AsDateTimeOffset()
                .WithColumn("ApplicationId").AsInt32()
                .WithColumn("Title").AsString();

            Create.Index("IX_FetchedTitles_Key").OnTable("FetchedTitles")
                .OnColumn("Hour").Ascending()
                .OnColumn("ApplicationId").Ascending()
                .OnColumn("Title").Ascending()
                .WithOptions().Unique();

            Execute.WithConnection(Backfill);
        }

        // Builds the statistics for downloads that ended in the last 13 months from their rows, so
        // the page is not empty after the upgrade. Uploads and connection checks were never kept,
        // so they start from here. This works out what StatisticsRecorder.ForDownload does.
        private static void Backfill(IDbConnection conn, IDbTransaction tran)
        {
            var categories = new List<(int Id, string Category)>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = "SELECT \"Id\", \"Settings\" FROM \"Applications\" ORDER BY \"Id\"";

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    categories.Add((reader.GetInt32(0), DownloadCategory(reader.IsDBNull(1) ? null : reader.GetString(1))));
                }
            }

            var since = DateTime.UtcNow.AddMonths(-13);
            var rows = new Dictionary<(DateTime Hour, string PeerNodeId, int ApplicationId, string Failure), Row>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = $"SELECT \"PeerNodeId\", \"Category\", \"State\", \"Failure\", \"Size\", \"BytesDone\", \"Started\", \"Ended\" FROM \"Transfers\" WHERE \"State\" IN ({Completed}, {Failed}) AND \"Ended\" IS NOT NULL";

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var ended = ToUtc(reader.GetValue(7));

                    if (!ended.HasValue || ended.Value < since)
                    {
                        continue;
                    }

                    var category = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var state = Convert.ToInt32(reader.GetValue(2));
                    var failureNumber = Convert.ToInt32(reader.GetValue(3));
                    var size = Convert.ToInt64(reader.GetValue(4));
                    var bytesDone = Convert.ToInt64(reader.GetValue(5));
                    var started = ToUtc(reader.GetValue(6));

                    var completed = state == Completed;
                    var failure = completed ? string.Empty : failureNumber >= 0 && failureNumber < FailureNames.Length ? FailureNames[failureNumber] : failureNumber.ToString(CultureInfo.InvariantCulture);
                    var bytes = completed ? size : bytesDone;
                    var seconds = started.HasValue ? Math.Max(0, (ended.Value - started.Value).TotalSeconds) : 0;
                    var applicationId = category == null ? 0 : categories.FirstOrDefault(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)).Id;
                    var hour = new DateTime(ended.Value.Year, ended.Value.Month, ended.Value.Day, ended.Value.Hour, 0, 0, DateTimeKind.Utc);
                    var key = (hour, reader.IsDBNull(0) ? string.Empty : reader.GetString(0), applicationId, failure);

                    if (!rows.TryGetValue(key, out var row))
                    {
                        row = new Row();
                        rows[key] = row;
                    }

                    row.Bytes += bytes;
                    row.Transfers++;
                    row.Seconds += seconds;

                    if (completed && seconds > 0)
                    {
                        row.Peak = Math.Max(row.Peak, bytes / seconds);
                    }
                }
            }

            conn.Execute(
                "INSERT INTO \"TransferStatistics\" (\"Hour\", \"PeerNodeId\", \"ApplicationId\", \"Direction\", \"Failure\", \"Bytes\", \"Transfers\", \"Seconds\", \"PeakBytesPerSecond\", \"Requests\", \"Waited\", \"WaitSeconds\", \"LongestWaitSeconds\") " +
                "VALUES (@Hour, @PeerNodeId, @ApplicationId, @Direction, @Failure, @Bytes, @Transfers, @Seconds, @Peak, 0, 0, 0, 0)",
                rows.Select(r => new
                {
                    r.Key.Hour,
                    r.Key.PeerNodeId,
                    r.Key.ApplicationId,
                    Direction = Fetched,
                    r.Key.Failure,
                    r.Value.Bytes,
                    r.Value.Transfers,
                    r.Value.Seconds,
                    r.Value.Peak
                }),
                transaction: tran);
        }

        private static string DownloadCategory(string settings)
        {
            if (string.IsNullOrWhiteSpace(settings))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(settings);

                return json.RootElement.ValueKind == JsonValueKind.Object
                    ? json.RootElement.EnumerateObject().Where(p => string.Equals(p.Name, "downloadCategory", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString()).FirstOrDefault()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static DateTime? ToUtc(object value)
        {
            return value switch
            {
                DateTime time => time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc),
                DateTimeOffset offset => offset.UtcDateTime,
                string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) => parsed,
                _ => null
            };
        }

        private class Row
        {
            public long Bytes { get; set; }
            public int Transfers { get; set; }
            public double Seconds { get; set; }
            public double Peak { get; set; }
        }
    }
}
