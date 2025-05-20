using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System.IO;
using Avalonia.Platform.Storage;
using Avalonia;

namespace MediaPlayerApp
{
    public partial class MainWindow : Window
    {
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private VideoView _videoView;
        private Canvas _pulseWaveCanvas;
        private Slider _seekSlider;
        private Slider _volumeSlider;
        private TextBlock _currentTime;
        private TextBlock _totalTime;
        private Button _playButton;
        private Button _pauseButton;
        private Button _openButton;
        private ComboBox _themeSelector;
        private bool _isDraggingSeekSlider;
        private bool _isAudio;
        private System.Timers.Timer _updateTimer;

        public MainWindow()
        {
            InitializeComponent();
            Core.Initialize();

            string vlcPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "vlc",
                OperatingSystem.IsWindows() ? "windows" : "linux");
            _libVLC = new LibVLC("--no-xlib", $"--plugin-path={System.IO.Path.Combine(vlcPath, "plugins")}");
            _mediaPlayer = new MediaPlayer(_libVLC);

            // Initialize controls with null checks
            _videoView = this.FindControl<VideoView>("VideoView") ?? throw new InvalidOperationException("VideoView control not found.");
            _pulseWaveCanvas = this.FindControl<Canvas>("PulseWaveCanvas") ?? throw new InvalidOperationException("PulseWaveCanvas control not found.");
            _seekSlider = this.FindControl<Slider>("SeekSlider") ?? throw new InvalidOperationException("SeekSlider control not found.");
            _volumeSlider = this.FindControl<Slider>("VolumeSlider") ?? throw new InvalidOperationException("VolumeSlider control not found.");
            _currentTime = this.FindControl<TextBlock>("CurrentTime") ?? throw new InvalidOperationException("CurrentTime control not found.");
            _totalTime = this.FindControl<TextBlock>("TotalTime") ?? throw new InvalidOperationException("TotalTime control not found.");
            _playButton = this.FindControl<Button>("PlayButton") ?? throw new InvalidOperationException("PlayButton control not found.");
            _pauseButton = this.FindControl<Button>("PauseButton") ?? throw new InvalidOperationException("PauseButton control not found.");
            _openButton = this.FindControl<Button>("OpenButton") ?? throw new InvalidOperationException("OpenButton control not found.");
            _themeSelector = this.FindControl<ComboBox>("ThemeSelector") ?? throw new InvalidOperationException("ThemeSelector control not found.");

            _videoView.MediaPlayer = _mediaPlayer;

            // Attach events programmatically
            _seekSlider.AddHandler(Slider.ValueChangedEvent, new EventHandler<RangeBaseValueChangedEventArgs>(SeekSlider_ValueChanged));
            _volumeSlider.AddHandler(Slider.ValueChangedEvent, new EventHandler<RangeBaseValueChangedEventArgs>(VolumeSlider_ValueChanged));
            _playButton.Click += PlayButton_Click;
            _pauseButton.Click += PauseButton_Click;
            _openButton.Click += OpenButton_Click;
            _themeSelector.SelectionChanged += ThemeSelector_SelectionChanged;

            // Timer to update seek bar and pulse waves
            _updateTimer = new System.Timers.Timer(1000);
            _updateTimer.Elapsed += (s, e) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateUI);
            _updateTimer.Start();

            // Set initial volume
            _mediaPlayer.Volume = (int)_volumeSlider.Value;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void PlayButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.Media != null)
                _mediaPlayer.Play();
        }

        private void PauseButton_Click(object? sender, RoutedEventArgs e)
        {
            _mediaPlayer.Pause();
        }

        private async void OpenButton_Click(object? sender, RoutedEventArgs e)
        {
            var fileTypes = new[] { "mp4", "avi", "mkv", "mp3", "wav", "flac" };
            var filePickerOptions = new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Media Files")
                    {
                        Patterns = fileTypes.Select(ext => $"*.{ext}").ToArray()
                    }
                }
            };

            var result = await StorageProvider.OpenFilePickerAsync(filePickerOptions);
            if (result.Any())
            {
                var filePath = result[0].Path.LocalPath;
                using var media = new Media(_libVLC, new Uri($"file://{filePath}"));
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();

                // Determine if the media is audio or video
                _isAudio = filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                          filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                          filePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);
                _videoView.IsVisible = !_isAudio;
                _pulseWaveCanvas.IsVisible = _isAudio;

                // Reset seek bar
                _seekSlider.Value = 0;
            }
        }

        private void SeekSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_isDraggingSeekSlider) return;
            if (_mediaPlayer.Media != null)
            {
                float position = (float)(e.NewValue / _mediaPlayer.Length);
                _mediaPlayer.Position = position;
            }
        }

        private void VolumeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            _mediaPlayer.Volume = (int)e.NewValue;
        }

        private void ThemeSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_themeSelector.SelectedIndex == 0) // Dark Theme
            {
                Background = new SolidColorBrush(Color.Parse("#1E1E2F"));
                _playButton.Background = new SolidColorBrush(Color.Parse("#3A3A5A"));
                _pauseButton.Background = new SolidColorBrush(Color.Parse("#3A3A5A"));
                _openButton.Background = new SolidColorBrush(Color.Parse("#3A3A5A"));
            }
            else // Light Theme
            {
                Background = new SolidColorBrush(Color.Parse("#F5F5F5"));
                _playButton.Background = new SolidColorBrush(Color.Parse("#D0D0D0"));
                _pauseButton.Background = new SolidColorBrush(Color.Parse("#D0D0D0"));
                _openButton.Background = new SolidColorBrush(Color.Parse("#D0D0D0"));
            }
        }

        private void UpdateUI()
        {
            if (_mediaPlayer.Media == null) return;

            // Update Seek Bar
            _seekSlider.Maximum = _mediaPlayer.Length;
            if (!_isDraggingSeekSlider)
            {
                _seekSlider.Value = _mediaPlayer.Time;
            }

            // Update Time Display
            _currentTime.Text = TimeSpan.FromMilliseconds(_mediaPlayer.Time).ToString(@"mm\:ss");
            _totalTime.Text = TimeSpan.FromMilliseconds(_mediaPlayer.Length).ToString(@"mm\:ss");

            // Update Pulse Wave Visualization for Audio
            if (_isAudio)
            {
                UpdatePulseWaves();
            }
        }

        private void UpdatePulseWaves()
        {
            _pulseWaveCanvas.Children.Clear();
            double width = _pulseWaveCanvas.Bounds.Width;
            double height = _pulseWaveCanvas.Bounds.Height;
            int barCount = 50;
            double barWidth = width / barCount;

            Random rand = new Random();
            for (int i = 0; i < barCount; i++)
            {
                double barHeight = rand.NextDouble() * height * 0.5 + height * 0.1; // Simulate audio amplitude
                var bar = new Rectangle
                {
                    Width = barWidth * 0.6,
                    Height = barHeight,
                    Fill = new SolidColorBrush(Color.Parse("#BB86FC")),
                    Margin = new Thickness(i * barWidth, (height - barHeight) / 2, 0, 0)
                };
                _pulseWaveCanvas.Children.Add(bar);
            }
        }

        protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.Source == _seekSlider)
            {
                _isDraggingSeekSlider = true;
            }
        }

        protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isDraggingSeekSlider = false;
        }
    }
}