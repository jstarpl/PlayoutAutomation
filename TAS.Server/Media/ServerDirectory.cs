﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TAS.Common;
using TAS.Common.Interfaces;
using TAS.Common.Interfaces.Media;
using TAS.Common.Interfaces.MediaDirectory;

namespace TAS.Server.Media
{
    public class ServerDirectory : WatcherDirectory, IServerDirectory
    {
        internal readonly IPlayoutServerProperties Server;
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public ServerDirectory(IPlayoutServerProperties server, MediaManager manager)
            : base(manager)
        {
            Server = server;
        }

        public override void Initialize()
        {
            if (IsInitialized)
                return;
            EngineController.Database.LoadServerDirectory<ServerMedia>(this, Server.Id);
            base.Initialize();
            Debug.WriteLine(this, "Directory initialized");
        }

        public override void RemoveMedia(IMedia media)
        {
            if (!(media is ServerMedia sm))
                throw new ArgumentException(nameof(media));
            sm.MediaStatus = TMediaStatus.Deleted;
            sm.IsVerified = false;
            sm.Save();
            base.RemoveMedia(sm);
        }

        public override void SweepStaleMedia()
        {
            var currentDateTime = DateTime.UtcNow.Date;
            var staleMediaList = FindMediaList(m => m is ServerMedia && currentDateTime > ((ServerMedia) m).KillDate);
            foreach (var media in staleMediaList)
            {
                var m = (MediaBase)media;
                m.Delete();
            }
        }

        internal override IMedia CreateMedia(IMediaProperties mediaProperties)
        {
            var newFileName = mediaProperties.FileName;
            if (File.Exists(Path.Combine(Folder, newFileName)))
            {
                Logger.Trace("{0}: File {1} already exists", nameof(CreateMedia), newFileName);
                newFileName = FileUtils.GetUniqueFileName(Folder, newFileName);
            }
            var result = (new ServerMedia
            {
                MediaName = mediaProperties.MediaName,
                MediaGuid = mediaProperties.MediaGuid == Guid.Empty || FindMediaByMediaGuid(mediaProperties.MediaGuid) != null ? Guid.NewGuid() : mediaProperties.MediaGuid,
                LastUpdated = mediaProperties.LastUpdated,
                MediaType = mediaProperties.MediaType == TMediaType.Unknown ? TMediaType.Movie : mediaProperties.MediaType,
                FileName = newFileName,
                MediaStatus = TMediaStatus.Required,
            });
            result.CloneMediaProperties(mediaProperties);
            AddMedia(result);
            return result;
        }

        public event EventHandler<MediaEventArgs> MediaSaved;

        protected override bool AcceptFile(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            return FileUtils.VideoFileTypes.Contains(ext) || FileUtils.StillFileTypes.Contains(ext);
        }

        internal void OnMediaSaved(MediaBase media)
        {
            MediaSaved?.Invoke(this, new MediaEventArgs(media));
        }

        protected override void OnMediaRenamed(MediaBase media, string newFullPath)
        {
            ((ServerMedia)media).Save();
            base.OnMediaRenamed(media, newFullPath);
        }

        protected override void EnumerateFiles(string directory, string filter, bool includeSubdirectories, CancellationToken cancelationToken)
        {
            base.EnumerateFiles(directory, filter, includeSubdirectories, cancelationToken);
            var unverifiedFiles = FindMediaList(mf => ((ServerMedia)mf).IsVerified == false);
            unverifiedFiles.ForEach(media => media.Verify(true));
        }

        protected override IMedia AddMediaFromPath(string fullPath, DateTime lastUpdated)
        {
            if (!AcceptFile(fullPath))
                return null;
            if (FindMediaFirstByFullPath(fullPath) is ServerMedia newMedia)
                return newMedia;
            var relativeName = fullPath.Substring(Folder.Length);
            var fileName = Path.GetFileName(relativeName);
            var mediaType = FileUtils.VideoFileTypes.Contains(Path.GetExtension(fullPath).ToLowerInvariant()) ? TMediaType.Movie : TMediaType.Still;
            newMedia = new ServerMedia
            {
                MediaName = FileUtils.GetFileNameWithoutExtension(fullPath, mediaType).ToUpper(),
                LastUpdated = lastUpdated,
                MediaType = mediaType,
                MediaGuid = Guid.NewGuid(),
                FileName = Path.GetFileName(relativeName),
                Folder = relativeName.Substring(0, relativeName.Length - fileName.Length).Trim(PathSeparator),
            };
            AddMedia(newMedia);
            newMedia.Save();
            return newMedia;
        }
    }
}