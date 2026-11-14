using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Playback;
using Windows.System;

namespace MyTwitterBackup.Desktop.Controls;

/// <summary>
/// Video without transport controls. A click (or Space/Enter) toggles play/pause and
/// animates a play or pause icon in the center of the video.
/// </summary>
public sealed partial class ClickToPlayVideo : UserControl
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(IMediaPlaybackSource), typeof(ClickToPlayVideo),
        new PropertyMetadata(null, (d, _) => ((ClickToPlayVideo)d).OnSourceChanged()));

    public static readonly DependencyProperty PosterSourceProperty = DependencyProperty.Register(
        nameof(PosterSource), typeof(ImageSource), typeof(ClickToPlayVideo),
        new PropertyMetadata(null, (d, e) => ((ClickToPlayVideo)d).Player.PosterSource = (ImageSource?)e.NewValue));

    public static readonly DependencyProperty AutoPlayProperty = DependencyProperty.Register(
        nameof(AutoPlay), typeof(bool), typeof(ClickToPlayVideo), new PropertyMetadata(false));

    public static readonly DependencyProperty IsLoopingProperty = DependencyProperty.Register(
        nameof(IsLooping), typeof(bool), typeof(ClickToPlayVideo), new PropertyMetadata(false));

    private MediaPlayer? _player;
    private bool _wantsPlay;
    private bool _ended;

    public ClickToPlayVideo()
    {
        InitializeComponent();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    public IMediaPlaybackSource? Source
    {
        get => (IMediaPlaybackSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ImageSource? PosterSource
    {
        get => (ImageSource?)GetValue(PosterSourceProperty);
        set => SetValue(PosterSourceProperty, value);
    }

    /// <summary>Start playing as soon as the video is loaded. Read when the player is created.</summary>
    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    /// <summary>Restart the video when it ends (used for GIFs). Read when the player is created.</summary>
    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => CreatePlayer();

    // Stops playback when the preview changes; otherwise audio keeps playing.
    private void OnUnloaded(object sender, RoutedEventArgs e) => DisposePlayer();

    private void OnSourceChanged()
    {
        if (IsLoaded)
        {
            CreatePlayer();
        }
    }

    private void CreatePlayer()
    {
        DisposePlayer();
        if (Source is null)
        {
            return;
        }

        _wantsPlay = AutoPlay;
        _ended = false;
        _player = new MediaPlayer
        {
            Source = Source,
            AutoPlay = AutoPlay,
            IsLoopingEnabled = IsLooping,
        };
        _player.MediaEnded += Player_MediaEnded;
        Player.SetMediaPlayer(_player);
        PlayHint.Visibility = AutoPlay ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DisposePlayer()
    {
        FlashStoryboard.Stop();
        PlayHint.Visibility = Visibility.Collapsed;
        if (_player is null)
        {
            return;
        }

        _player.MediaEnded -= Player_MediaEnded;
        _player.Pause();
        _player.Source = null;
        Player.SetMediaPlayer(null);
        _player.Dispose();
        _player = null;
    }

    private void TogglePlayback()
    {
        if (_player is null)
        {
            return;
        }

        if (_wantsPlay)
        {
            _player.Pause();
            _wantsPlay = false;
            Flash(PauseGlyph);
        }
        else
        {
            if (_ended)
            {
                _player.PlaybackSession.Position = TimeSpan.Zero;
                _ended = false;
            }

            _player.Play();
            _wantsPlay = true;
            PlayHint.Visibility = Visibility.Collapsed;
            Flash(PlayGlyph);
        }
    }

    private void Flash(string glyph)
    {
        FlashGlyph.Glyph = glyph;
        FlashStoryboard.Stop();
        FlashStoryboard.Begin();
    }

    private void FlashStoryboard_Completed(object? sender, object e)
    {
        if (_player is not null && !_wantsPlay)
        {
            PlayHint.Visibility = Visibility.Visible;
        }
    }

    private void Player_MediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (sender != _player || sender.IsLoopingEnabled)
            {
                return;
            }

            _ended = true;
            _wantsPlay = false;
            PlayHint.Visibility = Visibility.Visible;
        });
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Focus(FocusState.Pointer);
        TogglePlayback();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            e.Handled = true;
            TogglePlayback();
        }
    }
}
