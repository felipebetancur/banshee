using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Mono.Unix;

using Hyena.Data.Sqlite;

using Banshee.Base;
using Banshee.Configuration;
using Banshee.ServiceStack;
using Banshee.Database;
using Banshee.Sources;
using Banshee.Library;
using Banshee.Playlists.Formats;
using Banshee.Collection;

namespace Banshee.Playlist
{
    public class PlaylistImportCanceledException : ApplicationException
    {
        public PlaylistImportCanceledException (string message) : base (message)
        {
        }
        
        public PlaylistImportCanceledException () : base ()
        {
        }
    }
    
    public static class PlaylistFileUtil
    {        
        public static readonly SchemaEntry<string> DefaultExportFormat = new SchemaEntry<string> (
            "player_window", "default_export_format",
            String.Empty,
            "Export Format",
            "The default playlist export format"
        );
        
        private static PlaylistFormatDescription [] export_formats = new PlaylistFormatDescription [] {
            M3uPlaylistFormat.FormatDescription,
            PlsPlaylistFormat.FormatDescription
        };
        
        public static readonly string [] PlaylistExtensions = new string [] {
            M3uPlaylistFormat.FormatDescription.FileExtension,
            PlsPlaylistFormat.FormatDescription.FileExtension
        };
        
        public static PlaylistFormatDescription [] ExportFormats {
            get { return export_formats; }
        }
        
        public static bool IsSourceExportSupported (Source source)
        {
            bool supported = true;
            
            if (source == null || !(source is AbstractPlaylistSource)) {
                supported = false;
            }
            
            return supported;
        }
        
        public static PlaylistFormatDescription GetDefaultExportFormat ()
        {
            PlaylistFormatDescription default_format = null;
            try {
                string exportFormat = DefaultExportFormat.Get ();
                PlaylistFormatDescription [] formats = PlaylistFileUtil.ExportFormats;
                foreach (PlaylistFormatDescription format in formats) {
                    if (format.FileExtension.Equals (exportFormat)) {
                        default_format = format;
                        break;
                    }
                }
            } catch {            
                // Ignore errors, return our default if we encounter an error.                
            } finally {
                if (default_format == null) {                    
                    default_format = M3uPlaylistFormat.FormatDescription;
                }
            }
            return default_format;
        }
        
        public static void SetDefaultExportFormat (PlaylistFormatDescription format)
        {
            try {
                DefaultExportFormat.Set (format.FileExtension);        
            } catch (Exception) {
            }            
        }
        
        public static int GetFormatIndex (PlaylistFormatDescription [] formats, PlaylistFormatDescription playlist) 
        {
            int default_export_index = -1;
            foreach (PlaylistFormatDescription format in formats) {
                default_export_index++;
                if (format.FileExtension.Equals (playlist.FileExtension)) {                    
                    break;
                }
            }
            return default_export_index;
        }
        
        public static string [] ImportPlaylist (string playlistUri)
        {            
            PlaylistFormatDescription [] formats = PlaylistFileUtil.ExportFormats;            
            
            // If the file has an extenstion, rearrange the format array so that the 
            // appropriate format is tried first.
            if (System.IO.Path.HasExtension (playlistUri)) {
                string extension = System.IO.Path.GetExtension (playlistUri);
                extension = extension.ToLower ();
                
                int index = -1;
                foreach (PlaylistFormatDescription format in formats) {
                    index++;                    
                    if (extension.Equals ("." + format.FileExtension)) {                        
                        break;
                    } 
                }
                                
                if (index != -1 && index != 0 && index < formats.Length) {
                    // Move to first position in array.
                    PlaylistFormatDescription preferredFormat = formats[index];
                    formats[index] = formats[0];
                    formats[0] = preferredFormat;
                }
            }
            
            List<string> uris = new List<string> ();
                
            foreach (PlaylistFormatDescription format in formats) {
                try {
                    IPlaylistFormat playlist = (IPlaylistFormat)Activator.CreateInstance (format.Type);
                    playlist.Load (Banshee.IO.File.OpenRead (new SafeUri (playlistUri)), true);
                    foreach (Dictionary<string, object> element in playlist.Elements) {
                        uris.Add (((Uri)element["uri"]).AbsoluteUri);
                    }
                    break;
                } catch (InvalidPlaylistException) {                    
                    continue;
                }      
            }
        
            return uris.ToArray ();
        }
        
        public static void ImportPlaylistToLibrary (string path)
        {
            try {
                SafeUri uri = new SafeUri (path);
                PlaylistParser parser = new PlaylistParser ();
                string relative_dir = System.IO.Path.GetDirectoryName (uri.LocalPath);
                if (relative_dir[relative_dir.Length - 1] != System.IO.Path.DirectorySeparatorChar) {
                    relative_dir = relative_dir + System.IO.Path.DirectorySeparatorChar;
                }
                parser.BaseUri = new Uri (relative_dir);
                if (parser.Parse (uri)) {
                    List<string> uris = new List<string> ();
                    foreach (Dictionary<string, object> element in parser.Elements) {
                        uris.Add (((Uri)element["uri"]).LocalPath);
                    }
                    
                    ImportPlaylistWorker worker = new ImportPlaylistWorker (System.IO.Path.GetFileNameWithoutExtension (uri.LocalPath), uris.ToArray ());
                    worker.Import ();
                }
            } catch (Exception e) {
                Hyena.Log.Exception (e);
            }
        }
    }
    
    public class ImportPlaylistWorker
    {
        private string [] uris;
        private string name;
        private LibraryImportManager importer;
        
        public ImportPlaylistWorker (string name, string [] uris)
        {
            this.name = name;
            this.uris = uris;
        }
        
        public void Import ()
        {
            try {
                importer = new LibraryImportManager ();
                importer.ImportFinished += CreatePlaylist;
                importer.Enqueue (uris);
            } catch (PlaylistImportCanceledException e) {
                Hyena.Log.Exception (e);
            }
        }
        
        private void CreatePlaylist (object o, EventArgs args)
        {
            try {
                PlaylistSource playlist = new PlaylistSource (name, ServiceManager.SourceManager.MusicLibrary.DbId);
                playlist.Save ();
                ServiceManager.SourceManager.MusicLibrary.AddChildSource (playlist);

                HyenaSqliteCommand insert_command = new HyenaSqliteCommand (String.Format (
                    @"INSERT INTO CorePlaylistEntries (PlaylistID, TrackID) VALUES ({0}, ?)", playlist.DbId));

                //ServiceManager.DbConnection.BeginTransaction ();
                foreach (string uri in uris) {
                    int track_id = LibrarySource.GetTrackIdForUri (uri);
                    if (track_id > 0) {
                        ServiceManager.DbConnection.Execute (insert_command, track_id);
                    }
                }
                
                playlist.Reload ();
                playlist.NotifyUser ();
            } catch (Exception e) {
                Hyena.Log.Exception (e);
            }
        }
    }
}
