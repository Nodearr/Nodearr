using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using NLog;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Extensions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

using PostgresOptions = NzbDrone.Core.Datastore.PostgresOptions;

namespace NzbDrone.Host
{
    public static class Bootstrap
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Bootstrap));

        public static readonly List<string> ASSEMBLIES = new()
        {
            "Nodearr.Host",
            "Nodearr.Core",
            "Nodearr.SignalR",
            "Nodearr.Api.V3",
            "Nodearr.Api.V5",
            "Nodearr.Http"
        };

        public static void Start(string[] args, Action<IHostBuilder> trayCallback = null)
        {
            try
            {
                Logger.Info("Starting Nodearr - {0} - Version {1}",
                            Environment.ProcessPath,
                            Assembly.GetExecutingAssembly().GetName().Version);

                var startupContext = new StartupContext(args);
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var appMode = GetApplicationMode(startupContext);
                var config = GetConfiguration(startupContext);

                if (appMode is not (ApplicationModes.Interactive or ApplicationModes.Service))
                {
                    RunUtilityMode(appMode, startupContext, config);
                    return;
                }

                RunHostUntilShutdown(args, startupContext, appMode, trayCallback);

                Logger.Info("Nodearr has shut down completely");
            }
            catch (InvalidConfigFileException ex)
            {
                throw new NodearrStartupException(ex);
            }
            catch (TerminateApplicationException e)
            {
                Logger.Info(e.Message);
                LogManager.Configuration = null;
            }
        }

        private static void RunUtilityMode(ApplicationModes appMode, StartupContext startupContext, IConfiguration config)
        {
            Logger.Debug("Utility mode: {0}", appMode);

            new HostBuilder()
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                .ConfigureContainer<IContainer>(c =>
                {
                    c.AutoAddServices(ASSEMBLIES)
                        .AddNzbDroneLogger()
                        .AddDatabase()
                        .AddStartupContext(startupContext)
                        .Resolve<UtilityModeRouter>()
                        .Route(appMode);

                    if (config.GetValue(nameof(ConfigFileProvider.LogDbEnabled), true))
                    {
                        c.AddLogDatabase();
                    }
                    else
                    {
                        c.AddDummyLogDatabase();
                    }
                })
                .ConfigureServices(services =>
                {
                    services.Configure<PostgresOptions>(config.GetSection("Nodearr:Postgres"));
                    services.Configure<AppOptions>(config.GetSection("Nodearr:App"));
                    services.Configure<AuthOptions>(config.GetSection("Nodearr:Auth"));
                    services.Configure<ServerOptions>(config.GetSection("Nodearr:Server"));
                    services.Configure<TransportOptions>(config.GetSection("Nodearr:Transport"));
                    services.Configure<LogOptions>(config.GetSection("Nodearr:Log"));
                    services.Configure<UpdateOptions>(config.GetSection("Nodearr:Update"));
                })
                .Build();
        }

        private static void RunHostUntilShutdown(string[] args, StartupContext startupContext, ApplicationModes appMode, Action<IHostBuilder> trayCallback)
        {
            Logger.Debug("Starting in {0} mode", trayCallback != null ? "Tray" : appMode.ToString());

            bool shouldRestart;
            do
            {
                var builder = CreateConsoleHostBuilder(args, startupContext);
                trayCallback?.Invoke(builder);

                shouldRestart = RunWithRestartCheck(builder.Build());

                if (shouldRestart)
                {
                    Logger.Info("Application restart requested, reinitializing host");
                    NzbDroneLogger.ResetAllTargets(startupContext, false, true);
                    Thread.Sleep(1000);
                }
            }
            while (shouldRestart);
        }

        public static IHostBuilder CreateConsoleHostBuilder(string[] args, StartupContext context)
        {
            var config = GetConfiguration(context);

            var bindAddress = config.GetValue<string>($"Nodearr:Server:{nameof(ServerOptions.BindAddress)}") ?? config.GetValue(nameof(ConfigFileProvider.BindAddress), "*");
            var port = config.GetValue<int?>($"Nodearr:Server:{nameof(ServerOptions.Port)}") ?? config.GetValue(nameof(ConfigFileProvider.Port), 6633);
            var sslPort = config.GetValue<int?>($"Nodearr:Server:{nameof(ServerOptions.SslPort)}") ?? config.GetValue(nameof(ConfigFileProvider.SslPort), 9898);
            var enableSsl = config.GetValue<bool?>($"Nodearr:Server:{nameof(ServerOptions.EnableSsl)}") ?? config.GetValue(nameof(ConfigFileProvider.EnableSsl), false);
            var sslCertPath = config.GetValue<string>($"Nodearr:Server:{nameof(ServerOptions.SslCertPath)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPath));
            var sslKeyPath = config.GetValue<string>($"Nodearr:Server:{nameof(ServerOptions.SslKeyPath)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslKeyPath));
            var sslCertPassword = config.GetValue<string>($"Nodearr:Server:{nameof(ServerOptions.SslCertPassword)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPassword));
            var logDbEnabled = config.GetValue<bool?>($"Nodearr:Log:{nameof(LogOptions.DbEnabled)}") ?? config.GetValue(nameof(ConfigFileProvider.LogDbEnabled), true);

            var urls = new List<string> { BuildUrl("http", bindAddress, port) };

            if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
            {
                urls.Add(BuildUrl("https", bindAddress, sslPort));
            }

            return new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
                })
                .ConfigureContainer<IContainer>(c =>
                {
                    c.AutoAddServices(Bootstrap.ASSEMBLIES)
                        .AddNzbDroneLogger()
                        .AddDatabase()
                        .AddStartupContext(context);

                    if (logDbEnabled)
                    {
                        c.AddLogDatabase();
                    }
                    else
                    {
                        c.AddDummyLogDatabase();
                    }
                })
                .ConfigureServices(services =>
                {
                    services.Configure<PostgresOptions>(config.GetSection("Nodearr:Postgres"));
                    services.Configure<AppOptions>(config.GetSection("Nodearr:App"));
                    services.Configure<AuthOptions>(config.GetSection("Nodearr:Auth"));
                    services.Configure<ServerOptions>(config.GetSection("Nodearr:Server"));
                    services.Configure<TransportOptions>(config.GetSection("Nodearr:Transport"));
                    services.Configure<LogOptions>(config.GetSection("Nodearr:Log"));
                    services.Configure<UpdateOptions>(config.GetSection("Nodearr:Update"));
                })
                .ConfigureWebHost(builder =>
                {
                    builder.UseConfiguration(config);
                    builder.UseUrls(urls.ToArray());
                    builder.UseKestrel(options =>
                    {
                        if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
                        {
                            options.ConfigureHttpsDefaults(configureOptions =>
                            {
                                configureOptions.ServerCertificate = ValidateSslCertificate(sslCertPath, sslKeyPath, sslCertPassword);
                            });
                        }
                    });
                    builder.ConfigureKestrel(serverOptions =>
                    {
                        serverOptions.AllowSynchronousIO = false;
                        serverOptions.Limits.MaxRequestBodySize = null;
                    });
                    builder.UseStartup<Startup>();
                });
        }

        public static ApplicationModes GetApplicationMode(IStartupContext startupContext)
        {
            if (startupContext.Help)
            {
                return ApplicationModes.Help;
            }

            if (OsInfo.IsWindows && startupContext.RegisterUrl)
            {
                return ApplicationModes.RegisterUrl;
            }

            if (OsInfo.IsWindows && startupContext.InstallService)
            {
                return ApplicationModes.InstallService;
            }

            if (OsInfo.IsWindows && startupContext.UninstallService)
            {
                return ApplicationModes.UninstallService;
            }

            // IsWindowsService can throw sometimes, so wrap it
            var isWindowsService = false;
            try
            {
                isWindowsService = WindowsServiceHelpers.IsWindowsService();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to get service status");
            }

            if (OsInfo.IsWindows && isWindowsService)
            {
                return ApplicationModes.Service;
            }

            return ApplicationModes.Interactive;
        }

        private static IConfiguration GetConfiguration(StartupContext context)
        {
            var appFolder = new AppFolderInfo(context);
            var configPath = appFolder.GetConfigPath();

            try
            {
                return new ConfigurationBuilder()
                    .AddXmlFile(configPath, optional: true, reloadOnChange: false)
                    .AddInMemoryCollection(new List<KeyValuePair<string, string>> { new("dataProtectionFolder", appFolder.GetDataProtectionPath()) })
                    .AddEnvironmentVariables()
                    .Build();
            }
            catch (InvalidDataException ex)
            {
                Logger.Error(ex, ex.Message);

                throw new InvalidConfigFileException($"{configPath} is corrupt or invalid. Please delete the config file and Nodearr will recreate it.", ex);
            }
        }

        private static string BuildUrl(string scheme, string bindAddress, int port)
        {
            return $"{scheme}://{bindAddress}:{port}";
        }

        private static X509Certificate2 ValidateSslCertificate(string cert, string key, string password)
        {
            X509Certificate2 certificate;

            try
            {
                var type = X509Certificate2.GetCertContentType(cert);

                if (type == X509ContentType.Cert)
                {
                    certificate = X509Certificate2.CreateFromPemFile(cert, key.IsNullOrWhiteSpace() ? null : key);
                }
                else if (type == X509ContentType.Pkcs12)
                {
                    certificate = X509CertificateLoader.LoadPkcs12FromFile(cert, password, X509KeyStorageFlags.DefaultKeySet);
                }
                else
                {
                    throw new NodearrStartupException($"Invalid certificate type: {type}");
                }
            }
            catch (CryptographicException ex)
            {
                if (ex.HResult == 0x2 || ex.HResult == 0x2006D080)
                {
                    throw new NodearrStartupException(ex,
                        $"The SSL certificate file {cert} does not exist");
                }

                throw new NodearrStartupException(ex);
            }
            catch (Exception ex)
            {
                throw new NodearrStartupException(ex);
            }

            return certificate;
        }

        private static bool RunWithRestartCheck(IHost host)
        {
            var shouldRestart = false;

            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopped.Register(() =>
            {
                var runtimeInfo = host.Services.GetRequiredService<IRuntimeInfo>();
                shouldRestart = runtimeInfo.RestartPending;
            });

            host.Run();
            return shouldRestart;
        }
    }
}
