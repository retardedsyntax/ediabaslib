// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DownloaderService.cs" company="Matthew Leibowitz">
//   Copyright (c) Matthew Leibowitz
//   This code is licensed under the Apache 2.0 License
//   http://www.apache.org/licenses/LICENSE-2.0.html
// </copyright>
// <summary>
//   The downloader service.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Android.Content.PM;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;
using Android.Telephony;
using Android.Util;
using Java.Util;

using Google.Android.Vending.Expansion.Downloader;

namespace BmwDeepObd
{
    /// <summary>
    /// The downloader service.
    /// </summary>
    public abstract class CustomDownloaderService : CustomIntentService, IDownloaderService
    {
        #region Constants
        public const string Tag = "DownloaderService";
        
        /// <summary>
        /// The buffer size used to stream the data.
        /// </summary>
        public const int BufferSize = 4096;

        /// <summary>
        /// The default user agent used for downloads.
        /// </summary>
        public const string DefaultUserAgent = "Android.LVLDM";

        /// <summary>
        /// The maximum number of redirects. (can't be more than 7)
        /// </summary>
        public const int MaxRedirects = 5;

        /// <summary>
        /// The number of times that the download manager will retry its network
        /// operations when no progress is happening before it gives up.
        /// </summary>
        public const int MaximumRetries = 5;

        /// <summary>
        /// The minimum amount of progress that has to be done before the 
        /// progress bar gets updated.
        /// </summary>
        public const int MinimumProgressStep = 4096;

        /// <summary>
        /// The minimum amount of time that has to elapse before the progress 
        /// bar gets updated, in milliseconds.
        /// </summary>
        public const long MinimumProgressTime = 1000;

        /// <summary>
        /// The minimum amount of time that the download manager accepts for
        /// a Retry-After response header with a parameter in delta-seconds.
        /// </summary>
        public const int MinimumRetryAfter = 30;

        /// <summary>
        /// The time between a failure and the first retry after an IOException.
        /// Each subsequent retry grows exponentially, doubling each time.
        /// The time is in seconds.
        /// </summary>
        public const int RetryFirstDelay = 30;

        /// <summary>
        /// The wake duration to check to see if a download is possible. (seconds)
        /// </summary>
        public const int WatchdogWakeTimer = 60;

        /// <summary>
        /// The wake duration to check to see if the process was killed. (seconds)
        /// </summary>
        private const int ActiveThreadWatchdog = 5;

        /// <summary>
        /// When a number has to be appended to the filename, this string is
        /// used to separate the base filename from the sequence number.
        /// </summary>
        private const string FilenameSequenceSeparator = "-";

        /// <summary>
        /// The maximum number of rows in the database (FIFO).
        /// </summary>
        private const int MaximumDownloads = 1000;

        /// <summary>
        /// Service thread status
        /// </summary>
        private const float SmoothingFactor = 0.005f;

        /// <summary>
        /// The temporary file extension.
        /// </summary>
        private const string TemporaryFileExtension = ".tmp";

        #endregion

        #region Static Fields

        /// <summary>
        /// The maximum amount of time that the download manager accepts for a 
        /// Retry-After response header with a parameter in delta-seconds.
        /// </summary>
        public static readonly int MaxRetryAfter = (int)TimeSpan.FromDays(1).TotalSeconds;

        /// <summary>
        /// Service thread status
        /// </summary>
        private static volatile bool isRunning;

        #endregion

        #region Fields

        /// <summary>
        /// The locker.
        /// </summary>
        private readonly object locker = new object();

        /// <summary>
        /// Our binding to the network state broadcasts
        /// </summary>
        private readonly IDownloaderServiceConnection serviceConnection;

        /// <summary>
        /// Our binding to the network state broadcasts
        /// </summary>
        private readonly Messenger serviceMessenger;

        /// <summary>
        /// Our binding to the network state broadcasts
        /// </summary>
        private Android.App.PendingIntent alarmIntent;

        /// <summary>
        /// Used for calculating time remaining and speed
        /// </summary>
        private float averageDownloadSpeed;

        /// <summary>
        /// Used for calculating time remaining and speed
        /// </summary>
        private long bytesAtSample;

        /// <summary>
        /// Our binding to the network state broadcasts
        /// </summary>
        private Messenger clientMessenger;

        /// <summary>
        /// Our binding to the network state broadcasts
        /// </summary>
        private Android.Content.BroadcastReceiver connectionReceiver;

        /// <summary>
        /// Bindings to important services
        /// </summary>
        private ConnectivityManager connectivityManager;

        /// <summary>
        /// Our binding to the network state broadcasts
        /// </summary>
        private DownloadNotification downloadNotification;

        /// <summary>
        /// Byte counts
        /// </summary>
        private int fileCount;

        /// <summary>
        /// Used for calculating time remaining and speed
        /// </summary>
        private long millisecondsAtSample;

