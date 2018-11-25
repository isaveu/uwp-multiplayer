using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Game
{
    public static class Game
    {
        private static string logDate = null;
        private static int state = 0;
        private static bool transition = false;
        private static bool loading = false;

        private static int remoteState = 0;

        public static int State
        {
            get
            {
                return state;
            }

            set
            {
                Log("State (" + value + ")");
                state = value;
            }
        }

        public static bool Transition {
            get
            {
                return transition;
            }

            set
            {
                Log("Transition (" + value + ")");
                transition = value;
            }
        }

        public static bool Loading
        {
            get
            {
                return loading;
            }

            set
            {
                Log("Loading (" + value + ")");
                loading = value;
            }
        }

        public static int RemoteState
        {
            get
            {
                return remoteState;
            }

            set
            {
                Log("Remote State (" + value + ")");
                remoteState = value;
            }
        }

        public static string PlayerId { get; set; }
        public static bool Ready { get; set; }

        private static ReaderWriterLockSlim logReaderWriterLockSlim = new ReaderWriterLockSlim();

        public static async Task RemoveOldLogs()
        {
            try
            {
                await ApplicationData.Current.LocalFolder.CreateFolderAsync("Logs");
            }
            catch (Exception)
            {
                // ALready created
            }

            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFolder logs = await localFolder.GetFolderAsync("Logs");

            var files = await logs.GetFilesAsync();

            foreach (var file in files)
            {
                await file.DeleteAsync();
            }
        }

        public static async void Log(string message)
        {
            Debug.WriteLine(message);
            logReaderWriterLockSlim.EnterWriteLock();

            if (logDate == null)
            {
                await RemoveOldLogs();
                logDate = DateTime.Now.ToString("MM-dd-yyyy_HH-mm-ss");
            }

            try
            {
                using (StreamWriter streamWriter = File.AppendText(Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs/" + logDate + ".txt")))
                {
                    streamWriter.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
                    streamWriter.Flush();
                }
            }
            finally
            {
                logReaderWriterLockSlim.ExitWriteLock();
            }
        }

        // ...
    }
}
