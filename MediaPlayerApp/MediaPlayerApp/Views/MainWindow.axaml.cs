using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System.IO;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using System.Timers;
using Avalonia;

namespace MediaPlayerApp
{
    public partial class MainWindow : Window
    {
        private readonly LibVLC _libVLC;
        private readonly MediaPlayer _mediaPlayer;
        private readonly VideoView _videoView;
        private readonly Canvas _pulseWaveCanvas;
        private readonly Slider _seekSlider;
        private readonly Slider _volumeSlider;
        private readonly TextBlock _currentTime;
        private readonly TextBlock _totalTime;
        private readonly TextBlock _mediaInfo;
        private readonly Button _playPauseButton;
        private readonly Button _stopButton;
        private readonly Button _openButton;
        private readonly Button _fullScreenButton;
        private readonly Button _loopButton;
        private readonly Button _shuffleButton;
        private readonly Button _clearPlaylistButton;
        private readonly ListBox _playlist;
        private bool _isDraggingSeekSlider;
        private bool _isAudio;
        private bool _isLooping;
        private bool _isShuffling;
        private readonly System.Timers.Timer _updateTimer;
        private readonly List<string> _mediaFiles = new();
        private int _currentMediaIndex = -1;
        private readonly Random _random = new();

        public MainWindow()
        {
            InitializeComponent();
            Core.Initialize();

            string vlcPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "vlc",
                OperatingSystem.IsWindows() ? "windows" : "linux");
            _libVLC = new LibVLC("--no-xlib"); // Removed --plugin-path
            _mediaPlayer = new MediaPlayer(_libVLC);

            // Initialize controls with null checks
            _videoView = this.FindControl<VideoView>("VideoView") ?? throw new InvalidOperationException("VideoView control not found.");
            _pulseWaveCanvas = this.FindControl<Canvas>("PulseWaveCanvas") ?? throw new InvalidOperationException("PulseWaveCanvas control not found.");
            _seekSlider = this.FindControl<Slider>("SeekSlider") ?? throw new InvalidOperationException("SeekSlider control not found.");
            _volumeSlider = this.FindControl<Slider>("VolumeSlider") ?? throw new InvalidOperationException("VolumeSlider control not found.");
            _currentTime = this.FindControl<TextBlock>("CurrentTime") ?? throw new InvalidOperationException("CurrentTime control not found.");
            _totalTime = this.FindControl<TextBlock>("TotalTime") ?? throw new InvalidOperationException("TotalTime control not found.");
            _mediaInfo = this.FindControl<TextBlock>("MediaInfo") ?? throw new InvalidOperationException("MediaInfo control not found.");
            _playPauseButton = this.FindControl<Button>("PlayPauseButton") ?? throw new InvalidOperationException("PlayPauseButton control not found.");
            _stopButton = this.FindControl<Button>("StopButton") ?? throw new InvalidOperationException("StopButton control not found.");
            _openButton = this.FindControl<Button>("OpenButton") ?? throw new InvalidOperationException("OpenButton control not found.");
            _fullScreenButton = this.FindControl<Button>("FullScreenButton") ?? throw new InvalidOperationException("FullScreenButton control not found.");
            _loopButton = this.FindControl<Button>("LoopButton") ?? throw new InvalidOperationException("LoopButton control not found.");
            _shuffleButton = this.FindControl<Button>("ShuffleButton") ?? throw new InvalidOperationException("ShuffleButton control not found.");
            _clearPlaylistButton = this.FindControl<Button>("ClearPlaylistButton") ?? throw new InvalidOperationException("ClearPlaylistButton control not found.");
            _playlist = this.FindControl<ListBox>("Playlist") ?? throw new InvalidOperationException("Playlist control not found.");

            _videoView.MediaPlayer = _mediaPlayer;