        /// <summary>
        /// The current network state.
        /// </summary>
        private NetworkState networkState;

        /// <summary>
        /// Our binding to the network state broadcasts
        /// </summary>
        private Android.App.PendingIntent pPendingIntent;

        /// <summary>
        /// Package we are downloading for (defaults to package of application)
        /// </summary>
        private PackageInfo packageInfo;

        /// <summary>
        /// Network state.
        /// </summary>
        private bool stateChanged;

        /// <summary>
        /// The status.
        /// </summary>
        private DownloaderServiceStatus status;

        /// <summary>
        /// Bindings to important services
        /// </summary>
        private WifiManager wifiManager;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="DownloaderService"/> class.
        /// </summary>
        protected CustomDownloaderService()
            : base("LVLDownloadService")
        {
            Log.Debug(Tag,"LVLDL DownloaderService()");

            this.serviceConnection = DownloaderServiceMarshaller.CreateStub(this);
            this.serviceMessenger = this.serviceConnection.GetMessenger();

            this.Control = DownloaderServiceControlAction.Run;
        }

        #endregion

        #region Enums

        /// <summary>
        /// The network state.
        /// </summary>
        [Flags]
        protected enum NetworkState
        {
            /// <summary>
            /// The disconnected.
            /// </summary>
            Disconnected = 0, 

            /// <summary>
            /// The connected.
            /// </summary>
            Connected = 1, 

            /// <summary>
            /// The roaming.
            /// </summary>
            Roaming = 2, 

            /// <summary>
            /// The is 3 g.
            /// </summary>
            Is3G = 4, 

            /// <summary>
            /// The is 4 g.
            /// </summary>
            Is4G = 8, 

            /// <summary>
            /// The is cellular.
            /// </summary>
            IsCellular = 16, 

            /// <summary>
            /// The is fail over.
            /// </summary>
            IsFailOver = 32
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the number of bytes downloaded so far
        /// </summary>
        public long BytesSoFar { get; private set; }

        /// <summary>
        /// Gets the cntrol action.
        /// </summary>
        public DownloaderServiceControlAction Control { get; private set; }

        /// <summary>
        /// Gets or sets the download state
        /// </summary>
        public DownloaderServiceStatus Status
        {
            get
            {
                return this.status;
            }

            set
            {
                this.status = value;

                DownloadsDB db = DownloadsDB.GetDB(this);
                if (db != null)
                {
                    db.UpdateMetadata(db.LastCheckedVersionCode, this.status);
                }
            }
        }

        /// <summary>
        /// Gets the total length of the downloads.
        /// </summary>
        public long TotalLength { get; private set; }

        #endregion

        #region Properties

        /// <summary>
        /// Gets AlarmReceiverClassName.
        /// </summary>
        public abstract string AlarmReceiverClassName { get; }

        /// <summary>
        /// Gets PublicKey.
        /// </summary>
        public abstract string PublicKey { get; }

        /// <summary>
        /// Gets Salt.
        /// </summary>
        public abstract byte[] GetSalt();

        /// <summary>
        /// Gets or sets a value indicating whether the service is running.
        /// Note: Only use this internally.
        /// </summary>
        private bool IsServiceRunning
        {
            get
            {
                lock (this.locker)
                {
                    return isRunning;
                }
            }

            set
            {
                lock (this.locker)
                {
                    isRunning = value;
                }
            }
        }

        private static bool IsLvlCheckRequired(DownloadsDB db, PackageInfo pi)
        {
            // we need to update the LVL check and get a successful status to
            // proceed
            if (db.LastCheckedVersionCode != pi.VersionCode)
            {
                return true;
            }
            return false;
        }
        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// This version assumes that the intent contains the pending intent as
        /// a parameter. This is used for responding to alarms.
        /// The pending intent must be in an extra with the key 
        /// <see cref="DownloaderService#PendingIntent"/>.
        /// </summary>
        /// <param name="context">
        /// Your application Context.
        /// </param>
        /// <param name="intent">
        /// An Intent to start the Activity in your application that
        /// shows the download progress and which will also start the 
        /// application when downloadcompletes.
        /// </param>
        /// <param name="serviceType">
        /// The type of the service to start.
        /// </param>
        /// <returns>
        /// Whether the service was started and the reason for starting the 
        /// service.
        /// Either <see cref="DownloaderServiceRequirement.NoDownloadRequired"/>,
        /// <see cref="DownloaderServiceRequirement.LvlCheckRequired"/>, or 
        /// <see cref="DownloaderServiceRequirement.DownloadRequired"/>
        /// </returns>
        public static DownloaderServiceRequirement StartDownloadServiceIfRequired(
            Android.Content.Context context, Android.Content.Intent intent, Type serviceType)
        {
            var pendingIntent = (Android.App.PendingIntent)intent.GetParcelableExtra(DownloaderServiceExtras.PendingIntent);
            return StartDownloadServiceIfRequired(context, pendingIntent, serviceType);
        }

