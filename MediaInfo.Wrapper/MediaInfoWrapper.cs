#region Copyright (C) 2005-2017 Team MediaPortal

// Copyright (C) 2005-2017 Team MediaPortal
// http://www.team-mediaportal.com
// 
// MediaPortal is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 2 of the License, or
// (at your option) any later version.
// 
// MediaPortal is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MediaPortal. If not, see <http://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

using JetBrains.Annotations;

namespace MediaInfo
{
  /// <summary>
  /// Describes method and properties to retrieve information from media source
  /// </summary>
  public class MediaInfoWrapper
  {
    #region private vars

    private static readonly Dictionary<string, bool> SubTitleExtensions = new Dictionary<string, bool> 
    {
      { ".AQT", true },
      { ".ASC", true },
      { ".ASS", true },
      { ".DAT", true },
      { ".DKS", true },
      { ".IDX", true },
      { ".JS", true },
      { ".JSS", true },
      { ".LRC", true },
      { ".MPL", true },
      { ".OVR", true },
      { ".PAN", true },
      { ".PJS", true },
      { ".PSB", true },
      { ".RT", true },
      { ".RTF", true },
      { ".S2K", true },
      { ".SBT", true },
      { ".SCR", true },
      { ".SMI", true },
      { ".SON", true },
      { ".SRT", true },
      { ".SSA", true },
      { ".SST", true },
      { ".SSTS", true },
      { ".STL", true },
      { ".SUB", true },
      { ".TXT", true },
      { ".VKT", true },
      { ".VSF", true },
      { ".ZEG", true },
    };

    #endregion