            // Attach events
            _seekSlider.AddHandler(Slider.ValueChangedEvent, SeekSlider_ValueChanged);
            _volumeSlider.AddHandler(Slider.ValueChangedEvent, VolumeSlider_ValueChanged);
            _playPauseButton.Click += PlayPauseButton_Click;
            _stopButton.Click += StopButton_Click;
            _openButton.Click += OpenButton_Click;
            _fullScreenButton.Click += FullScreenButton_Click;
            _loopButton.Click += LoopButton_Click;
            _shuffleButton.Click += ShuffleButton_Click;
            _clearPlaylistButton.Click += ClearPlaylistButton_Click;
            _playlist.DoubleTapped += Playlist_DoubleTapped;
            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            this.AddHandler(DragDrop.DropEvent, Drop);
            this.AddHandler(DragDrop.DragOverEvent, DragOver);
            this.KeyDown += MainWindow_KeyDown;

            // Timer for UI updates
            _updateTimer = new System.Timers.Timer(100);
            _updateTimer.Elapsed += (s, e) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateUI);
            _updateTimer.Start();

            // Set initial volume
            _mediaPlayer.Volume = (int)_volumeSlider.Value;

            // Enable drag-and-drop
            DragDrop.SetAllowDrop(this, true);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void PlayPauseButton_Click(object? sender, RoutedEventArgs? e)
        {
            if (_mediaPlayer.Media == null) return;
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                _playPauseButton.Content = "Play";
            }
            else
            {
                _mediaPlayer.Play();
                _playPauseButton.Content = "Pause";
            }
        }

        private void StopButton_Click(object? sender, RoutedEventArgs? e)
        {
            _mediaPlayer.Stop();
            _seekSlider.Value = 0;
            _playPauseButton.Content = "Play";
        }

        private async void OpenButton_Click(object? sender, RoutedEventArgs? e)
        {
            var fileTypes = new[] { "mp4", "avi", "mkv", "mp3", "wav", "flac" };
            var filePickerOptions = new FilePickerOpenOptions
            {
                AllowMultiple = true,
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
                foreach (var file in result)
                {
                    _mediaFiles.Add(file.Path.LocalPath);
                    _playlist.Items.Add(System.IO.Path.GetFileName(file.Path.LocalPath));
                }
                if (_currentMediaIndex == -1)
                {
                    _currentMediaIndex = 0;
                    await PlayMedia(_mediaFiles[_currentMediaIndex]);
                }
            }
        }

        private async Task PlayMedia(string filePath)
        {
            try
            {
                using var media = new Media(_libVLC, new Uri($"file://{filePath}"));
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();

                // Determine if media is audio
                _isAudio = filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                           filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                           filePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);
                _videoView.IsVisible = !_isAudio;
                _pulseWaveCanvas.IsVisible = _isAudio;
                _fullScreenButton.IsVisible = !_isAudio;

                // Update media info
                await media.Parse(MediaParseOptions.ParseLocal);
                _mediaInfo.Text = $"Title: {media.Meta(MetadataType.Title) ?? System.IO.Path.GetFileName(filePath)}";
                _playPauseButton.Content = "Pause";
                _playlist.SelectedIndex = _currentMediaIndex;
            }
            catch (Exception ex)
            {
                _mediaInfo.Text = $"Error playing media: {ex.Message}";
            }
        }

        private void SeekSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_isDraggingSeekSlider || _mediaPlayer.Media == null) return;
            float position = (float)(e.NewValue / _mediaPlayer.Length);
            _mediaPlayer.Position = position;
        }

        private void VolumeSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            _mediaPlayer.Volume = (int)e.NewValue;
        }

        private void FullScreenButton_Click(object? sender, RoutedEventArgs? e)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }

        private void LoopButton_Click(object? sender, RoutedEventArgs? e)
        {
            _isLooping = !_isLooping;
            _loopButton.Content = $"Loop: {(_isLooping ? "On" : "Off")}";
        }

        private void ShuffleButton_Click(object? sender, RoutedEventArgs? e)
        {
            _isShuffling = !_isShuffling;
            _shuffleButton.Content = $"Shuffle: {(_isShuffling ? "On" : "Off")}";
        }

        private void ClearPlaylistButton_Click(object? sender, RoutedEventArgs? e)
        {
            _mediaFiles.Clear();
            _playlist.Items.Clear();
            _currentMediaIndex = -1;
            _mediaPlayer.Stop();
            _mediaInfo.Text = "No media loaded";
            _seekSlider.Value = 0;
            _playPauseButton.Content = "Play";
            _videoView.IsVisible = true;
            _pulseWaveCanvas.IsVisible = false;
        }

        private async void Playlist_DoubleTapped(object? sender, RoutedEventArgs? e)
        {
            if (_playlist.SelectedIndex >= 0)
            {
                _currentMediaIndex = _playlist.SelectedIndex;
                await PlayMedia(_mediaFiles[_currentMediaIndex]);
            }
        }

        private async void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_isLooping)
                {
                    await PlayMedia(_mediaFiles[_currentMediaIndex]);
                }
                else if (_isShuffling)
                {
                    _currentMediaIndex = _random.Next(_mediaFiles.Count);
                    await PlayMedia(_mediaFiles[_currentMediaIndex]);
                }
                else if (_currentMediaIndex < _mediaFiles.Count - 1)
                {
                    _currentMediaIndex++;
                    await PlayMedia(_mediaFiles[_currentMediaIndex]);
                }
                else
                {
                    _mediaPlayer.Stop();
                    _playPauseButton.Content = "Play";
                }
            });
        }

        private void DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles()?.Select(f => f.Path.LocalPath).Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                                                                      f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                                                                      f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                                                                      f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                                                                      f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                                                                      f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        _mediaFiles.Add(file);
                        _playlist.Items.Add(System.IO.Path.GetFileName(file));
                    }
                    if (_currentMediaIndex == -1 && _mediaFiles.Any())
                    {
                        _currentMediaIndex = 0;
                        await PlayMedia(_mediaFiles[_currentMediaIndex]);
                    }
                }
            }
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Space:
                    PlayPauseButton_Click(sender, null);
                    break;
                case Key.Left:
                    if (_mediaPlayer.Media != null)
                        _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 5000);
                    break;
                case Key.Right:
                    if (_mediaPlayer.Media != null)
                        _mediaPlayer.Time = Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 5000);
                    break;
                case Key.Add:
                case Key.OemPlus:
                    _volumeSlider.Value = Math.Min(100, _volumeSlider.Value + 5);
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    _volumeSlider.Value = Math.Max(0, _volumeSlider.Value - 5);
                    break;
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
            _currentTime.Text = TimeSpan.FromMilliseconds(_mediaPlayer.Time).ToString(@"mm:ss");
            _totalTime.Text = TimeSpan.FromMilliseconds(_mediaPlayer.Length).ToString(@"mm:ss");

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
            int barCount = 60;
            double barWidth = width / barCount;
            double time = _mediaPlayer.Time / 1000.0; // Time in seconds

            for (int i = 0; i < barCount; i++)
            {
                // Simulate audio amplitude with a dynamic wave
                double phase = (i / (double)barCount) * 2 * Math.PI + time * 0.5;
                double amplitude = Math.Sin(phase) * 0.3 + 0.7;
                double barHeight = amplitude * height * 0.4 + height * 0.1;

                var bar = new Rectangle
                {
                    Width = barWidth * 0.7,
                    Height = barHeight,
                    Fill = new LinearGradientBrush
                    {
                        GradientStops = new GradientStops
                        {
                            new GradientStop(Color.Parse("#BB86FC"), 0),
                            new GradientStop(Color.Parse("#6200EE"), 1)
                        },
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
                    },
                    Margin = new Thickness(i * barWidth, (height - barHeight) / 2, 0, 0)
                };
                _pulseWaveCanvas.Children.Add(bar);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.Source == _seekSlider)
            {
                _isDraggingSeekSlider = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isDraggingSeekSlider = false;
        }
    }
}