        /// <summary>
        /// Starts the download if necessary. 
        /// </summary>
        /// <remarks>
        /// This function starts a flow that 
        /// does many things:
        ///   1) Checks to see if the APK version has been checked and the 
        ///      metadata database updated 
        ///   2) If the APK version does not match, checks the new LVL status 
        ///      to see if a new download is required 
        ///   3) If the APK version does match, then checks to see if the 
        ///      download(s) have been completed
        ///   4) If the downloads have been completed, returns 
        ///      <see cref="DownloaderServiceRequirement.NoDownloadRequired"/> 
        /// The idea is that this can be called during the startup of an 
        /// application to quickly ascertain if the application needs to wait 
        /// to hear about any updated APK expansion files. 
        /// This does mean that the application MUST be run with a network 
        /// connection for the first time, even if Market delivers all of the 
        /// files.
        /// </remarks>
        /// <param name="context">
        /// Your application Context.
        /// </param>
        /// <param name="pendingIntent">
        /// A PendingIntent to start the Activity in your application that
        /// shows the download progress and which will also start the 
        /// application when downloadcompletes.
        /// </param>
        /// <param name="serviceType">
        /// The class of your <see cref="DownloaderService"/> implementation.
        /// </param>
        /// <returns>
        /// Whether the service was started and the reason for starting the 
        /// service.
        /// Either <see cref="DownloaderServiceRequirement.NoDownloadRequired"/>,
        /// <see cref="DownloaderServiceRequirement.LvlCheckRequired"/>, or 
        /// <see cref="DownloaderServiceRequirement.DownloadRequired"/>
        /// </returns>
        public static DownloaderServiceRequirement StartDownloadServiceIfRequired(
            Android.Content.Context context, Android.App.PendingIntent pendingIntent, Type serviceType)
        {
            // first: do we need to do an LVL update?
            // we begin by getting our APK version from the package manager
            PackageInfo pi = context.PackageManager.GetPackageInfo(context.PackageName, 0);

            DownloaderServiceRequirement status = DownloaderServiceRequirement.NoDownloadRequired;

            DownloadsDB db = DownloadsDB.GetDB(context);

            // we need to update the LVL check and get a successful status to proceed
            if (IsLvlCheckRequired(db, pi))
            {
                status = DownloaderServiceRequirement.LvlCheckRequired;
            }

            // we don't have to update LVL. Do we still have a download to start?
            if (!db.IsDownloadRequired)
            {
                DownloadInfo[] infos = db.GetDownloads();
                if (infos != null)
                {
                    foreach (DownloadInfo info in infos)
                    {
                        if (!Helpers.DoesFileExist(context, info.FileName, info.TotalBytes, true))
                        {
                            status = DownloaderServiceRequirement.DownloadRequired;
                            db.UpdateStatus(DownloaderServiceFlags.None);
                            break;
                        }
                    }
                }
            }
            else
            {
                status = DownloaderServiceRequirement.DownloadRequired;
            }

            switch (status)
            {
                case DownloaderServiceRequirement.DownloadRequired:
                case DownloaderServiceRequirement.LvlCheckRequired:
                    var fileIntent = new Android.Content.Intent(context.ApplicationContext, serviceType);
                    fileIntent.PutExtra(DownloaderServiceExtras.PendingIntent, pendingIntent);
                    context.StartService(fileIntent);
                    break;
            }

            return status;
        }

        /// <summary>
        /// Creates a filename (where the file should be saved) from info about a download.
        /// </summary>
        /// <param name="filename">
        /// The filename.
        /// </param>
        /// <param name="filesize">
        /// The filesize.
        /// </param>
        /// <returns>
        /// The generate save file.
        /// </returns>
        public string GenerateSaveFile(string filename, long filesize)
        {
            string path = this.GenerateTempSaveFileName(filename);

            if (!Helpers.IsExternalMediaMounted)
            {
                
                Log.Debug(Tag,"External media not mounted: {0}", path);

                throw new Google.Android.Vending.Expansion.Downloader.DownloaderService.GenerateSaveFileError((int) DownloaderServiceStatus.DeviceNotFound, "external media is not yet mounted");
            }

            if (File.Exists(path))
            {
                Log.Debug(Tag,"File already exists: {0}", path);

                throw new Google.Android.Vending.Expansion.Downloader.DownloaderService.GenerateSaveFileError((int) DownloaderServiceStatus.FileAlreadyExists, "requested destination file already exists");
            }

            if (Helpers.GetAvailableBytes(Helpers.GetFilesystemRoot(path)) < filesize)
            {
                throw new Google.Android.Vending.Expansion.Downloader.DownloaderService.GenerateSaveFileError((int) DownloaderServiceStatus.InsufficientSpace, "insufficient space on external storage");
            }

            return path;
        }

