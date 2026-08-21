using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.Services;
using CloudflareR2Uploader.Wpf.ViewModels;
using CloudflareR2Uploader.Wpf.ViewModels.Activity;
using CloudflareR2Uploader.Wpf.ViewModels.Files;
using CloudflareR2Uploader.Wpf.ViewModels.Upload;
using CloudflareR2Uploader.Wpf.Views;
using CloudflareR2Uploader.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace CloudflareR2Uploader.Wpf
{
    public partial class App : Application
    {
        private ServiceProvider? _services;
        private SingleInstanceCoordinator? _singleInstance;
        private LoggingService? _log;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            R2ClientFactory.ConfigureTransportSecurity();

            try
            {
                AppPaths.EnsureAllDirectories();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The application data folder could not be created:\n\n" +
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.AppFolderName) +
                    "\n\n" + ex.Message,
                    "Cloudflare R2 Uploader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            _log = new LoggingService();
            _log.Info("App.Start", "Cloudflare R2 Uploader " + ApplicationInfo.DisplayVersion +
                                   " starting on " + Environment.OSVersion.VersionString + ".");

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

#if DEBUG
            // A developer build must be inspectable without terminating the installed tray
            // application. Builds from the same output folder still coordinate with each
            // other; Release keeps the production mutex and pipe names unchanged.
            _singleInstance = new SingleInstanceCoordinator(AppContext.BaseDirectory);
#else
            _singleInstance = new SingleInstanceCoordinator();
#endif
            if (!_singleInstance.IsFirstInstance)
            {
                _singleInstance.SignalFirstInstance();
                Shutdown();
                return;
            }

            try
            {
                _services = BuildServices(_log, _singleInstance);

                IAppSession session = _services.GetRequiredService<IAppSession>();
                IThemeService theme = _services.GetRequiredService<IThemeService>();
                theme.Apply(session.Settings.Theme);

                MainWindow window = _services.GetRequiredService<MainWindow>();
                MainWindow = window;

                TrayIconService tray = _services.GetRequiredService<TrayIconService>();
                tray.Start();

                InstanceLaunchCommand command = SingleInstanceCoordinator.ParseArguments(e.Args);
                if (command == InstanceLaunchCommand.Show && !session.Settings.StartMinimized)
                    tray.ShowWindow();
            }
            catch (Exception ex)
            {
                _log.Error("App.Start", "The WPF application could not be started.", ex);
                MessageBox.Show(
                    "Cloudflare R2 Uploader could not start.\n\n" + LoggingService.Sanitize(ex.Message),
                    "Cloudflare R2 Uploader",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private static ServiceProvider BuildServices(LoggingService log, SingleInstanceCoordinator singleInstance)
        {
            ServiceCollection services = new();

            services.AddSingleton<ILoggingService>(log);
            services.AddSingleton(singleInstance);
            services.AddSingleton<SettingsService>();
            services.AddSingleton<CredentialProtectionService>();
            services.AddSingleton<IAppSession, AppSession>();

            services.AddSingleton<UploadStateStore>();
            services.AddSingleton<PreviewCacheService>();
            services.AddSingleton<IActivityHistoryService, ActivityHistoryService>();

            services.AddSingleton<R2ConnectionTester>();
            services.AddSingleton<R2ObjectBrowserService>();
            services.AddSingleton<R2ObjectOperationService>();
            services.AddSingleton<R2BulkOperationService>();
            services.AddSingleton<R2PresignedUrlService>();
            services.AddSingleton<R2PreviewService>();
            services.AddSingleton<R2ObjectDetailsService>();
            AddUploadQueueServices(services);
            services.AddSingleton<UpdateService>();

            services.AddSingleton<IClipboardService, WpfClipboardService>();
            services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();
            services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
            services.AddSingleton<ISystemThemeProvider, WindowsThemeProvider>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<IToastService, ToastService>();

            services.AddSingleton<IDialogService>(provider => new DialogService(
                () => Current?.MainWindow,
                () => new SettingsWindow
                {
                    DataContext = ActivatorUtilities.CreateInstance<SettingsViewModel>(provider)
                }));
            services.AddSingleton<IApplicationExitCoordinator, ApplicationExitCoordinator>();
            services.AddSingleton<IApplicationController, WpfApplicationController>();

            services.AddSingleton<PreviewViewModel>();
            services.AddSingleton<FileInspectorViewModel>();
            services.AddSingleton<FilesViewModel>();
            services.AddSingleton<UploadViewModel>();
            services.AddSingleton<ActivityViewModel>();
            services.AddSingleton<ShellViewModel>();

            services.AddSingleton(provider => new MainWindow
            {
                DataContext = provider.GetRequiredService<ShellViewModel>()
            });
            services.AddSingleton<TrayIconService>();

            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }

        internal static void AddUploadQueueServices(IServiceCollection services)
        {
            services.AddSingleton<UploadQueueService>();
            services.AddSingleton<IUploadQueueLifecycle>(provider =>
                provider.GetRequiredService<UploadQueueService>());
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _log?.Error("App.Unhandled", "Unhandled exception on the UI thread.", e.Exception);
            e.Handled = true;

            try
            {
                MessageBox.Show(
                    "Something went wrong and the action could not be completed.\n\n" +
                    LoggingService.Sanitize(e.Exception.Message) +
                    "\n\nA sanitised entry has been written to the log folder.",
                    "Unexpected error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // The error surface itself failed; the original exception is already logged.
            }
        }

        private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            _log?.Error("App.Unhandled", "Unhandled exception on a background thread.", e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _log?.Error("App.UnobservedTask", "A background task failed without being observed.", e.Exception);
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_services?.GetService<IAppSession>() is IAppSession session && session.Settings.ForgetCredentialsOnExit)
                    session.ForgetAllCredentials();
            }
            catch (Exception ex)
            {
                _log?.Warning("App.Exit", "Credentials marked for exit cleanup could not be removed (" + ex.GetType().Name + ").");
            }

            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            _services?.Dispose();
            _services = null;

            _singleInstance?.Dispose();
            _singleInstance = null;

            _log?.Info("App.Exit", "Cloudflare R2 Uploader closing.");
            _log?.Dispose();
            _log = null;

            base.OnExit(e);
        }
    }
}
