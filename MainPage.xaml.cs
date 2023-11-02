using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.BulkAccess;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Search;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace FileTest
{
    public class FileInfo
    {
        public FileInformation Info { get; }

        public FileInfo(FileInformation info)
        {
            Info = info;
            Name = info.Name;
            ItemDate = info.BasicProperties.ItemDate;
        }

        public FileInfo(StorageFolder Parent,
        string Name,
        DateTimeOffset ItemDate)
        {
            this.Parent = Parent;
            this.Name = Name;
            this.ItemDate = ItemDate;
        }

        public StorageFolder Parent { get; }
        public string Name { get; }
        public DateTimeOffset ItemDate { get; }

        public IAsyncOperation<StorageFile> GetFileAsync()
        {
            return Parent.GetFileAsync(Name);
        }

        public async Task<StorageItemThumbnail> GetThumbnailAsync(
            ThumbnailMode mode,
            uint requestedSize,
            ThumbnailOptions options,
            CancellationToken token)
        {
            if (Info != null)
                return Info.Thumbnail;

            var file = await GetFileAsync().AsTask(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            return await file.GetThumbnailAsync(mode, requestedSize, options).AsTask(token).ConfigureAwait(false);
        }
    }
    



    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            System.Diagnostics.Stopwatch watch = new ();
            FolderPicker picker = new ();
            if (await picker.PickSingleFolderAsync().AsTask().ConfigureAwait(false) is StorageFolder folder)
            {
                watch.Start();
                List<string> props = new () { "System.ItemDate" };
                QueryOptions ops = new ();

                ops.SetThumbnailPrefetch(ThumbnailMode.SingleItem, 160, ThumbnailOptions.ResizeThumbnail);
                ops.SetPropertyPrefetch(
                    PropertyPrefetchOptions.BasicProperties, props);

                var state = await folder.GetIndexedStateAsync().AsTask().ConfigureAwait(false);
                if (state == IndexedState.NotIndexed || state == IndexedState.Unknown)
                    ops.IndexerOption = IndexerOption.UseIndexerWhenAvailable;
                else
                    ops.IndexerOption = IndexerOption.OnlyUseIndexerAndOptimizeForIndexedProperties;

                var query = folder.CreateFileQueryWithOptions(ops);
                var factory = new FileInformationFactory(query, ThumbnailMode.SingleItem, 160, ThumbnailOptions.ReturnOnlyIfCached, true);
                

                //// Could use obseravble collection and bind items immediately
                //List<FileInfo> infos = new();
                //uint batchSize = 100;
                //uint start = 0;
                ////while (true)
                ////{
                ////    //var files = await query.GetFilesAsync(start, batchSize).AsTask().ConfigureAwait(false);
                ////    //start += batchSize;

                ////    //foreach (var file in files)
                ////    //{
                ////    //    var p = await file.GetBasicPropertiesAsync().AsTask().ConfigureAwait(false);
                ////    //    infos.Add(new FileInfo(folder, file.Name, p.ItemDate));
                ////    //}

                ////    //if (files.Count < batchSize)
                ////    //    break;

                ////    //var datas = await factory.GetFilesAsync(start, batchSize).AsTask().ConfigureAwait(false);
                ////    //start += batchSize;

                ////    //foreach (var data in datas)
                ////    //    infos.Add(new FileInfo(data));

                ////    //if (datas.Count < batchSize)
                ////    //    break;
                ////}

                watch.Stop();

                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    LoadingRing.IsActive = false;
                    Grid.ItemsSource = factory.GetVirtualizedFilesVector();
                });
            }

           
        }

        private void Grid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is GridViewItem item
                && item.ContentTemplateRoot is StorageFileImage img)
                img.StorageFile = null;
        }
    }
}