    #region ctor's

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaInfoWrapper"/> class.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    public MediaInfoWrapper(string filePath)
      : this (filePath, Environment.Is64BitProcess ? @".\x64" : @".\x86")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaInfoWrapper"/> class.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="pathToDll">The path to DLL.</param>
    private MediaInfoWrapper(string filePath, string pathToDll)
    {
      MediaInfoNotloaded = false;
      VideoStreams = new List<VideoStream>();
      AudioStreams = new List<AudioStream>();
      Subtitles = new List<SubtitleStream>();
      Chapters = new List<Chapter>();
      MenuStreams = new List<MenuStream>();

      if (!MediaInfoExist(pathToDll))
      {
        MediaInfoNotloaded = true;
        return;
      }

      if (string.IsNullOrEmpty(filePath))
      {
        MediaInfoNotloaded = true;
        return;
      }

      var isTv = filePath.IsLiveTv();
      var isRadio = filePath.IsLiveTv();
      var isRTSP = filePath.IsRTSP(); //rtsp for live TV and recordings.
      var isAvStream = filePath.IsAvStream(); //other AV streams
      var isNetwork = filePath.IsNetwork();

      //currently disabled for all tv/radio
      if (isTv || isRadio || isRTSP)
      {
        MediaInfoNotloaded = true;
        return;
      }

      NumberFormatInfo providerNumber = new NumberFormatInfo { NumberDecimalSeparator = "." };

      try
      {
        // Analyze local file for DVD and BD
        if (!(isAvStream || isNetwork))
        {
          if (filePath.EndsWith(".ifo", StringComparison.OrdinalIgnoreCase))
          {
            IsDvd = true;
            var path = Path.GetDirectoryName(filePath) ?? string.Empty;
            var bups = Directory.GetFiles(path, "*.BUP", SearchOption.TopDirectoryOnly);
            var programBlocks = new List<Tuple<string, int>>();
            foreach (var bupFile in bups)
            {
              using (var mi = new MediaInfo(pathToDll))
              {
                mi.Open(bupFile);
                var profile = mi.Get(StreamKind.General, 0, "Format_Profile");
                if (profile == "Program")
                {
                  double duration;
                  double.TryParse(mi.Get(StreamKind.Video, 0, "Duration"), NumberStyles.AllowDecimalPoint, providerNumber, out duration);
                  programBlocks.Add(new Tuple<string, int>(bupFile, (int)duration));
                }
              }
            }
            // get all other info from main title's 1st vob
            if (programBlocks.Any())
            {
              VideoDuration = programBlocks.Max(x => x.Item2);
              filePath = programBlocks.First(x => x.Item2 == VideoDuration).Item1;
            }
          }
          else if (filePath.EndsWith(".bdmv", StringComparison.OrdinalIgnoreCase))
          {
            IsBluRay = true;
            filePath = Path.GetDirectoryName(filePath);
          }

          HasExternalSubtitles = !string.IsNullOrEmpty(filePath) && CheckHasExternalSubtitles(filePath);
        }

        using (var mediaInfo = new MediaInfo(pathToDll))
        {
          Version = mediaInfo.Option("Info_Version");
          mediaInfo.Open(filePath);

          var streamNumber = 0;
          //Video
          for (var i = 0; i < mediaInfo.CountGet(StreamKind.Video); ++i)
          {
            VideoStreams.Add(new VideoStream(mediaInfo, streamNumber++, i));
          }

          if (VideoDuration == 0)
          {
            double duration;
            double.TryParse(
              mediaInfo.Get(StreamKind.Video, 0, "Duration"),
              NumberStyles.AllowDecimalPoint,
              providerNumber,
              out duration);
            VideoDuration = (int)duration;
          }

          //Audio
          for (var i = 0; i < mediaInfo.CountGet(StreamKind.Audio); ++i)
          {
            AudioStreams.Add(new AudioStream(mediaInfo, streamNumber++, i));
          }

          //Subtitles
          for (var i = 0; i < mediaInfo.CountGet(StreamKind.Text); ++i)
          {
            Subtitles.Add(new SubtitleStream(mediaInfo, streamNumber++, i));
          }

          for (var i = 0; i < mediaInfo.CountGet(StreamKind.Other); ++i)
          {
            Chapters.Add(new Chapter(mediaInfo, streamNumber++, i));
          }

          for (var i = 0; i < mediaInfo.CountGet(StreamKind.Menu); ++i)
          {
            MenuStreams.Add(new MenuStream(mediaInfo, streamNumber++, i));
          }

          MediaInfoNotloaded = VideoStreams.Count == 0 && AudioStreams.Count == 0 && Subtitles.Count == 0;

          // Produce copability properties
          if (!MediaInfoNotloaded)
          {
            BestVideoStream =
              VideoStreams.OrderByDescending(
                x =>
                  (long)x.Width * x.Height * x.BitDepth * (x.Stereoscopic == StereoMode.Mono ? 1L : 2L)
                  * x.FrameRate).FirstOrDefault();
            VideoCodec = BestVideoStream?.CodecName ?? string.Empty;
            VideoResolution = BestVideoStream?.Resolution ?? string.Empty;
            Width = BestVideoStream?.Width ?? 0;
            Height = BestVideoStream?.Height ?? 0;
            IsInterlaced = BestVideoStream?.Interlaced ?? false;
            Framerate = BestVideoStream?.FrameRate ?? 0;
            ScanType = BestVideoStream != null
                         ? mediaInfo.Get(StreamKind.Video, BestVideoStream.StreamPosition, "ScanType").ToLower()
                         : string.Empty;
            AspectRatio = BestVideoStream != null
                            ? mediaInfo.Get(StreamKind.Video, BestVideoStream.StreamPosition, "DisplayAspectRatio")
                            : string.Empty;
            AspectRatio = AspectRatio == "4:3" || AspectRatio == "1.333" ? "fullscreen" : "widescreen";

            BestAudioStream = AudioStreams.OrderByDescending(x => x.Channel * 10000000 + x.Bitrate).FirstOrDefault();
            AudioCodec = BestAudioStream?.CodecName ?? string.Empty;
            AudioRate = (int?)BestAudioStream?.Bitrate ?? 0;
            AudioChannels = BestAudioStream?.Channel ?? 0;
            AudioChannelsFriendly = BestAudioStream?.AudioChannelsFriendly ?? string.Empty;
          }
          else
          {
            VideoCodec = string.Empty;
            VideoResolution = string.Empty;
            ScanType = string.Empty;
            AspectRatio = string.Empty;

            AudioCodec = string.Empty;
            AudioChannelsFriendly = string.Empty;
          }
        }
      }
      catch
      {
        // ignored
      }
    }

