using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace Nodearr.Http.ArrContractSpike
{
    public interface IArrContractSpikeStore
    {
        ArrContractSpikeConfig GetConfig();
        SpikeJob AddJob(SpikeCandidate candidate, string category, string nzbName);
        SpikeJob Retry(string nzoId);
        List<SpikeJob> GetJobs();
        bool Remove(string nzoId, bool deleteFiles);
        void Record(HttpRequest request, int statusCode, string summary);
    }

    public class ArrContractSpikeStore : IArrContractSpikeStore
    {
        private const string ConfigFileName = "arr-contract-spike.json";
        private const string TranscriptFileName = "arr-contract-transcript.jsonl";

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;
        private readonly object _lock = new();
        private readonly List<SpikeJob> _jobs = new();

        public ArrContractSpikeStore(IAppFolderInfo appFolderInfo, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public ArrContractSpikeConfig GetConfig()
        {
            var path = Path.Combine(_appFolderInfo.AppDataFolder, ConfigFileName);

            if (!File.Exists(path))
            {
                return new ArrContractSpikeConfig();
            }

            // Read on every call so the harness can change candidates without a restart.
            var config = JsonSerializer.Deserialize<ArrContractSpikeConfig>(File.ReadAllText(path), JsonOptions) ?? new ArrContractSpikeConfig();

            config.CompleteDirReported ??= new ArrContractSpikeConfig().CompleteDirReported;

            return config;
        }

        public SpikeJob AddJob(SpikeCandidate candidate, string category, string nzbName)
        {
            var config = GetConfig();
            var job = new SpikeJob
            {
                NzoId = $"SABnzbd_nzo_{Guid.NewGuid().ToString("N")[..8]}",
                Candidate = candidate,
                Category = category,
                NzbName = nzbName,
                Status = SpikeJobStatus.Queued,
                TotalBytes = candidate.GetSize(),
                AddedAt = DateTime.UtcNow,
                Cancellation = new CancellationTokenSource()
            };

            lock (_lock)
            {
                _jobs.Add(job);
            }

            _ = Task.Run(() => RunAsync(job, config));

            return job;
        }

        public SpikeJob Retry(string nzoId)
        {
            SpikeJob failed;

            lock (_lock)
            {
                failed = _jobs.SingleOrDefault(j => j.NzoId == nzoId && j.Status == SpikeJobStatus.Failed);

                if (failed == null)
                {
                    return null;
                }

                _jobs.Remove(failed);
            }

            return AddJob(failed.Candidate, failed.Category, failed.NzbName);
        }

        public List<SpikeJob> GetJobs()
        {
            lock (_lock)
            {
                return _jobs.ToList();
            }
        }

        public bool Remove(string nzoId, bool deleteFiles)
        {
            SpikeJob job;

            lock (_lock)
            {
                job = _jobs.SingleOrDefault(j => j.NzoId == nzoId);

                if (job == null)
                {
                    return false;
                }

                _jobs.Remove(job);
            }

            job.Cancellation.Cancel();

            if (deleteFiles && job.StorageLocal != null && Directory.Exists(job.StorageLocal))
            {
                Directory.Delete(job.StorageLocal, true);
            }

            return true;
        }

        public void Record(HttpRequest request, int statusCode, string summary)
        {
            var query = request.Query
                .Where(q => !q.Key.Equals("apikey", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(q => q.Key, q => q.Value.ToString());

            var line = JsonSerializer.Serialize(new
            {
                time = DateTime.UtcNow.ToString("O"),
                client = request.Headers.UserAgent.ToString(),
                method = request.Method,
                path = request.Path.Value,
                query,
                status = statusCode,
                summary
            });

            lock (_lock)
            {
                File.AppendAllText(Path.Combine(_appFolderInfo.AppDataFolder, TranscriptFileName), line + Environment.NewLine);
            }
        }

        private async Task RunAsync(SpikeJob job, ArrContractSpikeConfig config)
        {
            var token = job.Cancellation.Token;
            var steps = Math.Max(2, config.TransferSeconds * 2);
            var incomplete = Path.Combine(Path.GetDirectoryName(config.CompleteDirLocal.TrimEnd('/', '\\')), "incomplete", job.NzoId);

            try
            {
                await Task.Delay(500, token);
                job.Status = SpikeJobStatus.Downloading;

                if (!string.IsNullOrWhiteSpace(job.Candidate.Fail))
                {
                    job.DoneBytes = job.TotalBytes / 4;
                    await Task.Delay(config.TransferSeconds * 500, token);

                    throw new InvalidOperationException(job.Candidate.Fail);
                }

                Directory.CreateDirectory(incomplete);
                var partial = Path.Combine(incomplete, job.Candidate.Title + Path.GetExtension(job.Candidate.SourcePath));

                await using (var source = File.OpenRead(job.Candidate.SourcePath))
                await using (var target = File.Create(partial))
                {
                    var buffer = new byte[Math.Max(64 * 1024, (int)Math.Min(int.MaxValue / 2, (source.Length / steps) + 1))];
                    int read;

                    while ((read = await source.ReadAsync(buffer, token)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), token);
                        job.DoneBytes += read;
                        await Task.Delay(500, token);
                    }
                }

                // Same shape as SABnzbd: one job folder per download under the category folder.
                var relative = Path.Combine(job.Category ?? string.Empty, job.Candidate.Title);
                var final = Path.Combine(config.CompleteDirLocal, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(final));

                if (Directory.Exists(final))
                {
                    Directory.Delete(final, true);
                }

                Directory.Move(incomplete, final);

                job.StorageLocal = final;
                job.StorageReported = config.CompleteDirReported.TrimEnd('/') + "/" + relative.Replace('\\', '/');
                job.DoneBytes = job.TotalBytes;
                job.CompletedAt = DateTime.UtcNow;
                job.Status = SpikeJobStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                CleanUp(incomplete);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Spike transfer {0} failed", job.NzoId);
                CleanUp(incomplete);

                job.FailMessage = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
                job.Status = SpikeJobStatus.Failed;
            }
        }

        private static void CleanUp(string folder)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
