﻿namespace UserInterface.Views
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using APSIM.Shared.Utilities;
    using System.Globalization;

    /// <summary>
    /// Describes an interface for an axis view.
    /// </summary>
    interface IMapView
    {
        /// <summary>
        /// Invoked when the zoom level or map center is changed
        /// </summary>
        event EventHandler ViewChanged;

        /// <summary>
        /// Invoked when the user wants to preview the map as it will appear
        /// in the autodocs.
        /// </summary>
        event EventHandler PreviewDocs;

        /// <summary>Show the map</summary>
        void ShowMap(List<Models.Map.Coordinate> coordinates, List<string> locNames, double zoom, Models.Map.Coordinate center);

        /// <summary>Export the map to an image.</summary>
        Image Export();
        /// <summary>
        /// Get or set the zoom factor of the map
        /// </summary>
        double Zoom { get; set; }

        /// <summary>
        /// Get or set the center position of the map
        /// </summary>
        Models.Map.Coordinate Center { get; set; }

        /// <summary>
        /// Store current position and zoom settings
        /// </summary>
        void StoreSettings();

        /// <summary>
        /// Hide zoom controls.
        /// </summary>
        void HideZoomControls();
    }

    /// It would be good if we could retrieve the current center and zoom values for a map,
    /// and store them as part of the Map object, so that maps can be recreated and exported
    /// using those settings. 
    /// Google map readily allows the center and zoom values to be obtained in JavaScript, and
    /// provides event handlers for when those values change, but the problem is getting those
    /// values back to the hosting application. With IE, it should be possible to use
    /// the ObjectForScripting approach. For Webkit, it may be a bit harder. See
    /// http://stackoverflow.com/questions/9804360/how-to-call-javascript-from-monos-webkitsharp
    /// for a workaround using the document title as a mechanism for receiving information. Webkit
    /// does provide a listener for title changes.
    /// 
    /// <summary>
    /// A Windows forms implementation of an AxisView
    /// </summary>
    public class MapView : HTMLView, IMapView
    {
        private double zoom = 1.0;
        private Gtk.Button previewDocsButton;

        /// <summary>
        /// Latitude of the center position.
        /// </summary>
        /// <remarks>
        /// It would be more convenient to use a Coordinate object,
        /// but then we run into shallow copy issues because the presenter
        /// does this.view.Center = map.Center; which means that
        /// modifying the center object will modify the map's center
        /// object, meaning the user dragging the map around won't
        /// be undo-able (or redoable).
        /// </remarks>
        private double latitude;

        /// <summary>
        /// Longitude of the center position.
        /// </summary>
        /// <remarks>
        /// See remarks on latitude.
        /// </remarks>
        private double longitude;

        /// <summary>
        /// Invoked when the zoom level or map center is changed
        /// </summary>
        public event EventHandler ViewChanged;

        /// <summary>
        /// Invoked when the user wants to preview the map as it will appear
        /// in the autodocs.
        /// </summary>
        public event EventHandler PreviewDocs;

        /// <summary>Construtor</summary>
        public MapView(ViewBase owner) : base(owner)
        {
            if (owner != null)
            {
                previewDocsButton = new Gtk.Button("Preview Map Documentation");
                previewDocsButton.HasTooltip = true;
                previewDocsButton.TooltipText = "Click to show a preview of how the map will appear in the auto-generated documentation";
                previewDocsButton.Clicked += OnPreviewDocsClicked;
                vbox2.PackEnd(previewDocsButton, false, false, 0);
            }

            MainWidget.Destroyed += OnMainWidgetDestroyed;
        }

        private void OnMainWidgetDestroyed(object sender, EventArgs e)
        {
            MainWidget.Destroyed -= OnMainWidgetDestroyed;

            if (previewDocsButton != null)
                previewDocsButton.Clicked -= OnPreviewDocsClicked;
        }

        private void OnPreviewDocsClicked(object sender, EventArgs e)
        {
            try
            {
                PreviewDocs?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>Show the map</summary>
        public void ShowMap(List<Models.Map.Coordinate> coordinates, List<string> locNames, double zoom, Models.Map.Coordinate center)
        {
            string html =
@"<!DOCTYPE html>
<html>
<meta charset=""UTF-8"">
<head>
   <link rel=""stylesheet"" href=""https://unpkg.com/leaflet@1.6.0/dist/leaflet.css""
            integrity=""sha512-xwE/Az9zrjBIphAcBb3F6JVqxf46+CDLwfLMHloNu6KEQCAWi6HcDUbeOfBIptF7tcCzusKFjFw2yuvEpDL9wQ==""
            crossorigin=""""/>  
   <!-- Make sure you put this AFTER Leaflet's CSS -->
   <script type=""text/javascript"" src=""https://unpkg.com/leaflet@1.6.0/dist/leaflet.js""
         integrity=""sha512-gZwIG9x3wUXg2hdXF6+rVkLF/0Vi9U8D2Ntg4Ga5I5BZpVkVxlJWbSQtXPSiUTtC0TjtGOmxa1AJPuV0CPthew==""
         crossorigin=""""></script>
</head>
<body>
  <!--Make sure you put this AFTER Leaflet's CSS -->

  <div id='mapid' style='position:fixed; top:0; bottom:0; left:0; right:0;' ></div>";

            html += @"
  <script>
    var locations = [";

            for (int i = 0; i < coordinates.Count; i++)
            {
                html += "[" + coordinates[i].Latitude.ToString(CultureInfo.InvariantCulture) + ", " + coordinates[i].Longitude.ToString(CultureInfo.InvariantCulture) + ", '" + locNames[i] + "']";
                if (i < coordinates.Count - 1)
                    html += ',';
            }
            html += "];" + Environment.NewLine;

            html += "    var mymap = L.map('mapid', {";
            html += "center: new L.LatLng(" + center.Latitude.ToString(CultureInfo.InvariantCulture) + ", " + center.Longitude.ToString(CultureInfo.InvariantCulture) + ")";
            html += ", zoom: " + zoom.ToString(CultureInfo.InvariantCulture);
            html += "});";


           html += @"

    mymap.zoomDelta = 0.1;
    L.tileLayer('https://api.mapbox.com/styles/v1/{id}/tiles/{z}/{x}/{y}?access_token=pk.eyJ1IjoiZXJpY3p1cmNoZXIiLCJhIjoiY2s5YzE2d3liMDBkMDNmbnN2cXhxOHQ2dCJ9.qN8AvphLYMMFSVHKbi7EAg', {
        maxZoom: 18,
        attribution: 'Map data &copy; <a href=""http://openstreetmap.org"">OpenStreetMap</a> contributors, ' +
        '<a href=""http://creativecommons.org/licenses/by-sa/2.0/"">CC-BY-SA</a>, ' +
        'Imagery © <a href=""http://mapbox.com"">Mapbox</a>',
        tileSize: 512,
        zoomOffset: -1,
        id: 'mapbox/outdoors-v11'
    }).addTo(mymap);

    L.control.scale({metric: true, imperial: false, updateWhenIdle: true}).addTo(mymap);

    var marker, i;
    for (i = 0; i<locations.length; i++)
    {
        L.marker(locations[i]).addTo(mymap).bindPopup('<b>' + locations[i][2] + '</b><br>Latitude: ' + locations[i][0] + '<br>Longitude: ' + locations[i][1]);
    }

    function SetTitle()
    {
	    var center = mymap.getCenter().wrap();
	    window.document.title = mymap.getZoom().toString() + ', (' + center.lat.toString() + ', ' + center.lng.toString() + ')';
    }
    function SetZoom(newZoom)
    {
        mymap.setZoom(newZoom);
    }
    function SetCenter(lat, long)
    {
        var center = new L.LatLng(lat, long);
        mymap.panTo(center);
    }

    function HideZoomControls()
    {
        mymap.removeControl(mymap.zoomControl);
    }

    mymap.on('zoomend', SetTitle);
    mymap.on('moveend', SetTitle);
	
    var popup = L.popup();
  </script>

</body>
</html>";
            SetContents(html, false);
        }

        /// <summary>
        /// Export the map to an image.
        /// </summary>
        public Image Export()
        {
            // Create a Bitmap and draw the DataGridView on it.
            int width;
            int height;
            Gdk.Window gridWindow = MainWidget.GdkWindow;
            gridWindow.GetSize(out width, out height);
            if (ProcessUtilities.CurrentOS.IsWindows)
            {
                // Give the browser half a second to run all its scripts
                // It would be better if we could tap into the browser's Javascript engine
                // and see whether loading of the map was complete, but my attempts to do
                // so were not entirely successful.
                Stopwatch watch = new Stopwatch();
                watch.Start();
                while (watch.ElapsedMilliseconds < 500)
                    Gtk.Application.RunIteration();
                if ((browser as TWWebBrowserIE) != null)
                {
                    System.Windows.Forms.WebBrowser wb = (browser as TWWebBrowserIE).Browser;
                    System.Drawing.Bitmap bm = new System.Drawing.Bitmap(width, height);
                    System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, width, height);
                    wb.DrawToBitmap(bm, rect);
                    return bm;
                }

            }
            Gdk.Pixbuf screenshot = Gdk.Pixbuf.FromDrawable(gridWindow, gridWindow.Colormap, 0, 0, 0, 0, width, height);
            byte[] buffer = screenshot.SaveToBuffer("png");
            MemoryStream stream = new MemoryStream(buffer);
            System.Drawing.Bitmap bitmap = new Bitmap(stream);
            return bitmap;
        }

        /// <summary>
        /// Get or set the zoom factor of the map
        /// </summary>
        public double Zoom
        {
            get
            {
                return zoom;
            }
            set
            {
                zoom = Math.Truncate(value + 0.5);
                browser.ExecJavaScript("SetZoom", new object[] { (int)zoom });

                // With WebKit, it appears we need to give it time to actually update the display
                // Really only a problem with the temporary windows used for generating documentation
                if (owner == null)
                {
                    Stopwatch watch = new Stopwatch();
                    watch.Start();
                    while (watch.ElapsedMilliseconds < 500)
                        Gtk.Application.RunIteration();
                }
            }
        }

        /// <summary>
        /// Get or set the center position of the map
        /// </summary>
        public Models.Map.Coordinate Center
        {
            get
            {
                return new Models.Map.Coordinate(latitude, longitude);
            }
            set
            {
                latitude = value.Latitude;
                longitude = value.Longitude;
                browser.ExecJavaScript("SetCenter", new object[] { value.Latitude, value.Longitude });

                // With WebKit, it appears we need to give it time to actually update the display
                // Really only a problem with the temporary windows used for generating documentation
                if (owner == null)
                {
                    Stopwatch watch = new Stopwatch();
                    watch.Start();
                    while (watch.ElapsedMilliseconds < 500)
                        Gtk.Application.RunIteration();
                }
            }
        }

        public void StoreSettings()
        {
            NewTitle(browser.GetTitle());
        }

        public void HideZoomControls()
        {
            browser.ExecJavaScript("HideZoomControls", new object[0]);
        }

        protected override void NewTitle(string title)
        {
            if (!String.IsNullOrEmpty(title))
            {
                double newLat, newLong, newZoom;
                bool modified = false;
                // Incoming title should look like "6, (-27.15, 151.25)"
                // That is Zoom, then lat, long pair
                // We remove the brackets and split on the commas
                title = title.Replace("(", "");
                title = title.Replace(")", "");
                string[] parts = title.Split(new char[] { ',' });
                if (Double.TryParse(parts[0], out newZoom) && newZoom != zoom)
                {
                    zoom = newZoom;
                    modified = true;
                }
                if (Double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out newLat) &&
                    Double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out newLong) &&
                    (newLat != latitude || newLong != longitude))
                {
                    Center = new Models.Map.Coordinate(newLat, newLong);
                    modified = true;
                }
                if (modified && ViewChanged != null)
                    ViewChanged.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