    #endregion

    /// <summary>
    /// Checks if mediaInfo.dll file exist.
    /// </summary>
    /// <param name="pathToDll">The path to mediaInfo.dll</param>
    /// <returns>Returns <b>true</b> if mediaInfo.dll is exists; elsewhere <b>false</b>.</returns>
    [PublicAPI]
    public static bool MediaInfoExist(string pathToDll)
    {
      return File.Exists(Path.Combine(pathToDll, "MediaInfo.dll"));
    }

    #region private methods

    private static bool CheckHasExternalSubtitles(string strFile)
    {
      if (string.IsNullOrEmpty(strFile))
      {
        return false;
      }

      var filenameNoExt = Path.GetFileNameWithoutExtension(strFile);
      try
      {
        return Directory.GetFiles(Path.GetDirectoryName(strFile) ?? string.Empty, filenameNoExt + "*")
          .Select(file => new FileInfo(file))
          .Any(fi => SubTitleExtensions.ContainsKey(fi.Extension.ToUpper()));
      }
      catch
      {
        return false;
      }
    }

    #endregion

    #region public video related properties

    /// <summary>
    /// Gets the duration of the video.
    /// </summary>
    /// <value>
    /// The duration of the video.
    /// </value>
    [PublicAPI]
    public int VideoDuration { get; }

    /// <summary>
    /// Gets a value indicating whether this instance has video.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance has video; otherwise, <c>false</c>.
    /// </value>
    [PublicAPI]
    public bool HasVideo => VideoStreams.Count > 0;

    /// <summary>
    /// Gets the video streams.
    /// </summary>
    /// <value>
    /// The video streams.
    /// </value>
    [PublicAPI]
    public IList<VideoStream> VideoStreams { get; }

    /// <summary>
    /// Gets the best video stream.
    /// </summary>
    /// <value>
    /// The best video stream.
    /// </value>
    [PublicAPI]
    public VideoStream BestVideoStream { get; }

    /// <summary>
    /// Gets the video codec.
    /// </summary>
    /// <value>
    /// The video codec.
    /// </value>
    [PublicAPI]
    public string VideoCodec { get; }

    /// <summary>
    /// Gets the video frame rate.
    /// </summary>
    /// <value>
    /// The video frame rate.
    /// </value>
    [PublicAPI]
    public double Framerate { get; }

    /// <summary>
    /// Gets the video width.
    /// </summary>
    /// <value>
    /// The video width.
    /// </value>
    [PublicAPI]
    public int Width { get; }

    /// <summary>
    /// Gets the video height.
    /// </summary>
    /// <value>
    /// The video height.
    /// </value>
    [PublicAPI]
    public int Height { get; }

    /// <summary>
    /// Gets the video aspect ratio.
    /// </summary>
    /// <value>
    /// The video aspect ratio.
    /// </value>
    [PublicAPI]
    public string AspectRatio { get; }

    /// <summary>
    /// Gets the type of the scan.
    /// </summary>
    /// <value>
    /// The type of the scan.
    /// </value>
    [PublicAPI]
    public string ScanType { get; }

    /// <summary>
    /// Gets a value indicating whether video is interlaced.
    /// </summary>
    /// <value>
    ///   <c>true</c> if video is interlaced; otherwise, <c>false</c>.
    /// </value>
    [PublicAPI]
    public bool IsInterlaced { get; }

    /// <summary>
    /// Gets the video resolution.
    /// </summary>
    /// <value>
    /// The video resolution.
    /// </value>
    [PublicAPI]
    public string VideoResolution { get; }

    #endregion

    #region public audio related properties

    /// <summary>
    /// Gets the audio streams.
    /// </summary>
    /// <value>
    /// The audio streams.
    /// </value>
    [PublicAPI]
    public IList<AudioStream> AudioStreams { get; }