        /// <summary>
        /// Returns the filename (where the file should be saved) from info about a download
        /// </summary>
        /// <param name="fileName">
        /// The file Name.
        /// </param>
        /// <returns>
        /// The generate temp save file name.
        /// </returns>
        public string GenerateTempSaveFileName(string fileName)
        {
            return string.Format(
                "{0}{1}{2}{3}", 
                Helpers.GetSaveFilePath(this), 
                Path.DirectorySeparatorChar, 
                fileName, 
                TemporaryFileExtension);
        }

        /// <summary>
        /// a non-localized string appropriate for logging corresponding to one of the NETWORK_* constants.
        /// </summary>
        /// <param name="networkError">
        /// The network Error.
        /// </param>
        /// <returns>
        /// The get log message for network error.
        /// </returns>
        public string GetLogMessageForNetworkError(DownloaderServiceNetworkAvailability networkError)
        {
            switch (networkError)
            {
                case DownloaderServiceNetworkAvailability.RecommendedUnusableDueToSize:
                    return "download size exceeds recommended limit for mobile network";

                case DownloaderServiceNetworkAvailability.UnusableDueToSize:
                    return "download size exceeds limit for mobile network";

                case DownloaderServiceNetworkAvailability.NoConnection:
                    return "no network connection available";

                case DownloaderServiceNetworkAvailability.CannotUseRoaming:
                    return "download cannot use the current network connection because it is roaming";

                case DownloaderServiceNetworkAvailability.TypeDisallowedByRequestor:
                    return "download was requested to not use the current network type";

                default:
                    return "unknown error with network connectivity";
            }
        }

        /// <summary>
        /// Calculating a moving average for the speed so we don't get jumpy calculations for time etc.
        /// </summary>
        /// <param name="totalBytesSoFar">
        /// The total Bytes So Far.
        /// </param>
        public void NotifyUpdateBytes(long totalBytesSoFar)
        {
            long timeRemaining;
            long currentTime = SystemClock.UptimeMillis();
            if (0 != this.millisecondsAtSample)
            {
                // we have a sample.
                long timePassed = currentTime - this.millisecondsAtSample;
                long bytesInSample = totalBytesSoFar - this.bytesAtSample;
                float currentSpeedSample = bytesInSample / (float)timePassed;
                if (Math.Abs(0 - this.averageDownloadSpeed) > SmoothingFactor)
                {
                    float smoothSpeed = SmoothingFactor * currentSpeedSample;
                    float averageSpeed = (1 - SmoothingFactor) * this.averageDownloadSpeed;
                    this.averageDownloadSpeed = smoothSpeed + averageSpeed;
                }
                else
                {
                    this.averageDownloadSpeed = currentSpeedSample;
                }

                timeRemaining = (long)((this.TotalLength - totalBytesSoFar) / this.averageDownloadSpeed);
            }
            else
            {
                timeRemaining = -1;
            }

            this.millisecondsAtSample = currentTime;
            this.bytesAtSample = totalBytesSoFar;
            this.downloadNotification.OnDownloadProgress(
                new DownloadProgressInfo(this.TotalLength, totalBytesSoFar, timeRemaining, this.averageDownloadSpeed));
        }

        /// <summary>
        /// The on bind.
        /// </summary>
        /// <param name="intent">
        /// The intent.
        /// </param>
        /// <returns>
        /// the binder
        /// </returns>
        public override IBinder OnBind(Android.Content.Intent intent)
        {
            return this.serviceMessenger.Binder;
        }

        /// <summary>
        /// The on client updated.
        /// </summary>
        /// <param name="messenger">
        /// The client messenger.
        /// </param>
        public void OnClientUpdated(Messenger messenger)
        {
            this.clientMessenger = messenger;
            this.downloadNotification.SetMessenger(this.clientMessenger);
        }

        /// <summary>
        /// The on create.
        /// </summary>
        public override void OnCreate()
        {
            base.OnCreate();
            try
            {
                this.packageInfo = this.PackageManager.GetPackageInfo(this.PackageName, 0);
                string applicationLabel = this.PackageManager.GetApplicationLabel(this.ApplicationInfo);
                Android.Content.Context context = this;
                Java.Lang.ICharSequence label = new Java.Lang.String(applicationLabel);
                IntPtr downloaderNotification = Android.Runtime.JNIEnv.CreateInstance(typeof(DownloadNotification),
                    "(Landroid/content/Context;Ljava/lang/CharSequence)V", new Android.Runtime.JValue[] {new (context), new (label) });
                if (downloaderNotification != IntPtr.Zero)
                {
                    Java.Lang.Object handle = new Java.Lang.Object(downloaderNotification, Android.Runtime.JniHandleOwnership.TransferGlobalRef);
                    Android.Runtime.JNIEnv.GetObjectClass(handle.Handle);
                }
                //this.downloadNotification = new DownloadNotification(this, applicationLabel);
            }
            catch (Exception e)
            {
                Log.Error(Tag, "OnCreate Exception: {0}", e.Message);
            }
        }

