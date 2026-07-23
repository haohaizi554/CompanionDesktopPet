using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class WindowShellTests
{
    private static readonly Lazy<StaTestHost> StaHost = new(() => new StaTestHost());

    [Fact]
    public void MainWindow_CloseRequestsShutdownUnlessExplicitlySuppressed()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var shutdownRequests = 0;
            var normalWindow = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                shutdownApplication: () => shutdownRequests++);
            normalWindow.Show();
            normalWindow.Close();

            var suppressedWindow = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true,
                shutdownApplication: () => shutdownRequests++);
            suppressedWindow.Show();
            suppressedWindow.Close();

            Assert.Equal(1, shutdownRequests);
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_SmokeReadinessRequiresRenderedImageBubbleAndReply()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);

            Assert.False(window.TryVerifySmokeReadiness(out var beforeRenderFailure));
            Assert.False(string.IsNullOrWhiteSpace(beforeRenderFailure));

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.True(window.TryVerifySmokeReadiness(out var renderedFailure), renderedFailure);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_StartupReply_DoesNotDriveActionAnimationsOrHearts()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal("none", GetLastReply(window).AnimationCue);
            Assert.False(Assert.IsType<ScaleTransform>(window.FindName("ActionScale")).HasAnimatedProperties);
            Assert.False(Assert.IsType<RotateTransform>(window.FindName("ActionRotation")).HasAnimatedProperties);
            Assert.False(Assert.IsType<TranslateTransform>(window.FindName("ActionOffset")).HasAnimatedProperties);
            foreach (var name in new[] { "HeartOne", "HeartTwo", "HeartThree" })
            {
                var heart = Assert.IsType<TextBlock>(window.FindName(name));
                Assert.False(heart.HasAnimatedProperties);
                Assert.Equal(0, heart.Opacity);
            }

            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_ConstructorAcceptsAnInjectableIdleTimeProvider()
    {
        var providerType = typeof(MainWindow).Assembly.GetType(
            "CompanionDesktopPet.Services.IIdleTimeProvider",
            throwOnError: false);
        Assert.NotNull(providerType);
        Assert.Contains(
            typeof(MainWindow).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == providerType
                         && parameter.Name == "idleTimeProvider"
                         && parameter.HasDefaultValue);
    }

    [Fact]
    public void MainWindow_EventTimerRunsOnlyWhileTheWindowIsLoaded()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            var field = typeof(MainWindow).GetField(
                "_eventTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var timer = Assert.IsType<DispatcherTimer>(field!.GetValue(window));
            Assert.Equal(TimeSpan.FromSeconds(30), timer.Interval);
            Assert.False(timer.IsEnabled);

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(timer.IsEnabled);

            window.Close();
            Assert.False(timer.IsEnabled);
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_StartupThenSaySomething_ReplacesTheStartupReplyWithNewV2Text()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var startup = GetLastReply(window);
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var say = Assert.IsType<MenuItem>(stage.ContextMenu!.Items[0]);

            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var click = GetLastReply(window);
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(CompanionEvent.Click, click.Trigger);
            Assert.True(click.ShouldDisplayText);
            Assert.NotNull(click.SourceLine);
            Assert.NotEqual(startup.SourceLine!.Id, click.SourceLine.Id);
            Assert.Equal(click.Text, speech.Text);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_BackgroundSilence_PreservesThePreviousBubble()
    {
        RunOnStaThread(() =>
        {
            var automaticTick = typeof(MainWindow).GetMethod(
                "AutomaticTimer_Tick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(automaticTick);
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            var startupText = speech.Text;

            automaticTick!.Invoke(window, [null, EventArgs.Empty]);

            var reply = GetLastReply(window);
            Assert.False(reply.ShouldDisplayText);
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            Assert.Equal(startupText, speech.Text);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_ExplicitUserSilence_ClearsThePreviousBubble()
    {
        RunOnStaThread(() =>
        {
            var presentReply = typeof(MainWindow).GetMethod(
                "PresentReply",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(presentReply);
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);

            presentReply!.Invoke(window,
            [
                new AgentReply(
                    string.Empty,
                    DialogueCategory.DailyCare,
                    DialogueTreeKind.Companion,
                    CompanionEvent.Click,
                    ShouldDisplayText: false)
            ]);

            Assert.Equal(Visibility.Collapsed, bubble.Visibility);
            Assert.Equal(string.Empty, speech.Text);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_DisplaysStartupClickAndAutomaticRepliesWithEnabledV2Provenance()
    {
        RunOnStaThread(() =>
        {
            var lastReply = typeof(MainWindow).GetProperty(
                "LastReply",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var automaticTick = typeof(MainWindow).GetMethod(
                "AutomaticTimer_Tick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(lastReply);
            Assert.NotNull(automaticTick);

            foreach (var (trigger, enterThroughRealHandler) in new (CompanionEvent, Action<MainWindow>)[]
                     {
                         (CompanionEvent.Startup, window =>
                         {
                             window.Show();
                             window.Dispatcher.Invoke(
                                 () => { },
                                 System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                         }),
                         (CompanionEvent.Click, window =>
                         {
                             var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
                             var say = Assert.IsType<MenuItem>(stage.ContextMenu!.Items[0]);
                             say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                         }),
                         (CompanionEvent.Automatic, window =>
                             automaticTick!.Invoke(window, [null, EventArgs.Empty]))
                     })
            {
                var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                var window = new MainWindow(
                    PetSettings.Default,
                    new SettingsService(settingsDirectory),
                    suppressApplicationShutdownOnClose: true);

                enterThroughRealHandler(window);

                var reply = Assert.IsType<AgentReply>(lastReply!.GetValue(window));
                var source = Assert.IsType<DialogueLine>(reply.SourceLine);
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                Assert.Equal(trigger, reply.Trigger);
                Assert.True(reply.ShouldDisplayText);
                Assert.True(source.Enabled);
                Assert.Contains(source, PersonaCorpus.All);
                Assert.Equal(source.Text, reply.Text);
                Assert.Equal(reply.Text, speech.Text);
                window.Close();
                if (Directory.Exists(settingsDirectory))
                {
                    Directory.Delete(settingsDirectory, true);
                }
            }
        });
    }

    [Fact]
    public void MainWindow_UsesTransparentDesktopPetChrome()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        RunOnStaThread(() =>
        {
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);

            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.AllowsTransparency);
            Assert.Null(window.Background);
            Assert.False(window.ShowInTaskbar);
            Assert.True(window.Topmost);
            Assert.Equal("角色桌宠", window.Title);
            Assert.NotNull(window.FindName("SpeechBubble"));
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var hearts = Assert.IsType<Canvas>(window.FindName("HeartLayer"));
            Assert.False(hearts.IsHitTestVisible);
            var image = Assert.IsType<System.Windows.Controls.Image>(window.FindName("PetImage"));
            Assert.NotNull(image.Source);
            Assert.NotNull(stage.ContextMenu);
            Assert.True(stage.ContextMenu.Items.Count >= 8);

            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            Assert.False(string.IsNullOrWhiteSpace(speech.Text));

            var say = Assert.IsType<MenuItem>(stage.ContextMenu.Items[0]);
            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(string.IsNullOrWhiteSpace(speech.Text));

            var pause = Assert.IsType<MenuItem>(stage.ContextMenu.Items[1]);
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("继续动画", pause.Header);

            var size = Assert.IsType<MenuItem>(stage.ContextMenu.Items[3]);
            var large = Assert.IsType<MenuItem>(size.Items[2]);
            large.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(390, stage.Width);

            var topmost = Assert.IsType<MenuItem>(stage.ContextMenu.Items[4]);
            topmost.IsChecked = false;
            topmost.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(window.Topmost);
            window.Close();
        });

        Assert.True(SpinWait.SpinUntil(
            () => !File.Exists(Path.Combine(settingsDirectory, "settings.json.tmp")),
            TimeSpan.FromSeconds(5)));
        if (Directory.Exists(settingsDirectory))
        {
            Directory.Delete(settingsDirectory, true);
        }
    }

    private static void RunOnStaThread(Action action) => StaHost.Value.Invoke(action);

    private sealed class StaTestHost
    {
        private readonly Dispatcher _dispatcher;

        public StaTestHost()
        {
            Dispatcher? dispatcher = null;
            Exception? initializationException = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    var app = new App();
                    app.InitializeComponent();
                    dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception caught)
                {
                    initializationException = caught;
                }
                finally
                {
                    ready.Set();
                }

                if (initializationException is null)
                {
                    Dispatcher.Run();
                }
            })
            {
                IsBackground = true,
                Name = "CompanionDesktopPet.WpfTests"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            if (initializationException is not null)
            {
                ExceptionDispatchInfo.Capture(initializationException).Throw();
            }

            _dispatcher = dispatcher
                ?? throw new InvalidOperationException("The WPF test dispatcher did not start.");
        }

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _dispatcher.Invoke(action);
        }
    }

    private static AgentReply GetLastReply(MainWindow window)
    {
        var property = typeof(MainWindow).GetProperty(
            "LastReply",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<AgentReply>(property!.GetValue(window));
    }
}
