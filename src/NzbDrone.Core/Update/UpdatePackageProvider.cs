using System;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Options;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Options;

namespace NzbDrone.Core.Update
{
    public enum UpdateManifestOutcome
    {
        // Checked, and the signature is good.
        Verified,

        // It could not be fetched. Said nowhere but the debug log: it is not the owner's to fix.
        Unavailable,

        // It was fetched, and is not signed by the key Nodearr trusts, or cannot be read.
        BadSignature
    }

    public class UpdateManifestResult
    {
        public UpdateManifestOutcome Outcome { get; set; }
        public UpdateManifest Manifest { get; set; }
    }

    public interface IUpdatePackageProvider
    {
        UpdateManifestResult Fetch(string branch);
    }

    // Fetches <branch>.json and <branch>.json.sig from updates.nodearr.com with two plain GETs: no
    // identifier, no query string and no cookies, and a user agent that says only "Nodearr" and the
    // version (ADR 0024). Nothing in the manifest is believed until its signature is checked.
    public class UpdatePackageProvider : IUpdatePackageProvider
    {
        public const string DefaultManifestUrl = "https://updates.nodearr.com/v1/";

        public static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(30);

        private const int MaxManifestBytes = 256 * 1024;

        private readonly IHttpClient _httpClient;
        private readonly UpdateOptions _options;
        private readonly Logger _logger;

        public UpdatePackageProvider(IHttpClient httpClient, IOptions<UpdateOptions> options, Logger logger)
        {
            _httpClient = httpClient;
            _options = options.Value ?? new UpdateOptions();
            _logger = logger;
        }

        // For testing: how a pause between attempts is made.
        public Action<TimeSpan> Pause { get; set; } = Thread.Sleep;

        public UpdateManifestResult Fetch(string branch)
        {
            var folder = _options.ManifestUrl.IsNotNullOrWhiteSpace() ? _options.ManifestUrl.Trim() : DefaultManifestUrl;
            var manifestUrl = folder.TrimEnd('/') + "/" + Uri.EscapeDataString(branch.IsNullOrWhiteSpace() ? "main" : branch) + ".json";

            var publicKey = _options.PublicKey.IsNotNullOrWhiteSpace() ? _options.PublicKey : UpdateSigningKey.PublicKey;
            byte[] manifestBytes = null;
            var verified = false;

            // Fetched twice if the first pair does not match: a check made while a new release is
            // being published can get the new manifest and the old signature.
            for (var attempt = 1; attempt <= 2 && !verified; attempt++)
            {
                if (attempt > 1)
                {
                    Pause(RetryPause);
                }

                string signature;

                try
                {
                    manifestBytes = Get(manifestUrl);
                    signature = Encoding.UTF8.GetString(Get(manifestUrl + ".sig"));
                }
                catch (Exception e)
                {
                    _logger.Debug(e, "Could not fetch the update manifest from {0}", manifestUrl);
                    return new UpdateManifestResult { Outcome = UpdateManifestOutcome.Unavailable };
                }

                verified = SshSignature.Verify(manifestBytes, signature, publicKey, UpdateSigningKey.Namespace);
            }

            if (!verified)
            {
                _logger.Warn("The update manifest at {0} is not signed by Nodearr's release key. It was ignored.", manifestUrl);
                return new UpdateManifestResult { Outcome = UpdateManifestOutcome.BadSignature };
            }

            try
            {
                return new UpdateManifestResult
                {
                    Outcome = UpdateManifestOutcome.Verified,
                    Manifest = UpdateManifest.Parse(manifestBytes)
                };
            }
            catch (FormatException e)
            {
                // Signed, so it came from the release, but this version cannot read it: a later
                // format, most likely. Treated as no manifest.
                _logger.Debug(e, "The update manifest at {0} is signed but could not be read", manifestUrl);
                return new UpdateManifestResult { Outcome = UpdateManifestOutcome.Unavailable };
            }
        }

        private byte[] Get(string url)
        {
            var request = new HttpRequest(url)
            {
                UseSimplifiedUserAgent = true,
                AllowAutoRedirect = true,
                StoreRequestCookie = false,
                StoreResponseCookie = false,
                LogHttpError = false,
                RequestTimeout = TimeSpan.FromSeconds(30)
            };

            var response = _httpClient.Get(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpException(request, response);
            }

            if (response.ResponseData == null || response.ResponseData.Length > MaxManifestBytes)
            {
                throw new InvalidOperationException($"{url} is empty or larger than {MaxManifestBytes} bytes");
            }

            return response.ResponseData;
        }
    }
}