        /// <summary>
        /// The on destroy.
        /// </summary>
        public override void OnDestroy()
        {
            if (this.connectionReceiver != null)
            {
                this.UnregisterReceiver(this.connectionReceiver);
                this.connectionReceiver = null;
            }

            this.serviceConnection.Disconnect(this);
            base.OnDestroy();
        }

        /// <summary>
        /// The request abort download.
        /// </summary>
        public void RequestAbortDownload()
        {
            this.Control = DownloaderServiceControlAction.Paused;
            this.Status = DownloaderServiceStatus.Canceled;
        }

        /// <summary>
        /// The request continue download.
        /// </summary>
        public void RequestContinueDownload()
        {
            Log.Debug(Tag,"RequestContinueDownload");

            if (this.Control == DownloaderServiceControlAction.Paused)
            {
                this.Control = DownloaderServiceControlAction.Run;
            }

            var fileIntent = new Android.Content.Intent(this, this.GetType());
            fileIntent.PutExtra(DownloaderServiceExtras.PendingIntent, this.pPendingIntent);
            this.StartService(fileIntent);
        }

        /// <summary>
        /// The request download status.
        /// </summary>
        public void RequestDownloadStatus()
        {
            this.downloadNotification.ResendState();
        }

        /// <summary>
        /// The request pause download.
        /// </summary>
        public void RequestPauseDownload()
        {
            this.Control = DownloaderServiceControlAction.Paused;
            this.Status = DownloaderServiceStatus.PausedByApp;
        }