    /// <summary>
    /// Gets the best audio stream.
    /// </summary>
    /// <value>
    /// The best audio stream.
    /// </value>
    [PublicAPI]
    public AudioStream BestAudioStream { get; }

    /// <summary>
    /// Gets the audio codec.
    /// </summary>
    /// <value>
    /// The audio codec.
    /// </value>
    [PublicAPI]
    public string AudioCodec { get; }

    /// <summary>
    /// Gets the audio bitrate.
    /// </summary>
    /// <value>
    /// The audio bitrate.
    /// </value>
    [PublicAPI]
    public int AudioRate { get; }

    /// <summary>
    /// Gets the count of audio channels.
    /// </summary>
    /// <value>
    /// The count of audio channels.
    /// </value>
    [PublicAPI]
    public int AudioChannels { get; }

    /// <summary>
    /// Gets the audio channels friendly name.
    /// </summary>
    /// <value>
    /// The audio channels friendly name.
    /// </value>
    [PublicAPI]
    public string AudioChannelsFriendly { get; }

    #endregion

    #region public subtitles related properties

    /// <summary>
    /// Gets the list of media subtitles.
    /// </summary>
    /// <value>
    /// The media subtitles.
    /// </value>
    [PublicAPI]
    public IList<SubtitleStream> Subtitles { get; }

    /// <summary>
    /// Gets a value indicating whether media has internal or external subtitles.
    /// </summary>
    /// <value>
    ///   <c>true</c> if media has subtitles; otherwise, <c>false</c>.
    /// </value>
    [PublicAPI]
    public bool HasSubtitles => HasExternalSubtitles || Subtitles.Count > 0;

    /// <summary>
    /// Gets a value indicating whether this instance has external subtitles.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance has external subtitles; otherwise, <c>false</c>.
    /// </value>
    [PublicAPI]
    public bool HasExternalSubtitles { get; }

    #endregion

    #region public chapters related properties

    /// <summary>
    /// Gets the media chapters.
    /// </summary>
    /// <value>
    /// The media chapters.
    /// </value>
    [PublicAPI]
    public IList<Chapter> Chapters { get; }

    /// <summary>
    /// Gets a value indicating whether media has chapters.
    /// </summary>
    /// <value>
    ///   <c>true</c> if media has chapters; otherwise, <c>false</c>.
    /// </value>
    [PublicAPI]
    public bool HasChapters => Chapters.Count > 0;

    #endregion

    #region public menu related properties

    /// <summary>
    /// Gets the menu streams from media.
    /// </summary>
    /// <value>
    /// The menu streams.
    /// </value>
    [PublicAPI]
    public IList<MenuStream> MenuStreams { get; }

    /// <summary>
    /// Gets a value indicating whether media has menu.
    /// </summary>
    /// <value>
    ///   <c>true</c> if media has menu; otherwise, <c>false</c>.
    /// </value>
    [PublicAPI]
    public bool HasMenu => MenuStreams.Count > 0;

    #endregion

    /// <summary>
    /// Gets a value indicating whether media is DVD.
    /// </summary>
    /// <value>
    ///   <c>true</c> if media is DVD; otherwise, <c>false</c>.
    /// </value>
    public bool IsDvd { get; }

    /// <summary>
    /// Gets a value indicating whether media is BluRay.
    /// </summary>
    /// <value>
    ///   <c>true</c> if media is BluRay; otherwise, <c>false</c>.
    /// </value>
    public bool IsBluRay { get; }

    /// <summary>
    /// Gets a value indicating whether media information was not loaded.
    /// </summary>
    /// <value>
    ///   <c>true</c> if media information was not loaded; otherwise, <c>false</c>.
    /// </value>
    public bool MediaInfoNotloaded { get; }

    /// <summary>
    /// Gets the mediainfo.dll version.
    /// </summary>
    /// <value>
    /// The mediainfo.dll version.
    /// </value>
    [PublicAPI]
    public string Version { get; }
  }
}