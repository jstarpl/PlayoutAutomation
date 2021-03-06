﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using TAS.Client.Common;
using TAS.Common;
using TAS.Common.Interfaces;
using TAS.Common.Interfaces.Media;
using TAS.Common.Interfaces.MediaDirectory;
using resources = TAS.Client.Common.Properties.Resources;

namespace TAS.Client.ViewModels
{
    public class MediaSearchViewmodel : ViewModelBase
    {
        private readonly TMediaType _mediaType;
        private readonly VideoFormatDescription _videoFormatDescription;
        private IWatcherDirectory _searchDirectory;
        private ICollectionView _itemsView;
        public readonly VideoLayer Layer;

        private IEvent _baseEvent;
        private string[] _searchTextSplit = new string[0];
        private string _searchText = string.Empty;
        private object _mediaCategory;
        private MediaViewViewmodel _selectedItem;
        private string _okButtonText = "OK";

        public MediaSearchViewmodel(IPreview preview, IEngine engine, TMediaType mediaType, VideoLayer layer,
            bool closeAfterAdd, VideoFormatDescription videoFormatDescription)
        {
            Engine = engine;
            Layer = layer;
            if (mediaType == TMediaType.Movie)
            {
                _videoFormatDescription = engine.FormatDescription;
                if (preview != null)
                    PreviewViewmodel = new PreviewViewmodel(engine, preview) { IsSegmentsVisible = true };
            }
            else
                _videoFormatDescription = videoFormatDescription;
            _mediaType = mediaType;
            if (PreviewViewmodel != null)
                PreviewViewmodel.PropertyChanged += _onPreviewViewModelPropertyChanged;
            CommandAdd = new UiCommand(_add, _allowAdd);
            _mediaCategory = MediaCategories.FirstOrDefault();
            NewEventStartType = TStartType.After;
            SetupSearchDirectory(closeAfterAdd, mediaType);
        }

        private async void SetupSearchDirectory(bool closeAfterAdd, TMediaType mediaType)
        {
            var pri = _mediaType == TMediaType.Animation
                ? (IWatcherDirectory) Engine.MediaManager.AnimationDirectoryPRI
                : Engine.MediaManager.MediaDirectoryPRI;
            var sec = _mediaType == TMediaType.Animation
                ? (IWatcherDirectory) Engine.MediaManager.AnimationDirectorySEC
                : Engine.MediaManager.MediaDirectorySEC;
            _searchDirectory = pri != null && await Task.Run(() => pri.DirectoryExists())
                ? pri
                : sec != null && await Task.Run(() => sec.DirectoryExists())
                    ? sec
                    : null;
            if (_searchDirectory == null)
                return;

            _searchDirectory.MediaAdded += _searchDirectory_MediaAdded;
            _searchDirectory.MediaRemoved += _searchDirectory_MediaRemoved;
            _searchDirectory.MediaVerified += _searchDirectory_MediaVerified;

            if (!closeAfterAdd)
                OkButtonText = resources._button_Add;
            Items = new ObservableCollection<MediaViewViewmodel>(
                _searchDirectory.GetFiles()
                    .Where(m => _canAddMediaToCollection(m, mediaType))
                    .Select(m => new MediaViewViewmodel(m)));
            _itemsView = CollectionViewSource.GetDefaultView(Items);
            _itemsView.Filter += _itemsFilter;

            if (mediaType == TMediaType.Movie || mediaType == TMediaType.Audio)
                SortByIngestDate();
            else
                SortByName();
        }

        public ObservableCollection<MediaViewViewmodel> Items { get => _items; private set => SetField(ref _items, value); }

        public ICommand CommandAdd { get; }

        public PreviewViewmodel PreviewViewmodel { get; }