        /// <summary>
        /// The set download flags.
        /// </summary>
        /// <param name="flags">
        /// The flags.
        /// </param>
        public void SetDownloadFlags(DownloaderServiceFlags flags)
        {
            DownloadsDB db = DownloadsDB.GetDB(this);
            if (db != null)
            {
                db.UpdateFlags(flags);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// The get network availability state.
        /// </summary>
        /// <returns>
        /// The ExpansionDownloader.Service.NetworkDisabledState.
        /// </returns>
        internal DownloaderServiceNetworkAvailability GetNetworkAvailabilityState()
        {
            if (!this.networkState.HasFlag(NetworkState.Connected))
            {
                return DownloaderServiceNetworkAvailability.NoConnection;
            }

            if (!this.networkState.HasFlag(NetworkState.IsCellular))
            {
                return DownloaderServiceNetworkAvailability.Ok;
            }

            if (this.networkState.HasFlag(NetworkState.Roaming))
            {
                return DownloaderServiceNetworkAvailability.CannotUseRoaming;
            }

            DownloadsDB db = DownloadsDB.GetDB(this);
            DownloaderServiceFlags flags = DownloaderServiceFlags.None;
            if (db != null)
            {
                flags = db.Flags;
            }

            if (flags.HasFlag(DownloaderServiceFlags.DownloadOverCellular))
            {
                return DownloaderServiceNetworkAvailability.Ok;
            }

            return DownloaderServiceNetworkAvailability.TypeDisallowedByRequestor;
        }

        /// <summary>
        /// Updates the network type based upon the info returned from the 
        /// connectivity manager. 
        /// </summary>
        /// <param name="info">
        /// </param>
        /// <returns>
        /// The ExpansionDownloader.Service.DownloaderService+NetworkState.
        /// </returns>
        private NetworkState GetNetworkState(NetworkInfo info)
        {
            var state = NetworkState.Disconnected;

            switch (info.Type)
            {
                case ConnectivityType.Wifi:
#if __ANDROID_13__
                case ConnectivityType.Ethernet:
                case ConnectivityType.Bluetooth:
#endif
		            break;
                case ConnectivityType.Wimax:
                    state = NetworkState.Is3G | NetworkState.Is4G | NetworkState.IsCellular;
                    break;

                case ConnectivityType.Mobile:
                    state = NetworkState.IsCellular;
                    switch ((NetworkType)info.Subtype)
                    {
                        case NetworkType.OneXrtt:
                        case NetworkType.Cdma:
                        case NetworkType.Edge:
                        case NetworkType.Gprs:
                        case NetworkType.Iden:
                            break;
                        case NetworkType.Hsdpa:
                        case NetworkType.Hsupa:
                        case NetworkType.Hspa:
                        case NetworkType.Evdo0:
                        case NetworkType.EvdoA:
                        case NetworkType.Umts:
                            state |= NetworkState.Is3G;
                            break;
#if __ANDROID_11__
                        case NetworkType.Lte:
                        case NetworkType.Ehrpd:
							state |= NetworkState.Is3G | NetworkState.Is4G;
                            break;
#endif
#if __ANDROID_13__
                        case NetworkType.Hspap:
							state |= NetworkState.Is3G | NetworkState.Is4G;
                            break;
#endif
                    }

                    break;
            }

            return state;
        }

        /// <summary>
        /// This is the main thread for the Downloader. 
        /// This thread is responsible for queuing up downloads and other goodness.
        /// </summary>
        /// <param name="intent">
        /// The intent that was recieved.
        /// </param>
        protected override void OnHandleIntent(Android.Content.Intent intent)
        {
            Log.Debug(Tag,"DownloaderService.OnHandleIntent");

            this.IsServiceRunning = true;
            try
            {
                DownloadsDB db = DownloadsDB.GetDB(this);

                var pendingIntent = (Android.App.PendingIntent)intent.GetParcelableExtra(DownloaderServiceExtras.PendingIntent);

                if (null != pendingIntent)
                {
                    this.downloadNotification.ClientIntent = pendingIntent;
                    this.pPendingIntent = pendingIntent;
                }
                else if (null != this.pPendingIntent)
                {
                    this.downloadNotification.ClientIntent = this.pPendingIntent;
                }
                else
                {
                    Log.Debug(Tag,"LVLDL Downloader started in bad state without notification intent.");
                    return;
                }

                // when the LVL check completes, a successful response will update the service
                if (IsLvlCheckRequired(db, this.packageInfo))
                {
                    this.UpdateLvl(this);
                    return;
                }

                // get each download
                DownloadInfo[] infos = db.GetDownloads();
                this.BytesSoFar = 0;
                this.TotalLength = 0;
                this.fileCount = infos.Count();
                foreach (DownloadInfo info in infos)
                {
                    // We do an (simple) integrity check on each file, just to 
                    // make sure and to verify that the file matches the state
                    if ((DownloaderServiceStatus) info.Status == DownloaderServiceStatus.Success
                         && !Helpers.DoesFileExist(this, info.FileName, info.TotalBytes, true))
                    {
                        info.Status = 0;
                        info.CurrentBytes = 0;
                    }

                    // get aggregate data
                    this.TotalLength += info.TotalBytes;
                    this.BytesSoFar += info.CurrentBytes;
                }

                this.PollNetworkState();
                if (this.connectionReceiver == null)
                {
                    // We use this to track network state, such as when WiFi, Cellular, etc. is enabled
                    // when downloads are paused or in progress.
                    this.connectionReceiver = new InnerBroadcastReceiver(this);
                    var intentFilter = new Android.Content.IntentFilter(ConnectivityManager.ConnectivityAction);
                    intentFilter.AddAction(WifiManager.WifiStateChangedAction);
                    this.RegisterReceiver(this.connectionReceiver, intentFilter);
                }

                // loop through all downloads and fetch them
                foreach (DownloadInfo info in infos)
                {
                    Log.Debug(Tag,"Starting download of " + info.FileName);

                    long startingCount = info.CurrentBytes;

                    if ((DownloaderServiceStatus) info.Status != DownloaderServiceStatus.Success)
                    {
                        // ToDo: Fix This
                        //var dt = new DownloadThread(info, this, this.downloadNotification);
                        this.CancelAlarms();
                        this.ScheduleAlarm(ActiveThreadWatchdog);
                        //dt.Run();
                        this.CancelAlarms();
                    }

                    db.UpdateFromDb(info);
                    bool setWakeWatchdog = false;
                    DownloaderClientState notifyStatus;
                    switch ((DownloaderServiceStatus) info.Status)
                    {
                        case DownloaderServiceStatus.Forbidden:
                            // the URL is out of date
                            UpdateLvl(this);
                            return;
                        case DownloaderServiceStatus.Success:
                            this.BytesSoFar += info.CurrentBytes - startingCount;
                            db.UpdateMetadata(packageInfo.VersionCode, 0);
                            this.downloadNotification.OnDownloadStateChanged(DownloaderClientState.Completed);
                            continue;
                        case DownloaderServiceStatus.FileDeliveredIncorrectly:

                            // we may be on a network that is returning us a web page on redirect
                            notifyStatus = DownloaderClientState.PausedNetworkSetupFailure;
                            info.CurrentBytes = 0;
                            db.UpdateDownload(info);
                            setWakeWatchdog = true;
                            break;
                        case DownloaderServiceStatus.PausedByApp:
                            notifyStatus = DownloaderClientState.PausedByRequest;
                            break;
                        case DownloaderServiceStatus.WaitingForNetwork:
                        case DownloaderServiceStatus.WaitingToRetry:
                            notifyStatus = DownloaderClientState.PausedNetworkUnavailable;
                            setWakeWatchdog = true;
                            break;
                        case DownloaderServiceStatus.QueuedForWifi:
                        case DownloaderServiceStatus.QueuedForWifiOrCellularPermission:

                            // look for more detail here
                            notifyStatus = this.wifiManager != null && !this.wifiManager.IsWifiEnabled
                                               ? DownloaderClientState.PausedWifiDisabledNeedCellularPermission
                                               : DownloaderClientState.PausedNeedCellularPermission;
                            setWakeWatchdog = true;
                            break;
                        case DownloaderServiceStatus.Canceled:
                            notifyStatus = DownloaderClientState.FailedCanceled;
                            setWakeWatchdog = true;
                            break;

                        case DownloaderServiceStatus.InsufficientSpace:
                            notifyStatus = DownloaderClientState.FailedSdCardFull;
                            setWakeWatchdog = true;
                            break;

                        case DownloaderServiceStatus.DeviceNotFound:
                            notifyStatus = DownloaderClientState.PausedSdCardUnavailable;
                            setWakeWatchdog = true;
                            break;

                        default:
                            notifyStatus = DownloaderClientState.Failed;
                            break;
                    }

                    if (setWakeWatchdog)
                    {
                        this.ScheduleAlarm(WatchdogWakeTimer);
                    }
                    else
                    {
                        this.CancelAlarms();
                    }

                    // failure or pause state
                    this.downloadNotification.OnDownloadStateChanged(notifyStatus);
                    return;
                }

                this.downloadNotification.OnDownloadStateChanged(DownloaderClientState.Completed);
            }
            catch (Exception ex)
            {
                Log.Error(Tag,ex.Message);
                Log.Error(Tag,ex.StackTrace);
            }
            finally
            {
                this.IsServiceRunning = false;
            }
        }

        /// <summary>
        /// Returns a value indicating whether the downloader should stop. 
        /// This will return True if all the downloads are complete.
        /// </summary>
        /// <returns>
        /// True if the downloader should stop.
        /// </returns>
        protected override bool ShouldStop()
        {
            // the database automatically reads the metadata for version code 
            // and download status when the instance is created
            DownloadsDB db = DownloadsDB.GetDB(this);
            DownloaderServiceFlags flags = DownloaderServiceFlags.None;
            if (db != null)
            {
                flags = db.Flags;
            }

            if (flags == 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// The cancel alarms.
        /// </summary>
        private void CancelAlarms()
        {
            if (null != this.alarmIntent)
            {
                Android.App.AlarmManager alarms = GetSystemService(AlarmService) as Android.App.AlarmManager;
                if (alarms == null)
                {
                    Log.Debug(Tag,"LVLDL couldn't get alarm manager");
                    return;
                }

                alarms.Cancel(this.alarmIntent);
                this.alarmIntent = null;
            }
        }

        /// <summary>
        /// The APK has been updated and a filename has been sent down from the
        /// Market call. If the file has the same name as the previous file, we do
        /// nothing as the file is guaranteed to be the same. If the file does not
        /// have the same name, we download it if it hasn't already been delivered by
        /// Market.
        /// </summary>
        /// <param name="filename">
        /// the name of the new file
        /// </param>
        /// <param name="fileSize">
        /// the size of the new file
        /// </param>
        /// <returns>
        /// The handle file updated.
        /// </returns>
        private bool HandleFileUpdated(string filename, long fileSize)
        {
            DownloadsDB db = DownloadsDB.GetDB(this);
            if (db == null)
            {
                return false;
            }

            MethodInfo getDownloadInfo = db.GetType().GetMethod("GetDownloadInfoByFileName", BindingFlags.NonPublic | BindingFlags.Instance);
            if (getDownloadInfo == null)
            {
                return false;
            }

            object[] args = { filename };
            // ReSharper disable once UsePatternMatching
            DownloadInfo di = getDownloadInfo.Invoke(db, args) as DownloadInfo;

            if (di != null && di.FileName != null)
            {
                if (filename == di.FileName)
                {
                    return false;
                }

                // remove partially downloaded file if it is there
                string deleteFile = Helpers.GenerateSaveFileName(this, di.FileName);
                if (File.Exists(deleteFile))
                {
                    File.Delete(deleteFile);
                }
            }

            return !Helpers.DoesFileExist(this, filename, fileSize, true);
        }

        /// <summary>
        /// Polls the network state, setting the flags appropriately.
        /// </summary>
        private void PollNetworkState()
        {
            if (this.connectivityManager == null)
            {
                this.connectivityManager = this.GetSystemService(ConnectivityService) as ConnectivityManager;
            }

            if (this.wifiManager == null)
            {
                this.wifiManager = this.GetSystemService(WifiService) as WifiManager;
            }

            if (this.connectivityManager == null)
            {
                Log.Debug(Tag,"LVLDL couldn't get connectivity manager to poll network state");
            }
            else
            {
                NetworkInfo activeInfo = this.connectivityManager.ActiveNetworkInfo;
                this.UpdateNetworkState(activeInfo);
            }
        }

        /// <summary>
        /// The schedule alarm.
        /// </summary>
        /// <param name="wakeUp">
        /// The wake up.
        /// </param>
        private void ScheduleAlarm(int wakeUp)
        {
            Android.App.AlarmManager alarms = GetSystemService(AlarmService) as Android.App.AlarmManager;
            if (alarms == null)
            {
                Log.Debug(Tag,"LVLDL couldn't get alarm manager");
                return;
            }

            Calendar cal = Calendar.Instance;
            cal.Add(CalendarField.Second, wakeUp);

            Log.Debug(Tag,"LVLDL scheduling retry in {0} seconds ({1})", wakeUp, cal.Time.ToLocaleString());

            var intent = new Android.Content.Intent(DownloaderServiceAction.ActionRetry);
            intent.PutExtra(DownloaderServiceExtras.PendingIntent, this.pPendingIntent);
            intent.SetClassName(this.PackageName, this.AlarmReceiverClassName);
            this.alarmIntent = Android.App.PendingIntent.GetBroadcast(this, 0, intent, Android.App.PendingIntentFlags.OneShot);
            alarms.Set(Android.App.AlarmType.RtcWakeup, cal.TimeInMillis, this.alarmIntent);
        }

        /// <summary>
        /// Updates the LVL information from the server.
        /// </summary>
        /// <param name="context">
        /// </param>
        private void UpdateLvl(CustomDownloaderService context)
        {
            var h = new Handler(context.MainLooper);
            // ToDo: Fix This
            //h.Post(new LVLRunnable(context, this.pPendingIntent));
        }

        /// <summary>
        /// The update network state.
        /// </summary>
        /// <param name="info">
        /// The info.
        /// </param>
        private void UpdateNetworkState(NetworkInfo info)
        {
            NetworkState tempState = this.networkState;

            this.networkState = NetworkState.Disconnected;

            if (info != null && info.IsConnected)
            {
                this.networkState = NetworkState.Connected;

                if (info.IsRoaming)
                {
                    this.networkState |= NetworkState.Roaming;
                }

                if (info.IsFailover)
                {
                    this.networkState |= NetworkState.IsFailOver;
                }

                this.networkState |= this.GetNetworkState(info);
            }

            this.stateChanged = this.stateChanged || this.networkState != tempState;

            if (this.stateChanged)
            {
                Log.Debug(Tag,"LVLDL Network state changed: ");
                Log.Debug(Tag,"LVLDL Starting State: {0}", tempState);
                Log.Debug(Tag,"LVLDL Ending State: {0}", this.networkState);

                if (this.IsServiceRunning)
                {
                    if (this.networkState.HasFlag(NetworkState.Roaming))
                    {
                        this.Status = DownloaderServiceStatus.WaitingForNetwork;
                        this.Control = DownloaderServiceControlAction.Paused;
                    }
                    else if (this.networkState.HasFlag(NetworkState.IsCellular))
                    {
                        DownloadsDB db = DownloadsDB.GetDB(this);
                        if (db != null)
                        {
                            DownloaderServiceFlags flags = db.Flags;
                            if (!flags.HasFlag(DownloaderServiceFlags.DownloadOverCellular))
                            {
                                this.Status = DownloaderServiceStatus.QueuedForWifi;
                                this.Control = DownloaderServiceControlAction.Paused;
                            }
                        }
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// We use this to track network state, such as when WiFi, Cellular, etc. is
        /// enabled when downloads are paused or in progress.
        /// </summary>
        private class InnerBroadcastReceiver : Android.Content.BroadcastReceiver
        {
            #region Fields

            /// <summary>
            /// The m service.
            /// </summary>
            private readonly CustomDownloaderService service;

            #endregion

            #region Constructors and Destructors

            /// <summary>
            /// Initializes a new instance of the <see cref="InnerBroadcastReceiver"/> class.
            /// </summary>
            /// <param name="service">
            /// The service.
            /// </param>
            internal InnerBroadcastReceiver(CustomDownloaderService service)
            {
                this.service = service;
            }

            #endregion

            #region Public Methods and Operators

            /// <summary>
            /// The on receive.
            /// </summary>
            /// <param name="context">
            /// The context.
            /// </param>
            /// <param name="intent">
            /// The intent.
            /// </param>
            public override void OnReceive(Android.Content.Context context, Android.Content.Intent intent)
            {
                this.service.PollNetworkState();
                if (this.service.stateChanged && !this.service.IsServiceRunning)
                {
                    Log.Debug(Tag,"LVLDL InnerBroadcastReceiver Called");
                    var fileIntent = new Android.Content.Intent(context, this.service.GetType());
                    fileIntent.PutExtra(DownloaderServiceExtras.PendingIntent, this.service.pPendingIntent);

                    // send a new intent to the service
                    context.StartService(fileIntent);
                }
            }

            #endregion
        }
    }
}