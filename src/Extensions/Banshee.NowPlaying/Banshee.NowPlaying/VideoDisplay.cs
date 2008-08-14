//
// VideoDisplay.cs
//
// Author:
//   Aaron Bockover <abockover@novell.com>
//
// Copyright (C) 2008 Novell, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using Gtk;

using Banshee.MediaEngine;
using Banshee.ServiceStack;
using Banshee.Collection;

namespace Banshee.NowPlaying
{   
    public abstract class VideoDisplay : Gtk.Widget
    {
        private Gdk.Pixbuf idle_pixbuf;
        private bool render_idle = true;
        private bool render_video = false;
        private Gdk.Pixbuf last_coverart_pixbuf;
        private string last_coverart_id;

        public VideoDisplay ()
        {
            ServiceManager.PlayerEngine.ConnectEvent (OnPlayerEvent,
                PlayerEvent.StartOfStream |
                PlayerEvent.EndOfStream);
            ToggleIdleVisibility ();
        }

        protected abstract Gdk.Window RenderWindow { get; }
        
        protected abstract void ExposeVideo (Gdk.EventExpose evnt);
        
        protected override void OnDestroyed ()
        {
            base.OnDestroyed ();
            ServiceManager.PlayerEngine.DisconnectEvent (OnPlayerEvent);
        }
        
        protected override bool OnExposeEvent (Gdk.EventExpose evnt)
        {
            if (RenderWindow == null || !RenderWindow.IsVisible) {
                return true;
            }
            
            if (render_video && ServiceManager.PlayerEngine.SupportsVideo) {
                ExposeVideo (evnt);
                return true;
            }

            if (render_idle || !DrawCoverArt ()) {
                DrawIdle ();
            }

            return true;
        }
        
        private void DrawIdle ()
        {
            if (idle_pixbuf == null) {
                idle_pixbuf = Gdk.Pixbuf.LoadFromResource ("idle-logo.png");
            }
            
            if (idle_pixbuf == null) {
                return;
            }
            
            RenderWindow.DrawPixbuf (Style.BackgroundGC (StateType.Normal), idle_pixbuf, 0, 0, 
                (Allocation.Width - idle_pixbuf.Width) / 2, (Allocation.Height - idle_pixbuf.Height) / 2, 
                idle_pixbuf.Width, idle_pixbuf.Height, Gdk.RgbDither.Normal, 0, 0);
        }
        
        private bool DrawCoverArt ()
        {
            TrackInfo track = ServiceManager.PlayerEngine.CurrentTrack;
            if (track == null) {
                return false;
            }
            
            Gdk.Pixbuf display_pixbuf = null;
            
            if (track.ArtworkId == last_coverart_id) {
                display_pixbuf = last_coverart_pixbuf;
            } else if (Banshee.Base.CoverArtSpec.CoverExists (track.ArtworkId)) {
                if (last_coverart_pixbuf != null) {
                    last_coverart_pixbuf.Dispose ();
                }
                last_coverart_id = track.ArtworkId;
                display_pixbuf = last_coverart_pixbuf = new Gdk.Pixbuf (Banshee.Base.CoverArtSpec.GetPath (last_coverart_id));
            } else {
                return false;
            }
            
            int img_w = Math.Min (Allocation.Width, display_pixbuf.Width);
            int img_h = Math.Min (Allocation.Height, display_pixbuf.Height);
            RenderWindow.DrawPixbuf (Style.BackgroundGC (StateType.Normal), display_pixbuf, 0, 0, 
                (Allocation.Width - img_w) / 2, (Allocation.Height - img_h) / 2, 
                img_w, img_h, Gdk.RgbDither.Normal, 0, 0
            );
            return true;
        }
        
        private void OnPlayerEvent (PlayerEventArgs args)
        {
            ToggleIdleVisibility ();
        }
        
        private void ToggleIdleVisibility ()
        {
            TrackInfo track = ServiceManager.PlayerEngine.CurrentTrack;
            render_idle = track == null;
            render_video = !render_idle && (track.MediaAttributes & TrackMediaAttributes.VideoStream) != 0;
            QueueDraw ();
        }
        
        public new void QueueDraw ()
        {
            base.QueueDraw ();
            if (RenderWindow != null) {
                RenderWindow.InvalidateRect (new Gdk.Rectangle (0, 0, Allocation.Width, Allocation.Height), true);
            }
        }
    }
}