        public Views.MediaSearchView Window { get; set; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                var oldValue = _searchText;
                value = value.ToLower();
                if (!SetField(ref _searchText, value))
                    return;
                _searchTextSplit = value.Split(' ');
                if (_mediaType == TMediaType.Audio || _mediaType == TMediaType.Movie)
                {
                    if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(oldValue))
                        SortByIngestDate();
                    if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(oldValue))
                        SortByName();
                }
                _itemsView?.Refresh();
                _itemsView?.MoveCurrentToFirst();
                SelectedItem = _itemsView?.CurrentItem as MediaViewViewmodel;
            }
        }

        public IEnumerable<object> MediaCategories { get; } =
            new List<object> { resources._all_ }.Concat(Enum.GetValues(typeof(TMediaCategory)).Cast<object>());

        public object MediaCategory
        {
            get => _mediaCategory;
            set
            {
                if (SetField(ref _mediaCategory, value))
                {
                    NotifyPropertyChanged(nameof(IsShowCategoryColumn));
                    _itemsView?.Refresh();
                }
            }
        }

        public IMedia SelectedMedia => _selectedItem?.Media;

        public MediaViewViewmodel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (value != _selectedItem)
                {
                    _selectedItem = value;
                    var media = SelectedMedia;
                    if (PreviewViewmodel != null)
                        PreviewViewmodel.SelectedMedia = media;
                    InvalidateRequerySuggested();
                }
            }
        }

        public bool IsMovie => _mediaType == TMediaType.Movie;

        public bool IsShowCategoryColumn => _mediaType == TMediaType.Movie && !(_mediaCategory is TMediaCategory);


        public bool CanEnableCGElements => Engine.CGElementsController != null && _mediaType == TMediaType.Movie;

        public string OkButtonText
        {
            get => _okButtonText;
            set
            {
                if (value == _okButtonText)
                    return;
                _okButtonText = value;
                NotifyPropertyChanged(nameof(OkButtonText));
            }
        }

        public event EventHandler<MediaSearchEventArgs> MediaChoosen;

        internal TStartType NewEventStartType;
        private ObservableCollection<MediaViewViewmodel> _items;

        internal IEvent BaseEvent
        {
            get => _baseEvent;
            set
            {
                IEvent b = _baseEvent;
                if (b != value)
                {
                    if (b != null)
                        b.PropertyChanged -= _onBaseEventPropertyChanged;
                    _baseEvent = value;
                    InvalidateRequerySuggested();
                    if (value != null)
                        value.PropertyChanged += _onBaseEventPropertyChanged;
                }
            }
        }

        public IEngine Engine { get; }

        private bool _canAddMediaToCollection(IMedia media, TMediaType requiredMediaType)
        {
            return
                media != null
                && media.MediaType == requiredMediaType
                &&
                   ((requiredMediaType == TMediaType.Still && (_videoFormatDescription == null || _videoFormatDescription.IsWideScreen == media.FormatDescription().IsWideScreen))
                 || (media.MediaType == TMediaType.Movie && media.FrameRate().Equals(_videoFormatDescription?.FrameRate))
                 || media.MediaType == TMediaType.Animation);
        }

        private void _searchDirectory_MediaVerified(object sender, MediaEventArgs e)
        {
            OnUiThread(() => _itemsView?.Refresh());
        }

        private void _searchDirectory_MediaRemoved(object sender, MediaEventArgs e)
        {
            OnUiThread(() =>
            {
                var mvm = Items.FirstOrDefault(m => m.Media == e.Media);
                if (mvm == null)
                    return;
                Items.Remove(mvm);
                mvm.Dispose();
                _itemsView?.Refresh();
            });
        }

        private void _searchDirectory_MediaAdded(object sender, MediaEventArgs e)
        {
            OnUiThread(() =>
            {
                IMedia media = e.Media;
                if (media != null
                    && _canAddMediaToCollection(media, _mediaType))
                    Items.Add(new MediaViewViewmodel(media));
            });
        }

        private bool _itemsFilter(object item)
        {
            var mvm = item as MediaViewViewmodel;
            if (mvm?.Media == null)
                return false;
            string mediaName = mvm.MediaName.ToLower();
            return mvm.MediaStatus == TMediaStatus.Available
                && (!(MediaCategory is TMediaCategory) || (MediaCategory as TMediaCategory?) == mvm.MediaCategory)
                && (_searchTextSplit.All(s => mediaName.Contains(s)));
        }

        private TimeSpan GetTCStart()
        {
            var pvlm = PreviewViewmodel?.LoadedMedia;
            if (pvlm != null && pvlm.MediaGuid == SelectedMedia?.MediaGuid)
            {
                var s = PreviewViewmodel.SelectedSegment;
                if (s != null)
                    return s.TcIn;
                if (PreviewViewmodel.TcIn != pvlm.TcPlay ||
                    PreviewViewmodel.TcOut != pvlm.TcPlay + pvlm.DurationPlay)
                    return PreviewViewmodel.TcIn;
            }
            return SelectedItem?.SelectedSegment?.TcIn ?? SelectedItem?.TcPlay ?? TimeSpan.Zero;
        }

        private TimeSpan GetDuration()
        {
            var pvlm = PreviewViewmodel?.LoadedMedia;
            if (pvlm != null && pvlm.MediaGuid == SelectedMedia?.MediaGuid)
            {
                var s = PreviewViewmodel.SelectedSegment;
                if (s != null)
                    return s.Duration;
                if (PreviewViewmodel.TcIn != pvlm.TcPlay ||
                    PreviewViewmodel.TcOut != pvlm.TcPlay + pvlm.DurationPlay)
                    return PreviewViewmodel.DurationSelection;
            }
            return SelectedItem?.SelectedSegment?.Duration ?? SelectedItem?.DurationPlay ?? TimeSpan.Zero;
        }

        private string GetMediaName()
        {
            var pvlm = PreviewViewmodel?.LoadedMedia;
            if (pvlm != null)
            {
                var s = PreviewViewmodel.SelectedSegment;
                if (s != null)
                    return pvlm.MediaName + " [" + s.SegmentName + "]";
                if (PreviewViewmodel.TcIn != pvlm.TcPlay ||
                    PreviewViewmodel.TcOut != pvlm.TcPlay + pvlm.DurationPlay)
                    return pvlm.MediaName + " [fragment]";
            }

            if (SelectedItem == null)
                return string.Empty;
            return SelectedItem.SelectedSegment == null ? SelectedItem.MediaName : $"{SelectedItem.MediaName} [{SelectedItem.SelectedSegment.SegmentName}]";
        }

        private bool _allowAdd(object o)
        {
            if (SelectedMedia == null || BaseEvent == null)
                return false;
            var loadedMedia = PreviewViewmodel?.LoadedMedia;
            if (loadedMedia != null && SelectedMedia.MediaGuid != loadedMedia.MediaGuid)
                return false;
            switch (NewEventStartType)
            {
                case TStartType.WithParent:
                case TStartType.WithParentFromEnd:
                    switch (BaseEvent.EventType)
                    {
                        case TEventType.Movie:
                        case TEventType.Live:
                            return BaseEvent.PlayState == TPlayState.Scheduled || (_mediaType == TMediaType.Still && BaseEvent.PlayState == TPlayState.Playing);
                        case TEventType.Rundown:
                            return BaseEvent.PlayState == TPlayState.Scheduled && BaseEvent.SubEventsCount == 0;
                        default:
                            return false;
                    }
                case TStartType.After:
                    switch (BaseEvent.EventType)
                    {
                        case TEventType.Live:
                        case TEventType.Movie:
                            return BaseEvent.PlayState != TPlayState.Played;
                        case TEventType.Rundown:
                            var vp = BaseEvent.GetVisualParent();
                            return BaseEvent.PlayState != TPlayState.Played && vp != null &&
                                   vp.EventType != TEventType.Container;
                        default:
                            return false;
                    }
                default:
                    return false;
            }
        }

        private void _onBaseEventPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IEvent.PlayState))
                InvalidateRequerySuggested();
        }

        private void _onPreviewViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PreviewViewmodel.LoadedMedia))
                InvalidateRequerySuggested();
        }

        private void _add(object param)
        {
            var sm = SelectedItem;
            var handler = MediaChoosen;
            if (handler != null && sm != null)
                handler(this, new MediaSearchEventArgs(sm.Media, sm.SelectedSegment?.MediaSegment, GetMediaName(), GetTCStart(), GetDuration()));
        }

        protected override void OnDispose()
        {
            BaseEvent = null;
            if (PreviewViewmodel != null)
            {
                PreviewViewmodel.PropertyChanged -= _onPreviewViewModelPropertyChanged;
                PreviewViewmodel.Dispose();
            }
            if (_searchDirectory != null)
            {
                _searchDirectory.MediaAdded -= _searchDirectory_MediaAdded;
                _searchDirectory.MediaRemoved -= _searchDirectory_MediaRemoved;
                _searchDirectory.MediaVerified -= _searchDirectory_MediaVerified;
            }
            foreach (var item in Items)
                item.Dispose();
            Debug.WriteLine("MediaSearchViewModel disposed");
        }

        private void SortByName()
        {
            _itemsView.SortDescriptions.Clear();
            _itemsView.SortDescriptions.Add(new SortDescription(nameof(MediaViewViewmodel.MediaName), ListSortDirection.Ascending));
        }

        private void SortByIngestDate()
        {
            _itemsView.SortDescriptions.Clear();
            _itemsView.SortDescriptions.Add(new SortDescription(nameof(MediaViewViewmodel.LastUpdated), ListSortDirection.Descending));
        }

    }

    public class MediaSearchEventArgs : EventArgs
    {
        public MediaSearchEventArgs(IMedia media, IMediaSegment segment, string mediaName, TimeSpan tCIn, TimeSpan duration)
        {
            Media = media;
            MediaSegment = segment;
            MediaName = mediaName;
            TCIn = tCIn;
            Duration = duration;
        }
        public IMedia Media { get; }
        public IMediaSegment MediaSegment {get; }
        public string MediaName { get; }
        public TimeSpan TCIn { get; }
        public TimeSpan Duration { get; }
    }
}
